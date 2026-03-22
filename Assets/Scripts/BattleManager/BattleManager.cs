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
    [SerializeField] private Unity.AI.Navigation.NavMeshModifierVolume _battleZoneModifier;
    [SerializeField] private LineRenderer _battleZoneLine;
    [SerializeField] private Vector3 _baseBattleZoneSize = new Vector3(25f, 35f, 10f);
    [SerializeField] private float _perParticipantGrowthRate = 0.3f;
    [SerializeField] private int _participantsPerTier = 6;

    [Header("Initiative System")]
    [SerializeField] private float _ticksPerSecond = 10f;
    private float _tickTimer = 0f;

    private bool _isBattleEnded = false;

    // --- NOUVEAUX CONTRÔLEURS DE DÉLÉGATION ---
    private BattleZoneController _zoneController;
    private CombatEngagementCoordinator _engagementCoordinator;

    // Liste pour le debug
    [SerializeField] private List<Character> _allParticipants = new List<Character>();

    [Header("Debug — Engagements actifs")]
    [SerializeField] private List<string> _debugEngagements = new List<string>();

    public List<BattleTeam> BattleTeams => _teams;
    public bool IsBattleEnded => _isBattleEnded;
    public CombatEngagementCoordinator Coordinator => _engagementCoordinator;

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

        _zoneController = new BattleZoneController(this, _battleZoneModifier, _battleZoneLine, _baseBattleZoneSize, _perParticipantGrowthRate, _participantsPerTier);
        _engagementCoordinator = new CombatEngagementCoordinator(this);

        // 2. Création physique de la zone
        _zoneController.CreateBattleZone(initiator, target);

        // 3. Inscription des participants
        RegisterParticipants();

        // 4. Créer l'engagement initial entre l'initiateur et la cible
        _engagementCoordinator.RequestEngagement(initiator, target);

        // 5. Rendu visuel UNIQUE (pas dans Update)
        _zoneController.DrawBattleZoneOutline();

        Debug.Log($"<color=orange>[Battle]</color> Combat lance : {initiator.name} vs {target.name}");
    }

    private void Update()
    {
        if (_isBattleEnded) return;

        // --- NOUVEAU : VERIFICATION DE FIN DE COMBAT EN CONTINU ---
        // S'assure que le combat s'arrête instantanément même si un objet est détruit
        // silencieusement sans tirer l'événement OnIncapacitated.
        if (_battleTeamInitiator.IsTeamEliminated() || _battleTeamTarget.IsTeamEliminated())
        {
            Debug.Log($"<color=red>[Battle]</color> Elimination globale détectée. Fin du combat.");
            EndBattle();
            return;
        }

        // Gestion du temps de combat (Ticks d'initiative)
        _tickTimer += Time.deltaTime;
        float tickPeriod = 1f / _ticksPerSecond;

        if (_tickTimer >= tickPeriod)
        {
            // Process as many ticks as needed to catch up with Time.deltaTime (especially on 8x speed)
            int ticksToProcess = Mathf.FloorToInt(_tickTimer / tickPeriod);
            
            // Safety cap to prevent lockups on massive lag spikes
            if (ticksToProcess > 30) ticksToProcess = 30;

            for (int i = 0; i < ticksToProcess; i++)
            {
                PerformBattleTick();
            }
            
            _tickTimer -= ticksToProcess * tickPeriod;
        }

#if UNITY_EDITOR
        // Debug : mise à jour de la liste d'engagements pour l'Inspector
        UpdateDebugEngagements();
#endif
    }

