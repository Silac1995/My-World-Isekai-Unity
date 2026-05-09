using System.Collections.Generic;
using UnityEngine;

public class CharacterStatusEffectInstance
{
    private string statusEffectName;
    private List<StatusEffectInstance> statusEffectInstances;
    private float remainingDuration; // 0 = permanent
    private bool isPermanent;
    private GameObject visualEffectInstance; 
    private Sprite icon;
    private string description;
    private Character caster;
    private Character target;
    private CharacterStatusEffect sourceAsset;
    private bool _isSuspended = false;
    private float _suspendCheckTimer = 0f;
    private const float SUSPEND_CHECK_INTERVAL = 1f;

    public string StatusEffectName => statusEffectName;
    public CharacterStatusEffect SourceAsset => sourceAsset;
    public IReadOnlyList<StatusEffectInstance> StatusEffectInstances => statusEffectInstances.AsReadOnly();
    public float RemainingDuration => remainingDuration;
    public bool IsPermanent => isPermanent;
    public GameObject VisualEffectInstance => visualEffectInstance; 
    public Sprite Icon => icon;
    public string Description => description;
    public Character Caster => caster;
    public Character Target => target;
    public bool IsSuspended => _isSuspended;

    public CharacterStatusEffectInstance(CharacterStatusEffect effectAsset, Character caster, Character target)
    {
        this.sourceAsset = effectAsset;
        statusEffectName = effectAsset.StatusEffectName;
        remainingDuration = effectAsset.Duration;
        isPermanent = (remainingDuration <= 0);
        icon = effectAsset.Icon;
        description = effectAsset.Description;
        this.caster = caster;
        this.target = target;

        statusEffectInstances = new List<StatusEffectInstance>();

        foreach (var effect in effectAsset.StatusEffects)
        {
            if (effect is StatModifierEffect statModifierEffect)
            {
                var instance = new StatModifierEffectInstance(statModifierEffect, caster, target);
                statusEffectInstances.Add(instance);
            }
            else if (effect is PeriodicStatEffect periodicEffect)
            {
                var instance = new PeriodicStatEffectInstance(periodicEffect, caster, target);
                statusEffectInstances.Add(instance);
            }
        }

        if (effectAsset.VisualEffectPrefab != null && target != null)
        {
            visualEffectInstance = Object.Instantiate(effectAsset.VisualEffectPrefab, target.transform);
        }
    }

    public void Apply()
    {
        foreach (var instance in statusEffectInstances)
        {
            instance.Apply();
        }
    }

    public void Remove()
    {
        foreach (var instance in statusEffectInstances)
        {
            instance.Remove();
        }

        if (visualEffectInstance != null)
        {
            Object.Destroy(visualEffectInstance);
        }
    }

    public void RefreshDuration()
    {
        remainingDuration = sourceAsset.Duration;
    }

    public bool Tick(float deltaTime)
    {
        // 1. Evaluate suspend condition (once per second — anti-chatter)
        if (sourceAsset.HasSuspendCondition && target != null)
        {
            _suspendCheckTimer += deltaTime;
            if (_suspendCheckTimer >= SUSPEND_CHECK_INTERVAL)
            {
                _suspendCheckTimer = 0f;
                bool shouldSuspend = sourceAsset.SuspendCondition.Evaluate(target.Stats);

                if (shouldSuspend && !_isSuspended)
                {
                    _isSuspended = true;
                    foreach (var effect in statusEffectInstances)
                        effect.Suspend();
                }
                else if (!shouldSuspend && _isSuspended)
                {
                    _isSuspended = false;
                    foreach (var effect in statusEffectInstances)
                        effect.Resume();
                }
            }
        }

        // 2. Tick child effects (only if NOT suspended)
        if (!_isSuspended)
        {
            foreach (var instance in statusEffectInstances)
                instance.Tick(deltaTime);
        }

        // 3. Duration ALWAYS decrements
        if (isPermanent) return false;
        remainingDuration -= deltaTime;
        return remainingDuration <= 0;
    }
}
