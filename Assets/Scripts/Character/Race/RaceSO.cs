using UnityEngine;

[CreateAssetMenu(fileName = "New Race", menuName = "Stats/Race")]
public class RaceSO : ScriptableObject
{
    [Header("Basic Information")]
    public string RaceName;
    public string raceName; // Legacy field for CharacterVisual.cs compatibility
    [TextArea] public string Description;
    
    [Header("Identity & Data")]
    public RandomNameGeneratorSO NameGenerator;

    [Header("Visuals")]
    public CharacterVisualPresetSO characterVisualPreset;
    public System.Collections.Generic.List<GameObject> character_prefabs = new();
    
    [Header("Legacy Stats")]
    public float bonusSpeed;

    [Header("Secondary Stats (Base Values)")]
    public float BaseStrength = 10f;
    public float BaseAgility = 10f;
    public float BaseDexterity = 10f;
    public float BaseIntelligence = 10f;
    public float BaseEndurance = 10f;
    public float BaseCharisma = 10f;

    [Header("Primary Stats (Offsets & Multipliers)")]
    public float BaseHealthOffset = 100f;
    public float HealthMultiplier = 10f;

    public float BaseStaminaOffset = 50f;
    public float StaminaMultiplier = 10f;

    public float BaseManaOffset = 50f;
    public float ManaMultiplier = 10f;

    public float BaseInitiative = 100f;
    public float BaseArmor = 0f;

    [Header("Tertiary Stats (Offsets & Multipliers)")]
    [Tooltip("La valeur de base pure ajoutée au calcul final.")]
    public float BaseMoveSpeedOffset = 5f;
    public float MoveSpeedMultiplier = 0.1f;

    public float BasePhysicalPowerOffset = 0f;
    public float PhysicalPowerMultiplier = 2f;

    public float BaseSpeedOffset = 0f;
    public float SpeedMultiplier = 1f;

    public float BaseDodgeChanceOffset = 0f;
    public float DodgeChanceMultiplier = 0.8f;

    public float BaseAccuracyOffset = 0f;
    public float AccuracyMultiplier = 1f;

    public float BaseSpellCastingOffset = 0f;
    public float SpellCastingMultiplier = 1f;

    public float BaseCombatCastingOffset = 0f;
    public float CombatCastingMultiplier = 1f;

    public float BaseMagicalPowerOffset = 0f;
    public float MagicalPowerMultiplier = 1f;

    public float BaseManaRegenRateOffset = 0f;
    public float ManaRegenRateMultiplier = 1f;

    public float BaseStaminaRegenRateOffset = 0f;
    public float StaminaRegenRateMultiplier = 1f;

    public float BaseCriticalHitChanceOffset = 0f;
    public float CriticalHitChanceMultiplier = 0.1f;
}
