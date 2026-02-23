using System.Collections.Generic;
using UnityEngine;

public class CharacterStatusManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character _character;

    [Header("Automatic Effects")]
    [SerializeField] private CharacterStatusEffect _unconsciousEffect;
    [SerializeField] private CharacterStatusEffect _outOfCombatEffect;

    private List<CharacterStatusEffectInstance> _activeEffects = new List<CharacterStatusEffectInstance>();
    private List<CharacterStatusEffectInstance> _effectsToRemove = new List<CharacterStatusEffectInstance>();

    public IReadOnlyList<CharacterStatusEffectInstance> ActiveEffects => _activeEffects.AsReadOnly();

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
    }

    private void Start()
    {
        if (_character != null)
        {
            _character.OnUnconsciousChanged += HandleUnconsciousChanged;
            if (_character.CharacterCombat != null)
                _character.CharacterCombat.OnCombatModeChanged += HandleCombatModeChanged;
        }
    }

    private void OnDestroy()
    {
        if (_character != null)
        {
            _character.OnUnconsciousChanged -= HandleUnconsciousChanged;
            if (_character.CharacterCombat != null)
                _character.CharacterCombat.OnCombatModeChanged -= HandleCombatModeChanged;
        }
    }

    public void ApplyEffect(CharacterStatusEffect effectAsset, Character caster = null)
    {
        if (effectAsset == null) return;

        var instance = new CharacterStatusEffectInstance(effectAsset, caster, _character);
        _activeEffects.Add(instance);
        instance.Apply();
        
        Debug.Log($"<color=cyan>[StatusManager]</color> Effet appliqué : {effectAsset.StatusEffectName} sur {_character.name}");

        // Notify Stats (optional if you want to keep the duplicate list, but better to centralize here)
        _character.Stats.AddCharacterStatusEffects(instance);
    }

    public void RemoveEffect(CharacterStatusEffectInstance instance)
    {
        if (instance == null) return;

        if (_activeEffects.Remove(instance))
        {
            Debug.Log($"<color=cyan>[StatusManager]</color> Effet retiré : {instance.StatusEffectName} sur {_character.name}");
            instance.Remove();
            _character.Stats.RemoveCharacterStatusEffects(instance);
        }
    }

    public void RemoveEffect(CharacterStatusEffect effectAsset)
    {
        if (effectAsset == null) return;

        var instance = _activeEffects.Find(i => i.SourceAsset == effectAsset);
        if (instance != null)
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

        // Seuil d'arrêt Regen Hors Combat : 50% de la vie max
        if (_outOfCombatEffect != null && HasEffect(_outOfCombatEffect))
        {
            if (_character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.5f)
            {
                RemoveEffect(_outOfCombatEffect);
            }
        }

        // Seuil de réveil : 30% de la vie max. 
        if (_character.IsUnconscious && _character.Stats.Health.CurrentAmount >= _character.Stats.Health.MaxValue * 0.3f)
        {
            _character.WakeUp();
        }
    }

    private void HandleUnconsciousChanged(bool unconscious)
    {
        if (unconscious)
        {
            // On n'applique la regen que si on n'est pas en combat (IsInBattle)
            bool isInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
            
            if (!isInBattle && _unconsciousEffect != null && !HasEffect(_unconsciousEffect))
                ApplyEffect(_unconsciousEffect);
        }
        else
        {
            if (_unconsciousEffect != null && HasEffect(_unconsciousEffect))
                RemoveEffect(_unconsciousEffect);
        }
    }

    private void HandleCombatModeChanged(bool isCombat)
    {
        if (_character == null || _character.Stats == null) return;

        bool isInBattle = _character.CharacterCombat != null && _character.CharacterCombat.IsInBattle;
        
        // --- GESTION REGEN INCONSCIENT (SORTIE DE COMBAT) ---
        if (!isInBattle && _character.IsUnconscious)
        {
             if (_unconsciousEffect != null && !HasEffect(_unconsciousEffect))
                ApplyEffect(_unconsciousEffect);
        }

        // --- GESTION REGEN HORS COMBAT (MANUEL) ---
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

}
