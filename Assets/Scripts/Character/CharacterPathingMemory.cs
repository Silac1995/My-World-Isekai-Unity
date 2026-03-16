using System.Collections.Generic;
using UnityEngine;
using MWI.Time;

namespace MWI.AI
{
    /// <summary>
    /// Lightweight data class to store targets that a character has failed to reach.
    /// Resets automatically on TimeManager events to prevent permanent soft-locks.
    /// </summary>
    public class CharacterPathingMemory
    {
        private readonly Character _character;
        private readonly Dictionary<int, int> _failedAttempts;
        private const int MAX_ATTEMPTS = 3;

        public CharacterPathingMemory(Character character)
        {
            _character = character;
            _failedAttempts = new Dictionary<int, int>();

            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnNewDay += ClearMemory;
                TimeManager.Instance.OnHourChanged += ClearMemoryOnHour;
            }
        }

        public void CleanUp()
        {
            if (TimeManager.Instance != null)
            {
                TimeManager.Instance.OnNewDay -= ClearMemory;
                TimeManager.Instance.OnHourChanged -= ClearMemoryOnHour;
            }
            _failedAttempts.Clear();
        }

        /// <summary>
        /// Records a failure to reach the specified collider's InstanceID.
        /// Returns true if the target is newly blacklisted after this failure.
        /// </summary>
        public bool RecordFailure(int targetInstanceId)
        {
            if (!_failedAttempts.ContainsKey(targetInstanceId))
            {
                _failedAttempts[targetInstanceId] = 0;
            }

            _failedAttempts[targetInstanceId]++;
            
            bool justBlacklisted = _failedAttempts[targetInstanceId] == MAX_ATTEMPTS;
            if (justBlacklisted)
            {
                Debug.LogWarning($"<color=orange>[PathingMemory]</color> {_character.CharacterName} has blacklisted target {targetInstanceId} after {MAX_ATTEMPTS} failed attempts.");
            }

            return justBlacklisted;
        }

        public bool IsBlacklisted(int targetInstanceId)
        {
            return _failedAttempts.TryGetValue(targetInstanceId, out int attempts) && attempts >= MAX_ATTEMPTS;
        }

        public int GetFailCount(int targetInstanceId)
        {
            return _failedAttempts.TryGetValue(targetInstanceId, out int attempts) ? attempts : 0;
        }

        public void ClearMemory()
        {
            if (_failedAttempts.Count > 0)
            {
                Debug.Log($"<color=cyan>[PathingMemory]</color> Resetting pathing memory for {_character.CharacterName} (New Day)");
                _failedAttempts.Clear();
            }
        }

        private void ClearMemoryOnHour(int hour)
        {
            if (_failedAttempts.Count > 0)
            {
                // We could selectively expire old things here, but flushing every hour is a safe baseline.
                _failedAttempts.Clear();
            }
        }
    }
}
