using System.Collections.Generic;
using UnityEngine;

public class CharacterStatusManager : CharacterSystem
{
    [Header("Automatic Effects")]
    [SerializeField] private CharacterStatusEffect _unconsciousEffect;
    [SerializeField] private CharacterStatusEffect _outOfCombatEffect;
    [SerializeField] private CharacterStatusEffect _outOfBreathEffect;

    private List<CharacterStatusEffectInstance> _activeEffects = new List<CharacterStatusEffectInstance>();
    private List<CharacterStatusEffectInstance> _effectsToRemove = new List<CharacterStatusEffectInstance>();

    public event System.Action<CharacterStatusEffectInstance> OnStatusEffectAdded;
    public event System.Action<CharacterStatusEffectInstance> OnStatusEffectRemoved;

    public IReadOnlyList<CharacterStatusEffectInstance> ActiveEffects => _activeEffects.AsReadOnly();

    private void Start()
    {
        if (_character != null)
        {
            if (_character.CharacterCombat != null)
            {
                _character.CharacterCombat.OnBattleLeft += HandleBattleLeft;
            }
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (_character != null)
        {
            if (_character.CharacterCombat != null)
            {
                _character.CharacterCombat.OnBattleLeft -= HandleBattleLeft;
            }
        }
    }

    public void ApplyEffect(CharacterStatusEffect effectAsset, Character caster = null)
    {
        if (effectAsset == null) return;

        var existingInstances = _activeEffects.FindAll(i => i.SourceAsset == effectAsset);
        int limit = Mathf.Max(1, effectAsset.MaxStacks);

        if (existingInstances.Count >= limit)
        {
            // We reached the max stacks. Replace the oldest instance with the new one.
            var oldestInstance = existingInstances[0];
            RemoveEffect(oldestInstance);
            Debug.Log($"<color=cyan>[StatusManager]</color> Effect replaced (max stacks reached): {effectAsset.StatusEffectName} on {_character.name}");
        }

        var instance = new CharacterStatusEffectInstance(effectAsset, caster, _character);
        _activeEffects.Add(instance);
        instance.Apply();
        
        Debug.Log($"<color=cyan>[StatusManager]</color> Effect applied: {effectAsset.StatusEffectName} on {_character.name}");

        OnStatusEffectAdded?.Invoke(instance);

        // Passive trigger: OnStatusEffectApplied
        _character.CharacterAbilities?.OnPassiveTriggerEvent(PassiveTriggerCondition.OnStatusEffectApplied, caster, _character);
    }

    public void RemoveEffect(CharacterStatusEffectInstance instance)
    {
        if (instance == null) return;

        if (_activeEffects.Remove(instance))
        {
            Debug.Log($"<color=cyan>[StatusManager]</color> Effect removed: {instance.StatusEffectName} on {_character.name}");
            OnStatusEffectRemoved?.Invoke(instance);
            instance.Remove();
        }
    }

    public void RemoveEffect(CharacterStatusEffect effectAsset)
    {
        if (effectAsset == null) return;

        var instances = _activeEffects.FindAll(i => i.SourceAsset == effectAsset);
        foreach (var instance in instances)
        {
            RemoveEffect(instance);
        }
    }

    public bool HasEffect(CharacterStatusEffect effectAsset)
    {
        return _activeEffects.Exists(i => i.SourceAsset == effectAsset);
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        _effectsToRemove.Clear();

        for (int i = 0; i < _activeEffects.Count; i++)
        {
            if (_activeEffects[i].Tick(deltaTime))
            {
                _effectsToRemove.Add(_activeEffects[i]);
            }
        }

        foreach (var effect in _effectsToRemove)
        {
            RemoveEffect(effect);
        }

        if (_character == null || _character.Stats == null) return;

        // Out of Breath: remove when stamina fully recovers
        if (_outOfBreathEffect != null && HasEffect(_outOfBreathEffect))
        {
            if (_character.Stats.Stamina != null && _character.Stats.Stamina.CurrentAmount >= _character.Stats.Stamina.MaxValue)
            {
                RemoveEffect(_outOfBreathEffect);
            }
        }

        // Out-of-combat regen stop threshold: 50% of max health
        if (_outOfCombatEffect != null && HasEffect(_outOfCombatEffect))
        {
            if (_character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.5f)
            {
                RemoveEffect(_outOfCombatEffect);
            }
        }

        // Wake-up threshold: 30% of max health.
        if (_character.IsUnconscious && _character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.3f)
        {
            _character.WakeUp();
        }
    }

    protected override void HandleIncapacitated(Character character)
    {
        if (_character.IsUnconscious)
        {
            // Only apply regen if we're not in combat (IsInBattle)
            bool isInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
            
            if (!isInBattle && _unconsciousEffect != null && !HasEffect(_unconsciousEffect))
                ApplyEffect(_unconsciousEffect);
        }
    }

    protected override void HandleWakeUp(Character character)
    {
        if (_unconsciousEffect != null && HasEffect(_unconsciousEffect))
            RemoveEffect(_unconsciousEffect);
            
        EvaluateOutOfCombatEffect();
    }

    protected override void HandleCombatStateChanged(bool isCombat)
    {
        if (_character == null || _character.Stats == null) return;

        bool isInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
        
        // --- UNCONSCIOUS REGEN HANDLING (LEAVING COMBAT) ---
        if (!isInBattle && _character.IsUnconscious)
        {
             if (_unconsciousEffect != null && !HasEffect(_unconsciousEffect))
                ApplyEffect(_unconsciousEffect);
        }

        EvaluateOutOfCombatEffect();
    }

    private void EvaluateOutOfCombatEffect()
    {
        if (_character == null || _character.Stats == null) return;

        bool isInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
        bool isCombat = _character.CharacterCombat != null && _character.CharacterCombat.IsCombatMode;

        bool hasEnoughHealth = _character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.5f;
        bool shouldHaveOutOfCombat = !isCombat && !isInBattle && _character.IsAlive() && !_character.IsUnconscious && !hasEnoughHealth;

        if (shouldHaveOutOfCombat)
        {
            if (_outOfCombatEffect != null && !HasEffect(_outOfCombatEffect))
                ApplyEffect(_outOfCombatEffect);
        }
        else
        {
            if (_outOfCombatEffect != null && HasEffect(_outOfCombatEffect))
                RemoveEffect(_outOfCombatEffect);
        }
    }

    public void ApplyOutOfBreathEffect()
    {
        if (_outOfBreathEffect != null && !HasEffect(_outOfBreathEffect))
        {
            ApplyEffect(_outOfBreathEffect);
            Debug.Log($"<color=cyan>[StatusManager]</color> {_character.CharacterName} is Out of Breath!");
        }
    }

    private void HandleBattleLeft()
    {
        // When leaving combat (manager null), if we're unconscious,
        // start regen IMMEDIATELY without waiting for the 7s combat-mode timeout.
        if (_character.IsUnconscious)
        {
            if (_unconsciousEffect != null && !HasEffect(_unconsciousEffect))
            {
                ApplyEffect(_unconsciousEffect);
                Debug.Log($"<color=cyan>[StatusManager]</color> Regeneration started immediately after combat ended for {_character.CharacterName}.");
            }
        }
    }

}
