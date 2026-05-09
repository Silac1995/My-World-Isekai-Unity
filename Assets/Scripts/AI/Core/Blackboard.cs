using System.Collections.Generic;
using UnityEngine;

namespace MWI.AI
{
    /// <summary>
    /// Shared memory for the NPC. Stores data that BT nodes read and write.
    /// Uses a typed dictionary with string keys for flexibility.
    /// Common keys are defined as constants to avoid typos.
    /// </summary>
    public class Blackboard
    {
        // --- Predefined keys ---
        public const string KEY_SELF = "Self";
        public const string KEY_CURRENT_ORDER = "CurrentOrder";
        public const string KEY_DETECTED_CHARACTER = "DetectedCharacter";
        public const string KEY_URGENT_NEED = "UrgentNeed";
        public const string KEY_SCHEDULE_ACTIVITY = "ScheduleActivity";
        public const string KEY_BATTLE_MANAGER = "BattleManager";
        public const string KEY_FLEE_BATTLE_MANAGER = "FleeBattleManager";
        public const string KEY_COMBAT_TARGET = "CombatTarget";
        public const string KEY_SOCIAL_TARGET = "SocialTarget";
        public const string KEY_PARTY_FOLLOW = "PartyFollow";

        private Dictionary<string, object> _data = new Dictionary<string, object>();

        /// <summary>
        /// Initializes the blackboard with the character reference.
        /// </summary>
        public Blackboard(Character self)
        {
            Set(KEY_SELF, self);
        }

        /// <summary>
        /// Writes a value to the blackboard.
        /// </summary>
        public void Set<T>(string key, T value)
        {
            _data[key] = value;
        }

        /// <summary>
        /// Reads a value from the blackboard. Returns default(T) if the key does not exist.
        /// </summary>
        public T Get<T>(string key)
        {
            if (_data.TryGetValue(key, out object value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// Checks whether a key exists and is not null.
        /// </summary>
        public bool Has(string key)
        {
            return _data.TryGetValue(key, out object value) && value != null;
        }

        /// <summary>
        /// Removes a key from the blackboard.
        /// </summary>
        public void Remove(string key)
        {
            _data.Remove(key);
        }

        /// <summary>
        /// Clears the entire blackboard except for Self.
        /// </summary>
        public void Clear()
        {
            Character self = Get<Character>(KEY_SELF);
            _data.Clear();
            if (self != null) Set(KEY_SELF, self);
        }

        // --- Shortcuts for common keys ---

        public Character Self => Get<Character>(KEY_SELF);
    }
}
