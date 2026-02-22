using System.Collections.Generic;
using UnityEngine;

public class CharacterStatusManager : MonoBehaviour
{
    [SerializeField] private Character _character;
    private List<CharacterStatusEffectInstance> _activeEffects = new List<CharacterStatusEffectInstance>();
    private List<CharacterStatusEffectInstance> _effectsToRemove = new List<CharacterStatusEffectInstance>();

    public IReadOnlyList<CharacterStatusEffectInstance> ActiveEffects => _activeEffects.AsReadOnly();

    private void Awake()
    {
        if (_character == null) _character = GetComponent<Character>();
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
    }
}
