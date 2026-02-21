using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private List<BattleTeam> _teams = new List<BattleTeam>();
    [SerializeField] private BattleTeam _battleTeamInitiator;
    [SerializeField] private BattleTeam _battleTeamTarget;
    [SerializeField] private Collider _battleZone;
    [SerializeField] private LineRenderer _battleZoneLine;
    [SerializeField] private float _padding = 30f;

    private bool _isBattleEnded = false;

    // Liste pour le debug (plus besoin de GetAllCharacters à chaque fois)
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;

    public void Initialize(Character initiator, Character target)
    {
        if (initiator == null || target == null) return;

        // 1. Setup des équipes
        _battleTeamInitiator = new BattleTeam();
        _battleTeamTarget = new BattleTeam();
        _battleTeamInitiator.AddCharacter(initiator);
        _battleTeamTarget.AddCharacter(target);

        _teams.Add(_battleTeamInitiator);
        _teams.Add(_battleTeamTarget);

        // 2. Création physique de la zone
        CreateBattleZone(initiator, target);

        // 3. Inscription des participants
        RegisterParticipants();

        // 4. Rendu visuel UNIQUE (pas dans Update)
        DrawBattleZoneOutline();

        Debug.Log($"<color=orange>[Battle]</color> Combat lancé : {initiator.name} vs {target.name}");
    }

    private void CreateBattleZone(Character a, Character b)
    {
        // On s'assure que le manager est neutre en rotation/scale pour que les calculs de Bounds (AABB) matchent
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // Cleanup : On s'assure qu'il n'y a pas de doublons de colliders qui traînent
        var oldColliders = gameObject.GetComponents<BoxCollider>();
        foreach (var old in oldColliders) Destroy(old);

        Bounds combinedBounds = a.Collider.bounds;
        combinedBounds.Encapsulate(b.Collider.bounds);
        
        // Expand(x) ajoute x au total (donc x/2 de chaque côté). 
        // Pour une marge de 30f de chaque côté, on Expand de 60f.
        combinedBounds.Expand(_padding * 2f);

        // On utilise un BoxCollider pour les limites
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        
        box.isTrigger = true;
        // On fixe la hauteur à 20f pour la zone de détection
        box.size = new Vector3(combinedBounds.size.x, 20f, combinedBounds.size.z);
        
        // On centre le pivot du manager sur le centre des bounds
        transform.position = new Vector3(combinedBounds.center.x, a.transform.position.y, combinedBounds.center.z);

        _battleZone = box;
    }

    private void RegisterParticipants()
    {
        _allParticipants.Clear();

        foreach (var team in _teams)
        {
            foreach (var character in team.CharacterList)
            {
                if (character == null) continue;
                RegisterCharacter(character);
            }
        }
    }

    private void RegisterCharacter(Character character)
    {
        if (_allParticipants.Contains(character)) return;

        _allParticipants.Add(character);

        if (character.CharacterCombat != null)
        {
            character.CharacterCombat.JoinBattle(this);

            if (!character.IsPlayer())
            {
                BattleTeam opponentTeam = GetEnemyTeamOf(character);
                Character closestEnemy = opponentTeam?.GetClosestMember(character.transform.position);
                if (closestEnemy != null)
                    character.Controller.PushBehaviour(new CombatBehaviour(this, closestEnemy));
            }
        }

        character.OnDeath -= HandleCharacterDeath;
        character.OnDeath += HandleCharacterDeath;

        UpdateBattleZoneWith(character);
        Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} a rejoint le combat.");
    }

    public void AddParticipant(Character newParticipant, Character target)
    {
        if (newParticipant == null || target == null || _isBattleEnded) return;

        // On trouve l'équipe de la cible et on met le nouveau dans l'équipe adverse
        BattleTeam targetTeam = _teams.FirstOrDefault(t => t.ContainsCharacter(target));
        if (targetTeam == null) return;

        BattleTeam enemyTeam = _teams.FirstOrDefault(t => t != targetTeam);
        if (enemyTeam == null) return;

        enemyTeam.AddCharacter(newParticipant);
        RegisterCharacter(newParticipant);
    }

    private void UpdateBattleZoneWith(Character character)
    {
        if (_battleZone == null || _allParticipants.Count == 0) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        // 1. On calcule les bounds "bruts" englobant tous les participants actuels
        Bounds rawBounds = new Bounds(_allParticipants[0].Collider.bounds.center, Vector3.zero);
        foreach (var p in _allParticipants)
        {
            if (p != null && p.Collider != null)
                rawBounds.Encapsulate(p.Collider.bounds);
        }

        // 2. On applique le padding UNE SEULE FOIS sur la taille totale
        // On fixe toujours la hauteur à 20f
        box.size = new Vector3(rawBounds.size.x + _padding * 2f, 20f, rawBounds.size.z + _padding * 2f);
        
        // 3. On centre le manager sur le centre géographique des participants
        transform.position = new Vector3(rawBounds.center.x, transform.position.y, rawBounds.center.z);

        DrawBattleZoneOutline();
    }

    private void HandleCharacterDeath(Character deadCharacter)
    {
        if (_isBattleEnded) return;

        // 1. Vérifier si le combat est terminé (UNE des deux équipes principales est éliminée)
        if (_battleTeamInitiator.IsTeamEliminated() || _battleTeamTarget.IsTeamEliminated())
        {
            _isBattleEnded = true;
            EndBattle();
            return;
        }

        // 2. Si le combat continue, on redirige ceux qui tapaient le mort
        RedirectAttackers(deadCharacter);
    }

    private void RedirectAttackers(Character deadTarget)
    {
        foreach (var participant in _allParticipants)
        {
            if (participant == null || !participant.IsAlive()) continue;

            // On récupère le comportement de combat s'il existe
            var combatBehaviour = participant.Controller.GetCurrentBehaviour<CombatBehaviour>();

            if (combatBehaviour != null)
            {
                // On vérifie si l'IA n'a plus de cible OU si sa cible actuelle est celle qui vient de mourir
                if (!combatBehaviour.HasTarget || combatBehaviour.Target == deadTarget)
                {
                    BattleTeam enemyTeam = GetEnemyTeamOf(participant);
                    Character nextTarget = enemyTeam?.GetClosestMember(participant.transform.position);

                    // On donne la nouvelle cible au comportement
                    combatBehaviour.SetCurrentTarget(nextTarget);

                    Debug.Log($"<color=yellow>[Battle]</color> {participant.CharacterName} a perdu sa cible ({deadTarget.CharacterName}) et se tourne vers {nextTarget?.CharacterName}");
                }
            }
        }
    }

    // Petite méthode utilitaire pour trouver l'équipe adverse
    private BattleTeam GetEnemyTeamOf(Character c)
    {
        return _teams.FirstOrDefault(team => !team.ContainsCharacter(c));
    }

    public void EndBattle()
    {
        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                character.OnDeath -= HandleCharacterDeath;
                character.CharacterCombat.LeaveBattle();
            }
        }

        Debug.Log("<color=red>[Battle]</color> Fin du combat.");
        Destroy(gameObject);
    }

    public void DrawBattleZoneOutline()
    {
        if (_battleZoneLine == null || _battleZone == null) return;

        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        _battleZoneLine.useWorldSpace = true;
        _battleZoneLine.loop = true;
        _battleZoneLine.positionCount = 4;

        // Calcul des coins basés sur le BoxCollider
        Vector3 center = box.transform.position;
        Vector3 size = box.size;

        float x = size.x / 2f;
        float z = size.z / 2f;
        float y = center.y; 

        Vector3[] corners = new Vector3[4]
        {
            center + new Vector3(-x, 0, -z),
            center + new Vector3(-x, 0, z),
            center + new Vector3(x, 0, z),
            center + new Vector3(x, 0, -z)
        };

        _battleZoneLine.SetPositions(corners);

        Debug.Log("<color=cyan>[Battle]</color> Outline de combat dessinée au sol.");
    }
}
