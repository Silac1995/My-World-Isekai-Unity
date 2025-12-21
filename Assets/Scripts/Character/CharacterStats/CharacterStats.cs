using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class CharacterStats : MonoBehaviour
{
    [SerializeField] private Character character;
    [SerializeField] private CharacterEquipment equipment;

    // === Primary Stats ===
    [SerializeReference] private CharacterHealth health;
    [SerializeReference] private CharacterStamina stamina;
    [SerializeReference] private CharacterMana mana;
    [SerializeReference] private CharacterInitiative initiative;

    // === Secondary Stats ===
    [SerializeReference] private CharacterStrength strength;
    [SerializeReference] private CharacterAgility agility;
    [SerializeReference] private CharacterDexterity dexterity;
    [SerializeReference] private CharacterIntelligence intelligence;
    [SerializeReference] private CharacterEndurance endurance;

    // === Tertiary Stats ===
    [SerializeReference] private PhysicalPower physicalPower;
    [SerializeReference] private Speed speed;
    [SerializeReference] private DodgeChance dodgeChance;
    [SerializeReference] private Accuracy accuracy;
    [SerializeReference] private CastingSpeed castingSpeed;
    [SerializeReference] private MagicalPower magicalPower;
    [SerializeReference] private ManaRegenRate manaRegenRate;
    [SerializeReference] private StaminaRegenRate staminaRegenRate;
    [SerializeReference] private CriticalHitChance criticalHitChance;
    [SerializeReference] private MoveSpeed moveSpeed;

    // === Status Effects ===
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

    // === Constructeur ===
    public CharacterStats(
        Character character,
        float health = 1,
        float mana = 1,
        float stamina = 1,
        float initiative = 1,
        float strength = 1,
        float agility = 1,
        float dexterity = 1,
        float intelligence = 1,
        float endurance = 1
    )
    {
        this.character = character;

        // Primary
        this.health = new CharacterHealth(this, health);
        this.mana = new CharacterMana(this, mana);
        this.stamina = new CharacterStamina(this, stamina);
        this.initiative = new CharacterInitiative(this, initiative);

        // Secondary
        this.strength = new CharacterStrength(this, strength);
        this.agility = new CharacterAgility(this, agility);
        this.dexterity = new CharacterDexterity(this, dexterity);
        this.intelligence = new CharacterIntelligence(this, intelligence);
        this.endurance = new CharacterEndurance(this, endurance);

        // Tertiary
        this.physicalPower = new PhysicalPower(this, 0f);
        this.speed = new Speed(this, 0f);
        this.dodgeChance = new DodgeChance(this, 0f);
        this.accuracy = new Accuracy(this, 0f);
        this.castingSpeed = new CastingSpeed(this, 0f);
        this.magicalPower = new MagicalPower(this, 0f);
        this.manaRegenRate = new ManaRegenRate(this, 0f);
        this.staminaRegenRate = new StaminaRegenRate(this, 0f);
        this.criticalHitChance = new CriticalHitChance(this, 0f);
        this.moveSpeed = new MoveSpeed(this);
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
        // TO DO
        physicalPower.Reset(); // Ajouter d'autres recalculs ici }

    }
}
