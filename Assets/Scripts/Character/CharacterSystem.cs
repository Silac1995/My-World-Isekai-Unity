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
            _character.Register(this);
        }
    }

    protected virtual void OnDisable()
    {
        if (_character != null)
        {
            _character.Unregister(this);
            _character.OnIncapacitated -= HandleIncapacitated;
            _character.OnWakeUp -= HandleWakeUp;
            _character.OnDeath -= HandleDeath;
            _character.OnCombatStateChanged -= HandleCombatStateChanged;
        }
    }

    /// <summary>
    /// Called when the character falls unconscious OR dies.
    /// </summary>
    protected virtual void HandleIncapacitated(Character character) { }

    /// <summary>
    /// Called when the character wakes up from unconsciousness.
    /// </summary>
    protected virtual void HandleWakeUp(Character character) { }

    /// <summary>
    /// Called only when the character permanently dies.
    /// </summary>
    protected virtual void HandleDeath(Character character) { }

    /// <summary>
    /// Called when the character enters or exits combat stance.
    /// Ideal for stopping movement or cancelling an interaction.
    /// </summary>
    protected virtual void HandleCombatStateChanged(bool inCombat) { }
}
