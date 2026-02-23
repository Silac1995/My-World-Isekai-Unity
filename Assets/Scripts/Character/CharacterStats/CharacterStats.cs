using System;
using System.Collections.Generic;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

[System.Serializable]
public class CharacterStats : MonoBehaviour
{
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
    [Header("Primary Stat Multipliers")]
    [SerializeField] private float healthMultiplier = 10f;
    [SerializeField] private float staminaMultiplier = 10f;
    [SerializeField] private float manaMultiplier = 10f;

    [Space(10)]
    [Header("Secondary Stats")]
    [SerializeField] private CharacterStrength strength;
    [SerializeField] private CharacterAgility agility;
    [SerializeField] private CharacterDexterity dexterity;
    [SerializeField] private CharacterIntelligence intelligence;
    [SerializeField] private CharacterEndurance endurance;

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

    [Space(10)]
    [Header("Tertiary Stat Multipliers")]
    [SerializeField] private float accuracyMultiplier = 1f;
    [SerializeField] private float castingSpeedMultiplier = 1f;
    [SerializeField] private float dodgeChanceMultiplier = 0.8f;
    [SerializeField] private float magicalPowerMultiplier = 1f;
    [SerializeField] private float manaRegenRateMultiplier = 1f;
    [SerializeField] private float moveSpeedMultiplier = 0.1f;
    [SerializeField] private float physicalPowerMultiplier = 2f;
    [SerializeField] private float speedMultiplier = 1f;
    [SerializeField] private float staminaRegenMultiplier = 1f;
    [SerializeField] private float criticalHitChanceMultiplier = 0.1f;

    // Constantes pour les stats tertiaires de base
    private const float BASE_MOVE_SPEED = 5f; // Vitesse de d??placement de base

    [Space(10)]
    [Header("Status Effects")]
    [SerializeReference] private List<CharacterStatusEffectInstance> characterStatusEffectInstance = new();


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

    public List<CharacterStatusEffectInstance> CharacterStatusEffectInstance => characterStatusEffectInstance;

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

        // Primary
        health = new CharacterHealth(this, endurance, healthMultiplier, 100f);
        mana = new CharacterMana(this, intelligence, manaMultiplier, 50f);
        stamina = new CharacterStamina(this, endurance, staminaMultiplier, 50f);
        initiative = new CharacterInitiative(this, 100f);

        // Tertiary (toujours dérivées)
        physicalPower = new PhysicalPower(this, strength, physicalPowerMultiplier);
        speed = new Speed(this, agility, speedMultiplier);
        dodgeChance = new DodgeChance(this, agility, dodgeChanceMultiplier);
        accuracy = new Accuracy(this, dexterity, accuracyMultiplier);
        castingSpeed = new CastingSpeed(this, dexterity, castingSpeedMultiplier);
        magicalPower = new MagicalPower(this, intelligence, magicalPowerMultiplier);
        manaRegenRate = new ManaRegenRate(this, intelligence, manaRegenRateMultiplier);
        staminaRegenRate = new StaminaRegenRate(this, endurance, staminaRegenMultiplier);
        criticalHitChance = new CriticalHitChance(this, dexterity, criticalHitChanceMultiplier);
        
        // MoveSpeed possède une base fixe de BASE_MOVE_SPEED sur laquelle s'ajoute le bonus d'Agilité
        moveSpeed = new MoveSpeed(this, agility, moveSpeedMultiplier, BASE_MOVE_SPEED);
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



    public void AddCharacterStatusEffects(CharacterStatusEffectInstance effect)
    {
        if (effect == null) return;

        if (!characterStatusEffectInstance.Contains(effect))
        {
            characterStatusEffectInstance.Add(effect);
        }
    }

    public void RemoveCharacterStatusEffects(CharacterStatusEffectInstance effect)
    {
        if (effect == null) return;

        characterStatusEffectInstance.Remove(effect);
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
            default: return 1f;
        }
    }
}
