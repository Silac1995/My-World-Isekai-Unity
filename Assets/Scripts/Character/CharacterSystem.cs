using UnityEngine;

using Unity.Netcode;

/// <summary>
/// Base class for all sub-components that make up a Character. 
/// Automatically handles references and core lifecycle events (Incapacitation, Death, WakeUp) 
/// so subsystems don't need to manually subscribe or be strictly managed by Character.cs.
/// </summary>
public abstract class CharacterSystem : NetworkBehaviour
{
    [SerializeField] protected Character _character;

    public Character Character => _character;

    protected virtual void Awake()
    {
        if (_character == null)
        {
            _character = GetComponentInParent<Character>();
        }
    }

    protected virtual void OnEnable()
    {
        if (_character != null)
        {
            _character.OnIncapacitated += HandleIncapacitated;
            _character.OnWakeUp += HandleWakeUp;
            _character.OnDeath += HandleDeath;
            _character.OnCombatStateChanged += HandleCombatStateChanged;
        }
    }

    protected virtual void OnDisable()
    {
        if (_character != null)
        {
            _character.OnIncapacitated -= HandleIncapacitated;
            _character.OnWakeUp -= HandleWakeUp;
            _character.OnDeath -= HandleDeath;
            _character.OnCombatStateChanged -= HandleCombatStateChanged;
        }
    }

    /// <summary>
    /// Appelé quand le personnage tombe inconscient OU meurt.
    /// </summary>
    protected virtual void HandleIncapacitated(Character character) { }

    /// <summary>
    /// Appelé quand le personnage se réveille d'une perte de connaissance.
    /// </summary>
    protected virtual void HandleWakeUp(Character character) { }

    /// <summary>
    /// Appelé uniquement quand le personnage meurt définitivement.
    /// </summary>
    protected virtual void HandleDeath(Character character) { }

    /// <summary>
    /// Appelé quand le personnage entre ou sort de la posture de combat.
    /// Idéal pour stopper une ligne de déplacement ou annuler une interaction.
    /// </summary>
    protected virtual void HandleCombatStateChanged(bool inCombat) { }
}
