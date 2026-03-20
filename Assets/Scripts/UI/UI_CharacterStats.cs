using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class UI_CharacterStats : UI_WindowBase
{
    [SerializeField] private Character _character;

    [Header("UI References")]
    [SerializeField] private Transform _slotContainer;
    [SerializeField] private GameObject _statSlotPrefab;
    [SerializeField] private TextMeshProUGUI _unassignedPointsText;

    private List<UI_StatSlot> _spawnedSlots = new List<UI_StatSlot>();

    // We explicitly list the upgradable secondary stats
    private readonly HashSet<StatType> _upgradableStats = new HashSet<StatType>
    {
        StatType.Strength, StatType.Agility, StatType.Dexterity, 
        StatType.Intelligence, StatType.Endurance, StatType.Charisma
    };

    // We explicitly list the stats we want to display.
    private readonly StatType[] _statsToDisplay = new StatType[]
    {
        StatType.Health, StatType.Stamina, StatType.Mana, StatType.Initiative,
        StatType.Strength, StatType.Agility, StatType.Dexterity, StatType.Intelligence, StatType.Endurance, StatType.Charisma,
        StatType.PhysicalPower, StatType.MagicalPower, StatType.Speed, StatType.Dodge, StatType.Accuracy, 
        StatType.CastingSpeed, StatType.ManaRegen, StatType.StaminaRegen, StatType.CriticalChance
    };

    public void Initialize(Character character)
    {
        if (_character != null && _character.Stats != null)
        {
            _character.Stats.OnStatsUpdated -= HandleStatsUpdated;
        }

        _character = character;

        if (_character != null && _character.Stats != null)
        {
            _character.Stats.OnStatsUpdated += HandleStatsUpdated;
            RefreshDisplay();
        }
        else
        {
            ClearSlots();
        }
    }

    private void OnEnable()
    {
        RefreshDisplay();
    }

    private void HandleStatsUpdated()
    {
        if (gameObject.activeInHierarchy)
        {
            RefreshDisplay();
        }
    }

    public void RefreshDisplay()
    {
        if (_character == null || _character.Stats == null || _statSlotPrefab == null || _slotContainer == null) 
            return;

        ClearSlots();

        int unassignedPoints = 0;
        if (_character.CharacterCombatLevel != null)
        {
            unassignedPoints = _character.CharacterCombatLevel.UnassignedStatPoints;
        }
        
        if (_unassignedPointsText != null)
        {
            _unassignedPointsText.text = unassignedPoints > 0 ? $"Unassigned Points: {unassignedPoints}" : "Unassigned Points: 0";
        }

        foreach (StatType statType in _statsToDisplay)
        {
            var statObj = _character.Stats.GetBaseStat(statType);
            if (statObj != null)
            {
                bool canUpgrade = unassignedPoints > 0 && _upgradableStats.Contains(statType);
                CreateSlot(statType, statObj.CurrentValue.ToString("F1"), canUpgrade);
            }
        }
    }

    private void CreateSlot(StatType statType, string statValue, bool canUpgrade)
    {
        GameObject newSlotObj = Instantiate(_statSlotPrefab, _slotContainer);
        UI_StatSlot slotScript = newSlotObj.GetComponent<UI_StatSlot>();
        
        if (slotScript != null)
        {
            slotScript.Setup(statType.ToString(), statValue, canUpgrade, () => OnUpgradeClicked(statType));
            _spawnedSlots.Add(slotScript);
        }
    }
    
    private void OnUpgradeClicked(StatType statType)
    {
        if (_character != null && _character.CharacterCombatLevel != null)
        {
            if (_character.CharacterCombatLevel.SpendStatPoint(statType))
            {
                RefreshDisplay();
            }
        }
    }

    private void ClearSlots()
    {
        foreach (var slot in _spawnedSlots)
        {
            if (slot != null && slot.gameObject != null)
            {
                Destroy(slot.gameObject);
            }
        }
        _spawnedSlots.Clear();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy(); // Always call base if it overrides! Wait, UI_WindowBase has `protected virtual void OnDestroy()`.
        
        if (_character != null && _character.Stats != null)
        {
            _character.Stats.OnStatsUpdated -= HandleStatsUpdated;
        }
    }
}
