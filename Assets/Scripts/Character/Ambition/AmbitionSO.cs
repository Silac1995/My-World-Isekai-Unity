using System;
using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Discriminator for AmbitionContext entries. See spec — drives save layer routing.
    /// </summary>
    public enum ContextValueKind
    {
        Character,
        Primitive,
        Enum,
        ItemSO,
        AmbitionSO,
        QuestSO,
        NeedSO,
        Zone
    }

    /// <summary>
    /// Declares one input slot on an AmbitionSO that callers must fill via SetAmbition.
    /// Validation in AmbitionSO.ValidateParameters checks each Required key is present
    /// and matches the declared Kind.
    /// </summary>
    [Serializable]
    public class AmbitionParameterDef
    {
        public string Key;
        public ContextValueKind Kind;
        public bool Required = true;
    }

    /// <summary>
    /// Authored top-level life goal. Holds an ordered chain of QuestSO steps, optional
    /// input parameters filled at SetAmbition time, and the OverridesSchedule flag that
    /// gates whether ambition pursuit can pre-empt the NPC's scheduled work shift.
    /// Subclasses override ValidateParameters to enforce per-ambition rules.
    /// </summary>
    public abstract class AmbitionSO : ScriptableObject
    {
        [SerializeField] private string _displayName;
        [TextArea, SerializeField] private string _description;
        [SerializeField] private Sprite _icon;
        [SerializeField] private bool _overridesSchedule;
        [SerializeField] private List<QuestSO> _quests = new();
        [SerializeField] private List<AmbitionParameterDef> _parameters = new();

        public string DisplayName => _displayName;
        public string Description => _description;
        public Sprite Icon => _icon;
        public bool OverridesSchedule => _overridesSchedule;
        public IReadOnlyList<QuestSO> Quests => _quests;
        public IReadOnlyList<AmbitionParameterDef> Parameters => _parameters;

        /// <summary>
        /// Default validation: every Required parameter present and the value classifies
        /// under the declared Kind. Subclasses can override to add cross-parameter rules
        /// (e.g. Ambition_Murder ensures Target is alive at assignment time).
        /// </summary>
        public virtual bool ValidateParameters(IReadOnlyDictionary<string, object> p)
        {
            if (_parameters == null) return true;
            foreach (var def in _parameters)
            {
                if (def == null || string.IsNullOrEmpty(def.Key)) continue;
                bool has = p != null && p.TryGetValue(def.Key, out var val) && val != null;
                if (def.Required && !has)
                {
                    Debug.LogError($"[AmbitionSO] {name} requires parameter '{def.Key}' but none was provided.");
                    return false;
                }
            }
            return true;
        }
    }
}