#if UNITY_EDITOR
    private void UpdateDebugEngagements()
    {
        _debugEngagements.Clear();
        if (_engagementCoordinator == null || _engagementCoordinator.ActiveEngagements == null) return;

        var engagements = _engagementCoordinator.ActiveEngagements;
        for (int i = 0; i < engagements.Count; i++)
        {
            var e = engagements[i];
            string groupA = string.Join(", ", e.GroupA.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            string groupB = string.Join(", ", e.GroupB.Members
                .Where(m => m != null)
                .Select(m => m.CharacterName));
            _debugEngagements.Add($"[{i}] A: [{groupA}]  vs  B: [{groupB}]");
        }
    }
#endif

    private void PerformBattleTick()
    {
        _engagementCoordinator?.CleanupEngagements();

        foreach (var character in _allParticipants)
        {
            if (character == null || !character.IsAlive()) continue;

            if (character.CharacterCombat != null)
            {
                character.CharacterCombat.UpdateInitiativeTick();
            }
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
                Character bestEnemy = _engagementCoordinator?.GetBestTargetFor(character);
                if (bestEnemy != null)
                {
                    _engagementCoordinator?.RequestEngagement(character, bestEnemy);
                }
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
        if (_zoneController != null)
        {
            int count = _allParticipants.Count(p => p != null);
            _zoneController.UpdateBattleZoneWith(count);
        }
    }

    #region Helpers
    public BattleTeam GetTeamOf(Character character)
    {
        return _teams.FirstOrDefault(t => t.CharacterList.Contains(character));
    }

    public BattleTeam GetOpponentTeamOf(Character character)
    {
        BattleTeam myTeam = GetTeamOf(character);
        if (myTeam == null) 
        {
            Debug.LogWarning($"<color=red>[Battle]</color> {character.CharacterName} n'est dans aucune équipe ! Impossible de trouver son opposant.");
            return null;
        }
        return (myTeam == _battleTeamInitiator) ? _battleTeamTarget : _battleTeamInitiator;
    }

    /// <summary>
    /// Passthrough vers le coordinateur pour trouver le meilleur ennemi à cibler.
    /// </summary>
    public Character GetBestTargetFor(Character attacker)
    {
        return _engagementCoordinator?.GetBestTargetFor(attacker);
    }

    public CombatEngagement RequestEngagement(Character attacker, Character target)
    {
        return _engagementCoordinator?.RequestEngagement(attacker, target);
    }
    #endregion

    private void HandleCharacterIncapacitated(Character incapacitatedCharacter)
    {
        if (_isBattleEnded) return;

        // --- NOUVEAU : DIAGNOSTIC DE FIN DE COMBAT ---
        int initiatorAlive = _battleTeamInitiator.CharacterList.Count(c => c != null && c.IsAlive());
        int targetAlive = _battleTeamTarget.CharacterList.Count(c => c != null && c.IsAlive());
        
        string initiatorNames = string.Join(", ", _battleTeamInitiator.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));
        string targetNames = string.Join(", ", _battleTeamTarget.CharacterList.Where(c => c != null && c.IsAlive()).Select(c => c.CharacterName));

        Debug.Log($"<color=white>[Battle Check]</color> {incapacitatedCharacter.CharacterName} tombe. " +
                  $"Team Initiateur : {initiatorAlive}/{_battleTeamInitiator.CharacterList.Count} ({initiatorNames}) | " +
                  $"Team Cible : {targetAlive}/{_battleTeamTarget.CharacterList.Count} ({targetNames})");

        // (La vérification globale d'élimination est maintenant gérée passivement dans l'Update)

        // On libère simplement les slots d'engagements liés à ce personnage.
        RedirectIncapacitated(incapacitatedCharacter);
    }

    private void RedirectIncapacitated(Character victim)
    {
        _engagementCoordinator?.CleanupEngagements();
        // Le BT (BTCond_IsInCombat) se chargera naturellement de recibler au prochain tick.
    }


    public bool AreOpponents(Character a, Character b)
    {
        if (a == null || b == null || a == b) return false;
        
        BattleTeam teamA = GetTeamOf(a);
        BattleTeam teamB = GetTeamOf(b);

        return teamA != null && teamB != null && teamA != teamB;
    }

    public void EndBattle()
    {
        if (_isBattleEnded) return;
        _isBattleEnded = true;

        foreach (var character in _allParticipants)
        {
            if (character != null)
            {
                try 
                {
                    if (character.CharacterCombat != null)
                    {
                        character.CharacterCombat.LeaveBattle();
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"<color=red>[Battle]</color> Exception non-critique durant LeaveBattle pour {character.CharacterName} : {e.Message}. Le nettoyage continue.");
                }
                
                _engagementCoordinator?.LeaveCurrentEngagement(character);
            }
        }
        
        _engagementCoordinator?.ClearAll();

        Debug.Log("<color=red>[Battle]</color> Le combat est TERMINÉ.");
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_allParticipants != null)
        {
            foreach (var character in _allParticipants)
            {
                if (character != null)
                {
                    character.OnIncapacitated -= HandleCharacterIncapacitated;
                    character.OnDeath -= HandleCharacterIncapacitated;
                }
            }
        }
    }

    public void DrawBattleZoneOutline()
    {
        _zoneController?.DrawBattleZoneOutline();
    }
}
