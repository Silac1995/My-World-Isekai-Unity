using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using MWI.Time;

namespace MWI.WorldSystem
{
    [Serializable]
    public class WorldOffsetSaveData
    {
        public int HighWaterMark;
        public List<int> AllocatedSlots = new List<int>();
        public List<ReleasedSlotData> FreeList = new List<ReleasedSlotData>();
    }

    [Serializable]
    public struct ReleasedSlotData
    {
        public int SlotIndex;
        public int DayFreed;
    }

    /// <summary>
    /// Server-side registry responsible for robust spatial slot distribution.
    /// Uses lazy recycling to prevent race conditions with dirty client caches.
    /// </summary>
    public class WorldOffsetAllocator : MonoBehaviour, ISaveable
    {
        public static WorldOffsetAllocator Instance { get; private set; }

        [SerializeField] private WorldSettingsData _settings;

        private int _highWaterMark = 1; // Start at 1 (0 is usually the spawn/hub)
        private HashSet<int> _allocatedSlots = new HashSet<int>();
        private Queue<ReleasedSlotData> _freeList = new Queue<ReleasedSlotData>();

        public string SaveKey => "WorldOffsetAllocator_Data";

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // Move under GameSessionManager or DontDestroyOnLoad
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (_settings == null)
            {
                _settings = Resources.Load<WorldSettingsData>("Data/World/WorldSettingsData");
                if (_settings == null)
                {
                    Debug.LogWarning("[WorldOffsetAllocator] WorldSettingsData not found in Resources/Data/World/WorldSettingsData. Using hardcoded defaults.");
                }
            }

            // Defer ISaveable registration until SaveManager is ready
            Invoke(nameof(RegisterWithSaveManager), 0.5f);
        }

        private void RegisterWithSaveManager()
        {
            if (SaveManager.Instance != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                SaveManager.Instance.RegisterWorldSaveable(this);
            }
        }

        private void OnDestroy()
        {
            if (SaveManager.Instance != null)
            {
                SaveManager.Instance.UnregisterWorldSaveable(this);
            }
        }

        /// <summary>
        /// Allocates a new guaranteed unique spatial slot.
        /// Reuses stale slots first, otherwise issues a new increment.
        /// </summary>
        /// <returns>The slot index multiplier.</returns>
        public int AllocateSlotIndex()
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                Debug.LogError("[WorldOffsetAllocator] Only the Server can allocate map slots.");
                return -1;
            }

            int cooldownDays = _settings != null ? _settings.SlotRecycleCooldownDays : 30;
            int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;

            // Try to pop a free slot if it's cooled down
            if (_freeList.Count > 0)
            {
                var oldestFreeSlot = _freeList.Peek();
                if (currentDay - oldestFreeSlot.DayFreed >= cooldownDays)
                {
                    _freeList.Dequeue();
                    _allocatedSlots.Add(oldestFreeSlot.SlotIndex);
                    Debug.Log($"[WorldOffsetAllocator] Re-allocated cooled down slot index: {oldestFreeSlot.SlotIndex}");
                    return oldestFreeSlot.SlotIndex;
                }
            }

            // Otherwise, grab the next available fresh slot
            int allocated = _highWaterMark;
            _highWaterMark++;
            _allocatedSlots.Add(allocated);
            Debug.Log($"[WorldOffsetAllocator] Allocated new slot index: {allocated}. High water mark is now: {_highWaterMark}");
            return allocated;
        }

        /// <summary>
        /// Converts a Slot Index into an absolute XYZ World offset.
        /// </summary>
        public Vector3 GetOffsetVector(int slotIndex)
        {
            float distance = _settings != null ? _settings.SlotOffsetDistance : 10000f;
            return new Vector3(slotIndex * distance, 0, slotIndex * distance);
        }

        /// <summary>
        /// Releases a slot back into the recycling pool.
        /// CAUTION: Do not release Abandoned Cities - only roaming camps or settlements that dissolve completely.
        /// </summary>
        public void ReleaseSlot(int slotIndex)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            if (_allocatedSlots.Contains(slotIndex))
            {
                _allocatedSlots.Remove(slotIndex);
                int currentDay = TimeManager.Instance != null ? TimeManager.Instance.CurrentDay : 0;
                
                _freeList.Enqueue(new ReleasedSlotData 
                { 
                    SlotIndex = slotIndex, 
                    DayFreed = currentDay 
                });
                Debug.Log($"[WorldOffsetAllocator] Released slot index: {slotIndex} back to the free list (Day {currentDay}).");
            }
        }

        #region ISaveable Implementation

        public object CaptureState()
        {
            return new WorldOffsetSaveData
            {
                HighWaterMark = _highWaterMark,
                AllocatedSlots = new List<int>(_allocatedSlots),
                FreeList = new List<ReleasedSlotData>(_freeList)
            };
        }

        public void RestoreState(object state)
        {
            if (state is WorldOffsetSaveData data)
            {
                _highWaterMark = data.HighWaterMark;
                
                _allocatedSlots.Clear();
                foreach (int slot in data.AllocatedSlots)
                {
                    _allocatedSlots.Add(slot);
                }

                _freeList.Clear();
                foreach (var released in data.FreeList)
                {
                    _freeList.Enqueue(released);
                }
                
                Debug.Log($"<color=green>[WorldOffsetAllocator]</color> Restored {_allocatedSlots.Count} allocated slots. High water mark: {_highWaterMark}");
            }
        }

        #endregion
    }
}
