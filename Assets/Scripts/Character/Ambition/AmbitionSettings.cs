using System.Collections.Generic;
using UnityEngine;

namespace MWI.Ambition
{
    /// <summary>
    /// Global per-project tuning for the Ambition system. Lives as a single asset under
    /// Assets/Resources/Data/Ambitions/AmbitionSettings.asset; the asset itself is
    /// authored by hand in Phase 13.
    ///
    /// GatingNeedTypeNames — the set of CharacterNeed subclass names whose IsActive()
    /// blocks ambition pursuit. v1 default: "NeedHunger" + "NeedSleep". Adding to this
    /// list does NOT require code changes — BTCond_CanPursueAmbition (Task 39) reads it
    /// at evaluation time and matches against CharacterNeeds.AllNeeds via GetType().Name.
    ///
    /// String type-names rather than SO references because CharacterNeed is a plain C#
    /// class instantiated per-character, not a ScriptableObject. This matches the
    /// existing project convention used in NeedsSaveData (see CharacterNeeds.cs:310,
    /// .Serialize/.Deserialize).
    /// </summary>
    [CreateAssetMenu(menuName = "MWI/Ambition/AmbitionSettings", fileName = "AmbitionSettings")]
    public class AmbitionSettings : ScriptableObject
    {
        [SerializeField] private List<string> _gatingNeedTypeNames = new List<string> { "NeedHunger", "NeedSleep" };
        public IReadOnlyList<string> GatingNeedTypeNames => _gatingNeedTypeNames;

        private static AmbitionSettings _instance;
        public static AmbitionSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<AmbitionSettings>("Data/Ambitions/AmbitionSettings");
                if (_instance == null)
                {
                    Debug.LogError("[AmbitionSettings] No AmbitionSettings asset found at Resources/Data/Ambitions/AmbitionSettings. Create it via the asset menu.");
                }
                return _instance;
            }
        }

        public static void ResetForTests() => _instance = null;
    }
}
