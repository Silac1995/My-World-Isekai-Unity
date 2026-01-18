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
        // Primary
        health = new CharacterHealth(this, 1f);
        mana = new CharacterMana(this, 1f);
        stamina = new CharacterStamina(this, 1f);
        initiative = new CharacterInitiative(this, 1f);

        // Secondary
        strength = new CharacterStrength(this, 1f);
        agility = new CharacterAgility(this, 1f);
        dexterity = new CharacterDexterity(this, 1f);
        intelligence = new CharacterIntelligence(this, 1f);
        endurance = new CharacterEndurance(this, 1f);

        // Tertiary (toujours dérivées)
        physicalPower = new PhysicalPower(this);
        speed = new Speed(this);
        dodgeChance = new DodgeChance(this);
        accuracy = new Accuracy(this);
        castingSpeed = new CastingSpeed(this);
        magicalPower = new MagicalPower(this);
        manaRegenRate = new ManaRegenRate(this);
        staminaRegenRate = new StaminaRegenRate(this);
        criticalHitChance = new CriticalHitChance(this);
        moveSpeed = new MoveSpeed(this, 1f);
    }

    public void InitializeStats(float health, float mana, float strength, float agility)
    {

        // Primary
        Health.SetBaseValue(health);
        Mana.SetBaseValue(mana);

        // Secondary
        Strength.SetBaseValue(strength);
        Agility.SetBaseValue(agility);

        // Recalcul des stats dérivées
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
        // Exemple de logique de calcul dérivée :
        // Puissance Physique = Force * 2 + Agilité * 0.5
        physicalPower.SetBaseValue(strength.CurrentValue * 2f + agility.CurrentValue * 0.5f);

        // Vitesse de déplacement = Base + (Agilité / 10)
        moveSpeed.SetBaseValue(moveSpeed.BaseValue + (agility.CurrentValue * 0.1f));

        // Esquive = Agilité * 0.8
        dodgeChance.SetBaseValue(agility.CurrentValue * 0.8f);

        Debug.Log("<color=green>[Stats]</color> Statistiques tertiaires recalculées.");
    }
}
