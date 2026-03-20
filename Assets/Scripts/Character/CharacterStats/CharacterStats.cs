using System;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

[System.Serializable]
public class CharacterStats : MonoBehaviour
{
    public event Action OnStatsUpdated;

    [Header("References")]
    [SerializeField] private Character character;
    [SerializeField] private CharacterEquipment equipment;

    [Space(10)]
    [Header("Primary Stats")]
    [SerializeField] private CharacterHealth health;
    [SerializeField] private CharacterStamina stamina;
    [SerializeField] private CharacterMana mana;
    [SerializeField] private CharacterInitiative initiative;

    [Space(10)]
    [Header("Secondary Stats")]
    [SerializeField] private CharacterStrength strength;
    [SerializeField] private CharacterAgility agility;
    [SerializeField] private CharacterDexterity dexterity;
    [SerializeField] private CharacterIntelligence intelligence;
    [SerializeField] private CharacterEndurance endurance;
    [SerializeField] private CharacterCharisma charisma;

    [Space(10)]
    [Header("Tertiary Stats")]
    [SerializeField] private PhysicalPower physicalPower;
    [SerializeField] private Speed speed;
    [SerializeField] private DodgeChance dodgeChance;
    [SerializeField] private Accuracy accuracy;
    [SerializeField] private CastingSpeed castingSpeed;
    [SerializeField] private MagicalPower magicalPower;
    [SerializeField] private ManaRegenRate manaRegenRate;
    [SerializeField] private StaminaRegenRate staminaRegenRate;
    [SerializeField] private CriticalHitChance criticalHitChance;
    [SerializeField] private MoveSpeed moveSpeed;



    // === Getters publics ===

    public Character Character => character;

    public CharacterHealth Health => health;
    public CharacterStamina Stamina => stamina;
    public CharacterMana Mana => mana;
    public CharacterInitiative Initiative => initiative;

    public CharacterStrength Strength => strength;
    public CharacterAgility Agility => agility;
    public CharacterDexterity Dexterity => dexterity;
    public CharacterIntelligence Intelligence => intelligence;
    public CharacterEndurance Endurance => endurance;
    public CharacterCharisma Charisma => charisma;

    public PhysicalPower PhysicalPower => physicalPower;
    public Speed Speed => speed;
    public DodgeChance DodgeChance => dodgeChance;
    public Accuracy Accuracy => accuracy;
    public CastingSpeed CastingSpeed => castingSpeed;
    public MagicalPower MagicalPower => magicalPower;
    public ManaRegenRate ManaRegenRate => manaRegenRate;
    public StaminaRegenRate StaminaRegenRate => staminaRegenRate;
    public CriticalHitChance CriticalHitChance => criticalHitChance;
    public MoveSpeed MoveSpeed => moveSpeed;


    private void Awake()
    {
        CreateStats();
        RecalculateTertiaryStats();
    }

    private void CreateStats()
    {
        // Secondary
        strength = new CharacterStrength(this, 1f);
        agility = new CharacterAgility(this, 1f);
        dexterity = new CharacterDexterity(this, 1f);
        intelligence = new CharacterIntelligence(this, 1f);
        endurance = new CharacterEndurance(this, 1f);
        charisma = new CharacterCharisma(this, 1f);

        // Primary (Defaulted to 1s, overwritten by RaceSO quickly)
        health = new CharacterHealth(this, endurance, 1f, 0f);
        mana = new CharacterMana(this, intelligence, 1f, 0f);
        stamina = new CharacterStamina(this, endurance, 1f, 0f);
        initiative = new CharacterInitiative(this, 0f); // Default 0 offset

        // Tertiary (Defaulted to 1s, overwritten by RaceSO quickly)
        physicalPower = new PhysicalPower(this, strength, 1f);
        speed = new Speed(this, agility, 1f);
        dodgeChance = new DodgeChance(this, agility, 1f);
        accuracy = new Accuracy(this, dexterity, 1f);
        castingSpeed = new CastingSpeed(this, dexterity, 1f);
        magicalPower = new MagicalPower(this, intelligence, 1f);
        manaRegenRate = new ManaRegenRate(this, intelligence, 1f);
        staminaRegenRate = new StaminaRegenRate(this, endurance, 1f);
        criticalHitChance = new CriticalHitChance(this, dexterity, 1f);
        moveSpeed = new MoveSpeed(this, agility, 1f, 0f);
    }

    public void InitializeStats(float health, float mana, float strength, float agility)
    {

        // Primary
        Health.SetBaseValue(health);
        Mana.SetBaseValue(mana);

        // Secondary
        Strength.SetBaseValue(strength);
        Agility.SetBaseValue(agility);

        // Recalcul des stats dérivées (primaires dynamiques et tertiaires)
        RecalculateTertiaryStats();
    }

