using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BattleManager : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private List<BattleTeam> _teams = new List<BattleTeam>();
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
        BattleTeam teamA = new BattleTeam();
        BattleTeam teamB = new BattleTeam();
        teamA.AddCharacter(initiator);
        teamB.AddCharacter(target);

        _teams.Add(teamA);
        _teams.Add(teamB);

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

        for (int i = 0; i < _teams.Count; i++)
        {
            BattleTeam currentTeam = _teams[i];
            // L'adversaire est l'autre équipe (0 -> 1, 1 -> 0)
            BattleTeam opponentTeam = _teams[(i + 1) % _teams.Count];

            foreach (var character in currentTeam.CharacterList)
            {
                if (character == null) continue;

                _allParticipants.Add(character);

                if (character.CharacterCombat != null)
                {
                    character.CharacterCombat.JoinBattle(this);

                    // On ne force pas le comportement de combat si c'est le Joueur
                    if (!character.IsPlayer())
                    {
                        Character randomEnemy = opponentTeam.GetClosestMember(character.transform.position);
                        if (randomEnemy != null)
                        {
                            character.Controller.PushBehaviour(new CombatBehaviour(this, randomEnemy));
                        }
                    }

                    Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} a rejoint le combat.");
                }

                // Gestion de la mort pour vérifier la fin du combat
                character.OnDeath -= HandleCharacterDeath;
                character.OnDeath += HandleCharacterDeath;
            }
        }
    }

    private void HandleCharacterDeath(Character deadCharacter)
    {
        if (_isBattleEnded) return;

        // 1. Vérifier si le combat est terminé (une équipe entière est éliminée)
        foreach (var team in _teams)
        {
            if (team.IsTeamEliminated())
            {
                _isBattleEnded = true;
                EndBattle();
                return;
            }
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
