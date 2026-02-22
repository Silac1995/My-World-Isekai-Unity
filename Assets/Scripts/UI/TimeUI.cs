using UnityEngine;
using TMPro;
using MWI.Time;

namespace MWI.UI
{
    public class TimeUI : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private TextMeshProUGUI _timeText;
        [SerializeField] private TextMeshProUGUI _phaseText;

        [Header("Settings")]
        [SerializeField] private Character _targetCharacter;
        [SerializeField] private TimeManager _timeManager;
        [SerializeField] private string _timeFormat = "{0:00}:{1:00}";

        public TimeManager EffectiveTimeManager
        {
            get
            {
                if (_targetCharacter != null) return _targetCharacter.TimeManager;
                return _timeManager != null ? _timeManager : TimeManager.Instance;
            }
        }

        public void SetTargetCharacter(Character character) => _targetCharacter = character;
        public void SetTimeManager(TimeManager manager) => _timeManager = manager;

        private void Update()
        {
            if (EffectiveTimeManager == null) return;

            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            TimeManager manager = EffectiveTimeManager;

            if (_timeText != null)
            {
                int hours = manager.CurrentHour;
                int minutes = manager.CurrentMinute;
                _timeText.text = string.Format(_timeFormat, hours, minutes);
            }

            if (_phaseText != null)
            {
                _phaseText.text = manager.CurrentPhase.ToString();
            }
        }
    }
}