    public void ApplyRaceStats(RaceSO race)
    {
        if (race == null) return;

        // Secondaries Base
        Strength.SetBaseValue(race.BaseStrength);
        Agility.SetBaseValue(race.BaseAgility);
        Dexterity.SetBaseValue(race.BaseDexterity);
        Intelligence.SetBaseValue(race.BaseIntelligence);
        Endurance.SetBaseValue(race.BaseEndurance);
        Charisma.SetBaseValue(race.BaseCharisma);

        // Primary Scaling Update
        Health.UpdateScaling(race.HealthMultiplier, race.BaseHealthOffset);
        Stamina.UpdateScaling(race.StaminaMultiplier, race.BaseStaminaOffset);
        Mana.UpdateScaling(race.ManaMultiplier, race.BaseManaOffset);
        Initiative.UpdateScaling(1f, race.BaseInitiative);
        
        // Tertiary Scaling Update
        PhysicalPower.UpdateScaling(race.PhysicalPowerMultiplier, race.BasePhysicalPowerOffset);
        Speed.UpdateScaling(race.SpeedMultiplier, race.BaseSpeedOffset);
        DodgeChance.UpdateScaling(race.DodgeChanceMultiplier, race.BaseDodgeChanceOffset);
        Accuracy.UpdateScaling(race.AccuracyMultiplier, race.BaseAccuracyOffset);
        CastingSpeed.UpdateScaling(race.CastingSpeedMultiplier, race.BaseCastingSpeedOffset);
        MagicalPower.UpdateScaling(race.MagicalPowerMultiplier, race.BaseMagicalPowerOffset);
        ManaRegenRate.UpdateScaling(race.ManaRegenRateMultiplier, race.BaseManaRegenRateOffset);
        StaminaRegenRate.UpdateScaling(race.StaminaRegenRateMultiplier, race.BaseStaminaRegenRateOffset);
        CriticalHitChance.UpdateScaling(race.CriticalHitChanceMultiplier, race.BaseCriticalHitChanceOffset);
        MoveSpeed.UpdateScaling(race.MoveSpeedMultiplier, race.BaseMoveSpeedOffset);

        RecalculateTertiaryStats();
    }





    public void RecalculateTertiaryStats()
    {
        // Primaires dynamiques
        health.UpdateFromLinkedStat();
        mana.UpdateFromLinkedStat();
        stamina.UpdateFromLinkedStat();

        // Tertiaires
        physicalPower.UpdateFromLinkedStat();
        speed.UpdateFromLinkedStat();
        dodgeChance.UpdateFromLinkedStat();
        accuracy.UpdateFromLinkedStat();
        castingSpeed.UpdateFromLinkedStat();
        magicalPower.UpdateFromLinkedStat();
        manaRegenRate.UpdateFromLinkedStat();
        staminaRegenRate.UpdateFromLinkedStat();
        criticalHitChance.UpdateFromLinkedStat();
        moveSpeed.UpdateFromLinkedStat();

        Debug.Log("<color=green>[Stats]</color> Statistiques dynamiques (Primaires & Tertiaires) recalculées.");
        
        OnStatsUpdated?.Invoke();
    }

    public CharacterBaseStats GetBaseStat(StatType statType)
    {
        return statType switch
        {
            StatType.Health => health,
            StatType.Mana => mana,
            StatType.Stamina => stamina,
            StatType.Initiative => initiative,
            StatType.Strength => strength,
            StatType.Endurance => endurance,
            StatType.Agility => agility,
            StatType.Dexterity => dexterity,
            StatType.Intelligence => intelligence,
            StatType.Charisma => charisma,
            StatType.PhysicalPower => physicalPower,
            StatType.Speed => speed,
            StatType.Dodge => dodgeChance,
            StatType.Accuracy => accuracy,
            StatType.CastingSpeed => castingSpeed,
            StatType.MagicalPower => magicalPower,
            StatType.ManaRegen => manaRegenRate,
            StatType.StaminaRegen => staminaRegenRate,
            StatType.CriticalChance => criticalHitChance,
            _ => null
        };
    }

    public float GetSecondaryStatValue(SecondaryStatType statType)
    {
        switch (statType)
        {
            case SecondaryStatType.Strength: return strength.Value;
            case SecondaryStatType.Agility: return agility.Value;
            case SecondaryStatType.Dexterity: return dexterity.Value;
            case SecondaryStatType.Intelligence: return intelligence.Value;
            case SecondaryStatType.Endurance: return endurance.Value;
            case SecondaryStatType.Charisma: return charisma.Value;
            default: return 1f;
        }
    }
}
