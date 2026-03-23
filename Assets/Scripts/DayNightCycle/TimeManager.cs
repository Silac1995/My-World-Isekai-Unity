using UnityEngine;
using System;
using Unity.Netcode;

namespace MWI.Time
{
    public class TimeManager : MonoBehaviour, ISaveable
    {
        public static TimeManager Instance { get; private set; }

        [Header("Time Settings")]
        [SerializeField, Tooltip("Seconds in real time for one in-game hour")]
        private float _secondsPerHour = 50f; // 50s * 24h = 1200s (20 min)
        
        [SerializeField, Range(0, 23)]
        private int _startHour = 6;

        [Header("Phase Settings (Hours)")]
        [SerializeField] private int _morningStart = 6;
        [SerializeField] private int _afternoonStart = 12;
        [SerializeField] private int _eveningStart = 18;
        [SerializeField] private int _nightStart = 21;

        private float _currentTime; // 0 to 1
        private DayPhase _currentPhase;
        private int _lastHour;

        public float CurrentTime01 => _currentTime;
        public int CurrentHour => Mathf.FloorToInt(_currentTime * 24f);
        public int CurrentMinute => Mathf.FloorToInt(((_currentTime * 24f) % 1f) * 60f);
        public DayPhase CurrentPhase => _currentPhase;
        
        /// <summary>
        /// Le jour actuel du jeu (commence à 1).
        /// </summary>
        public int CurrentDay { get; private set; } = 1;

        public event Action<DayPhase> OnPhaseChanged;
        public event Action OnNewDay;
        public event Action<int> OnHourChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            _currentTime = (float)_startHour / 24f;
            _lastHour = _startHour;
            UpdatePhase(true);

            // Defer ISaveable registration until SaveManager is ready
            Invoke(nameof(RegisterWithSaveManager), 0.5f);
        }

        private void RegisterWithSaveManager()
        {
            if (SaveManager.Instance != null)
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

        private void Update()
        {
            ProgressTime();
        }

        private void ProgressTime()
        {
            float fullDayDuration = _secondsPerHour * 24f;
            float timeToAdvance = UnityEngine.Time.deltaTime / fullDayDuration;
            _currentTime += timeToAdvance;

            if (_currentTime >= 1f)
            {
                _currentTime -= 1f;
                CurrentDay++;
                OnNewDay?.Invoke();
            }

            // Détection du changement d'heure
            int currentHour = CurrentHour;
            if (currentHour != _lastHour)
            {
                _lastHour = currentHour;
                OnHourChanged?.Invoke(currentHour);

                // Periodic atomic save (only if server/host)
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && SaveManager.Instance != null)
                {
                    _ = SaveManager.Instance.SaveWorldAsync();
                }
            }

            UpdatePhase(false);
        }

        private void UpdatePhase(bool force)
        {
            int hour = CurrentHour;
            DayPhase newPhase;

            if (hour >= _nightStart || hour < _morningStart)
            {
                newPhase = DayPhase.Night;
            }
            else if (hour >= _eveningStart)
            {
                newPhase = DayPhase.Evening;
            }
            else if (hour >= _afternoonStart)
            {
                newPhase = DayPhase.Afternoon;
            }
            else
            {
                newPhase = DayPhase.Morning;
            }

            if (force || newPhase != _currentPhase)
            {
                _currentPhase = newPhase;
                OnPhaseChanged?.Invoke(_currentPhase);
                Debug.Log($"[TimeManager] Phase changed to: {_currentPhase} at {hour:00}:{CurrentMinute:00}");
            }
        }

        // Helper to skip time (for debugging or sleeping)
        public void SkipToHour(int hour)
        {
            int clampedHour = Mathf.Clamp(hour, 0, 23);

            // Si on saute à une heure inférieure à l'heure actuelle, on a passé minuit (passage au lendemain).
            if (clampedHour < CurrentHour)
            {
                CurrentDay++;
                OnNewDay?.Invoke();
            }

            _currentTime = (float)clampedHour / 24f;

            if (CurrentHour != _lastHour)
            {
                _lastHour = CurrentHour;
                OnHourChanged?.Invoke(CurrentHour);
            }

            UpdatePhase(true);
        }

        #region ISaveable Implementation

        public string SaveKey => "TimeManager_Data";

        [Serializable]
        public class TimeSaveData
        {
            public int Day;
            public float Time01;
        }

        public object CaptureState()
        {
            return new TimeSaveData
            {
                Day = CurrentDay,
                Time01 = _currentTime
            };
        }

        public void RestoreState(object state)
        {
            if (state is TimeSaveData data)
            {
                CurrentDay = data.Day;
                _currentTime = data.Time01;
                _lastHour = CurrentHour;
                UpdatePhase(true);
                Debug.Log($"<color=green>[TimeManager]</color> Time restored from save: Day {CurrentDay}, {CurrentHour:00}:{CurrentMinute:00}");
            }
        }

        #endregion
    }
}
