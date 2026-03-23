using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using MWI.UI.Notifications;

public class CharacterCombatLevel : CharacterSystem
{
    [Header("Progression Settings")]
    [SerializeField] private int _statPointsPerLevel = 5;
    [SerializeField] private int _currentExperience = 0; // Better to start at 0 towards Level 2
    [SerializeField] private int _unassignedStatPoints = 0;

    [Header("XP Balancing")]
    [Tooltip("The total amount of EXP this character yields when defeated (100% HP lost).")]
    [SerializeField] private int _baseExpYield = 50;
    [SerializeField] private float _maxLevelDifference = 10f;
    [SerializeField] private float _maxXpReduction = 0.75f; // Reduces XP up to 75% max for lower targets
    [SerializeField] private float _maxXpBoost = 0.5f;      // Boosts XP up to 50% max for higher targets

    [Header("Level History")]
    [SerializeField] private List<CombatLevelEntry> _levelHistory = new List<CombatLevelEntry>();

    [Header("Notifications")]
    [SerializeField] private ToastNotificationChannel _expToastChannel;
    [SerializeField] private NotificationChannel _statsBadgeChannel;

    public event System.Action OnExperienceChanged;
    public event System.Action OnLevelChanged;

    public int CurrentExperience => _currentExperience;
    public int StatPointsPerLevel => _statPointsPerLevel;
    public int UnassignedStatPoints => _unassignedStatPoints;
    public int BaseExpYield => _baseExpYield;
    
    /// <summary>
    /// The character's level is the sum of his level list.
    /// If the list is empty, starts at Level 1.
    /// </summary>
    public int CurrentLevel => _levelHistory.Count > 0 ? _levelHistory.Count : 1;

    public IReadOnlyList<CombatLevelEntry> LevelHistory => _levelHistory;

    protected override void Awake()
    {
        base.Awake();
        InitializeBaseLevel();
    }

    private void InitializeBaseLevel()
    {
        // Every character starts with level 1. We ensure the history has at least the base level.
        if (_levelHistory.Count == 0)
        {
            _levelHistory.Add(new CombatLevelEntry { LevelIndex = 1 });
        }
    }

    public int GetRequiredExpForNextLevel()
    {
        // Simple scaling formula: 100 for level 2, 150 for level 3, 200 for level 4...
        return 100 + (CurrentLevel - 1) * 50;
    }

    public int CalculateCombatExp(int targetLevel, bool isKill, float damagePercentage, int targetExpYield)
    {
        // Calculate the base EXP deserved based strictly on the % of MaxHP depleted
        float baseExp = targetExpYield * damagePercentage;
        
        // Minor bonus to the player who actually executes the kill
        if (isKill) baseExp += (targetExpYield * 0.1f); // 10% bonus for the killing blow

        int levelDiff = targetLevel - CurrentLevel;
        int absDiff = Mathf.Abs(levelDiff);

        float multiplier = 1f;

        if (levelDiff > 0)
        {
            // Target is higher level: apply boost
            float boostPercent = Mathf.Min(absDiff / _maxLevelDifference, 1f) * _maxXpBoost;
            multiplier += boostPercent; // e.g. up to 1.5x
        }
        else if (levelDiff < 0)
        {
            // Target is lower level: apply malus
            float penaltyPercent = Mathf.Min(absDiff / _maxLevelDifference, 1f) * _maxXpReduction;
            multiplier -= penaltyPercent; // e.g. up to 0.25x
        }

        // Make sure multiplier doesn't drop to 0 or negative
        multiplier = Mathf.Max(0.1f, multiplier);

        int finalExp = Mathf.Max(1, Mathf.RoundToInt(baseExp * multiplier));
        return finalExp;
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0) return;

        if (IsServer)
        {
            AddExperienceLocally(amount);
            SyncExperienceRpc(amount);
        }
    }

    [Rpc(SendTo.Owner)]
    private void SyncExperienceRpc(int amount)
    {
        if (IsServer) return; // Server already predicted it
        AddExperienceLocally(amount);
    }

    private void AddExperienceLocally(int amount)
    {
        _currentExperience += amount;

        // ONLY show Toast if this is the actual local Player playing!
        if (_expToastChannel != null && _character != null && _character.Controller is PlayerController && IsOwner)
        {
            _expToastChannel.Raise(new ToastNotificationPayload($"+{amount} EXP", ToastType.Info, 2f));
        }

        CheckLevelUp();
        OnExperienceChanged?.Invoke();
    }

    private void CheckLevelUp()
    {
        // Check if we have enough experience to reach the next level
        while (_currentExperience >= GetRequiredExpForNextLevel())
        {
            _currentExperience -= GetRequiredExpForNextLevel();
            LevelUp();
        }
    }

    private void LevelUp()
    {
        _unassignedStatPoints += _statPointsPerLevel;
        
        CombatLevelEntry newLevel = new CombatLevelEntry { LevelIndex = CurrentLevel + 1 };
        _levelHistory.Add(newLevel);

        if (_character != null)
        {
            // Heal only on the Server to prevent double-dipping, the DamageRpc or next tick will sync it
            if (IsServer && _character.Stats != null && _character.Stats.Health != null)
            {
                _character.Stats.Health.HealPercent(0.3f);
            }

            Debug.Log($"<color=yellow>[Progression]</color> {_character.CharacterName} a atteint le niveau Combat {CurrentLevel} ! Points restants : {_unassignedStatPoints}");
            
            if (_character.Controller is PlayerController && IsOwner)
            {
                if (_expToastChannel != null)
                {
                    _expToastChannel.Raise(new ToastNotificationPayload($"Level Up! Combat Level {CurrentLevel}", ToastType.Success, 4f, "Progression"));
                }
                
                if (_statsBadgeChannel != null && _unassignedStatPoints > 0)
                {
                    _statsBadgeChannel.Raise();
                }
            }
            else if (IsServer && !(_character.Controller is PlayerController))
            {
                AutoAllocateStats();
            }
        }
        
        OnLevelChanged?.Invoke();
    }

    private void AutoAllocateStats()
    {
        if (_character == null || _character.Stats == null) return;

        StatType[] coreStats = new StatType[] 
        { 
            StatType.Strength, StatType.Agility, StatType.Dexterity, 
            StatType.Intelligence, StatType.Endurance, StatType.Charisma 
        };

        while (_unassignedStatPoints > 0)
        {
            StatType randomStat = coreStats[UnityEngine.Random.Range(0, coreStats.Length)];
            SpendStatPoint(randomStat);
        }
    }

    public bool SpendStatPoint(StatType statType)
    {
        if (_unassignedStatPoints <= 0) return false;
        if (_character == null || _character.Stats == null) return false;

        CharacterBaseStats stat = _character.Stats.GetBaseStat(statType);
        if (stat != null)
        {
            stat.IncreaseBaseValue(1f);
            _unassignedStatPoints--;
            
            if (_unassignedStatPoints <= 0 && _statsBadgeChannel != null)
            {
                _statsBadgeChannel.Clear();
            }
            
            return true;
        }
        return false;
    }

    public void AddLevel(CombatLevelEntry newLevel)
    {
        newLevel.LevelIndex = CurrentLevel + 1;
        _levelHistory.Add(newLevel);
    }
}
