using UnityEngine;
using System;

namespace MWI.Time
{
    public class TimeManager : MonoBehaviour
    {
        public static TimeManager Instance { get; private set; }

        [Header("Time Settings")]
        [SerializeField, Tooltip("Seconds in real time for a full 24h in-game")]
        private float _fullDayDurationInSeconds = 1200f; // 20 minutes by default
        
        [SerializeField, Range(0, 23)]
        private int _startHour = 6;

        [Header("Phase Settings (Hours)")]
        [SerializeField] private int _morningStart = 6;
        [SerializeField] private int _afternoonStart = 12;
        [SerializeField] private int _eveningStart = 18;
        [SerializeField] private int _nightStart = 21;

        private float _currentTime; // 0 to 1
        private DayPhase _currentPhase;

        public float CurrentTime01 => _currentTime;
        public int CurrentHour => Mathf.FloorToInt(_currentTime * 24f);
        public int CurrentMinute => Mathf.FloorToInt(((_currentTime * 24f) % 1f) * 60f);
        public DayPhase CurrentPhase => _currentPhase;

        public event Action<DayPhase> OnPhaseChanged;
        public event Action OnNewDay;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            _currentTime = (float)_startHour / 24f;
            UpdatePhase(true);
        }

        private void Update()
        {
            ProgressTime();
        }

        private void ProgressTime()
        {
            float timeToAdvance = UnityEngine.Time.deltaTime / _fullDayDurationInSeconds;
            _currentTime += timeToAdvance;

            if (_currentTime >= 1f)
            {
                _currentTime -= 1f;
                OnNewDay?.Invoke();
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
            _currentTime = (float)Mathf.Clamp(hour, 0, 23) / 24f;
            UpdatePhase(true);
        }
    }
}
