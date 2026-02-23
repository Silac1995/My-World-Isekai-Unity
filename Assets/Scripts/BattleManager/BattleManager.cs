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
    [SerializeField] private Vector3 _baseBattleZoneSize = new Vector3(25f, 35f, 10f);
    [SerializeField] private float _perParticipantGrowthRate = 0.3f;
    [SerializeField] private int _maxGrowthTiers = 3;
    [SerializeField] private int _participantsPerTier = 4;

    [Header("Initiative System")]
    [SerializeField] private float _ticksPerSecond = 10f;
    private float _tickTimer = 0f;

    private bool _isBattleEnded = false;

    // Liste pour le debug (plus besoin de GetAllCharacters à chaque fois)
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;

    public void Initialize(Character initiator, Character target)
    {
        if (initiator == null || target == null) return;

        // 1. Setup des équipes (On s'assure qu'il n'y en a QUE 2)
        _teams.Clear();
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

        Debug.Log($"<color=orange>[Battle]</color> Combat lance : {initiator.name} vs {target.name}");
    }

    private void Update()
    {
        if (_isBattleEnded) return;

        // Gestion du temps de combat (Ticks d'initiative)
        _tickTimer += Time.deltaTime;
        float tickPeriod = 1f / _ticksPerSecond;

        if (_tickTimer >= tickPeriod)
        {
            _tickTimer -= tickPeriod;
            PerformBattleTick();
        }
    }

    private void PerformBattleTick()
    {
        foreach (var character in _allParticipants)
        {
            if (character == null || !character.IsAlive()) continue;

            if (character.CharacterCombat != null)
            {
                character.CharacterCombat.UpdateInitiativeTick();
            }
        }
    }

    private void CreateBattleZone(Character a, Character b)
    {
        // On s'assure que le manager est neutre en rotation/scale
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        // Cleanup
        var oldColliders = gameObject.GetComponents<BoxCollider>();
        foreach (var old in oldColliders) Destroy(old);

        // On utilise un BoxCollider pour les limites
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        gameObject.tag = "BattleZone";
        
        box.isTrigger = true;

        // Taille de base définie par l'user
        box.size = _baseBattleZoneSize;
        
        // Position initiale : milieu entre les deux combattants
        Vector3 center = (a.transform.position + b.transform.position) / 2f;
        transform.position = new Vector3(center.x, a.transform.position.y, center.z);

        // --- NOUVEAU : RÉSOLUTION DES SUPERPOSITIONS ---
        ResolveZoneOverlap();

        _battleZone = box;
    }

    private void ResolveZoneOverlap()
    {
        int maxAttempts = 5;
        // On utilise la taille de base pour la détection initiale
        Vector3 halfExtents = _baseBattleZoneSize / 2f;

        for (int i = 0; i < maxAttempts; i++)
        {
            // On cherche d'autres zones de combat (le tag "BattleZone" est crucial ici)
            Collider[] overlaps = Physics.OverlapBox(transform.position, halfExtents, Quaternion.identity);
            bool foundOverlap = false;

            foreach (var other in overlaps)
            {
                // On s'ignore soi-même et on ne cible que les autres BattleZones
                if (other.gameObject != gameObject && other.CompareTag("BattleZone"))
                {
                    foundOverlap = true;
                    
                    // Calcul d'une direction de répulsion (de l'autre vers nous)
                    Vector3 pushDir = (transform.position - other.transform.position);
                    pushDir.y = 0; // On ne décale que sur le plan horizontal
                    
                    if (pushDir.sqrMagnitude < 0.01f) 
                        pushDir = Vector3.right; // Fallback si positions identiques
                    else
                        pushDir.Normalize();

                    // Décalage d'un demi-diamètre pour sortir de la zone de collision
                    float shiftAmount = _baseBattleZoneSize.x * 0.5f;
                    transform.position += pushDir * shiftAmount;
                    
                    Debug.Log($"<color=cyan>[Battle]</color> Superposition detectee ! Decalage de la zone vers {transform.position}");
                    break; // On re-check à la prochaine itération de la boucle for
                }
            }

            if (!foundOverlap) break;
        }
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
            character.CharacterCombat.ConsumeInitiative();

            if (!character.IsPlayer())
            {
                BattleTeam opponentTeam = GetEnemyTeamOf(character);
                Character closestEnemy = opponentTeam?.GetClosestMember(character.transform.position);
                if (closestEnemy != null)
                    character.Controller.PushBehaviour(new CombatBehaviour(this, closestEnemy));
            }
        }

        character.OnIncapacitated -= HandleCharacterIncapacitated;
        character.OnIncapacitated += HandleCharacterIncapacitated;
        character.OnDeath -= HandleCharacterIncapacitated;
        character.OnDeath += HandleCharacterIncapacitated;

        UpdateBattleZoneWith(character);
        Debug.Log($"<color=white>[Battle]</color> {character.CharacterName} a rejoint le combat.");
    }

    public void AddParticipant(Character newParticipant, Character target, bool asAlly = false)
    {
        if (newParticipant == null || target == null || _isBattleEnded) return;

        // On trouve l'équipe de la cible
        BattleTeam targetTeam = GetTeamOf(target);
        if (targetTeam == null) return;

        if (asAlly)
        {
            // On le met dans la MÊME équipe que la cible
            targetTeam.AddCharacter(newParticipant);
        }
        else
        {
            // On le met dans l'équipe ADVERSE de la cible
            BattleTeam enemyTeam = (targetTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
            enemyTeam.AddCharacter(newParticipant);
        }

        RegisterCharacter(newParticipant);
    }

    private void UpdateBattleZoneWith(Character character)
    {
        if (_battleZone == null || _allParticipants.Count == 0) return;
        BoxCollider box = _battleZone as BoxCollider;
        if (box == null) return;

        // 1. Calcul du nombre de participants valides
        int count = _allParticipants.Count(p => p != null);

        // 2. Calcul de la taille par paliers (Ex: chaque 4 persos)
        // Multiplier = 1 + (Nombre de paliers * Taux)
        // On cap le nombre de paliers (max 3 itérations = +90%)
        int tiers = (count - 1) / _participantsPerTier;
        tiers = Mathf.Min(tiers, _maxGrowthTiers);
        
        float multiplier = 1f + (tiers * _perParticipantGrowthRate);

        // On n'applique le multiplicateur qu'à X et Z (le sol). Y reste fixe.
        box.size = new Vector3(_baseBattleZoneSize.x * multiplier, _baseBattleZoneSize.y, _baseBattleZoneSize.z * multiplier);

        DrawBattleZoneOutline();
    }

    #region Helpers
    public BattleTeam GetTeamOf(Character character)
    {
        return _teams.FirstOrDefault(t => t.CharacterList.Contains(character));
    }

    public BattleTeam GetOpponentTeamOf(Character character)
    {
        BattleTeam myTeam = GetTeamOf(character);
        if (myTeam == null) return null;
        return _teams.FirstOrDefault(t => t != myTeam);
    }
    #endregion

    private void HandleCharacterIncapacitated(Character incapacitatedCharacter)
    {
        if (_isBattleEnded) return;

        // 1. Vérifier si le combat est terminé (UNE des deux équipes principales est éliminée)
        // IsTeamEliminated() utilise IsAlive(), qui renvoie désormais false si unconscious ou dead.
        if (_battleTeamInitiator.IsTeamEliminated() || _battleTeamTarget.IsTeamEliminated())
        {
            EndBattle();
            return;
        }

        // 2. Si le combat continue, on redirige ceux qui tapaient l'incapacité
        RedirectIncapacitated(incapacitatedCharacter);
    }

    private void RedirectIncapacitated(Character victim)
    {
        foreach (var participant in _allParticipants)
        {
            if (participant == null || participant.IsIncapacitated) continue;

            var combatBehaviour = participant.Controller.GetCurrentBehaviour<CombatBehaviour>();

            if (combatBehaviour != null)
            {
                if (!combatBehaviour.HasTarget || combatBehaviour.Target == victim)
                {
                    BattleTeam enemyTeam = GetEnemyTeamOf(participant);
                    Character nextTarget = enemyTeam?.GetClosestMember(participant.transform.position);

                    combatBehaviour.SetCurrentTarget(nextTarget);

                    Debug.Log($"<color=yellow>[Battle]</color> {participant.CharacterName} a perdu sa cible ({victim.CharacterName}) et se tourne vers {nextTarget?.CharacterName}");
                }
            }
        }
    }

    // Petite méthode utilitaire pour trouver l'équipe adverse
    private BattleTeam GetEnemyTeamOf(Character c)
    {
        BattleTeam myTeam = GetTeamOf(c);
        if (myTeam == null) return _battleTeamTarget; // Fallback par défaut
        return (myTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
    }

    public bool AreOpponents(Character a, Character b)
    {
        BattleTeam teamA = _teams.FirstOrDefault(t => t.IsAlly(a));
        return teamA != null && teamA.IsOpponent(b, this);
    }

    public void EndBattle()
    {
        if (_isBattleEnded) return;
        _isBattleEnded = true;

        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                character.OnIncapacitated -= HandleCharacterIncapacitated;
                character.OnDeath -= HandleCharacterIncapacitated;
                character.CharacterCombat.LeaveBattle();
            }
        }

        Debug.Log("<color=red>[Battle]</color> Le combat est TERMINÉ.");
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
