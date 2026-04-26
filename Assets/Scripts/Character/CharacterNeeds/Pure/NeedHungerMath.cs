using System;
using UnityEngine;

namespace MWI.Needs
{
    /// <summary>
    /// Pure value-math base for NeedHunger. Contains no game-system dependencies
    /// (no Character, no GOAP, no TimeManager) so it can be covered by EditMode unit tests.
    /// <para>
    /// NeedHunger (in Assembly-CSharp) extends this class and adds Unity/GOAP integration.
    /// </para>
    /// </summary>
    public class NeedHungerMath
    {
        public const float DEFAULT_MAX = 100f;
        public const float DEFAULT_LOW_THRESHOLD = 30f;

        protected float _currentValue;
        protected float _maxValue = DEFAULT_MAX;
        protected float _lowThreshold = DEFAULT_LOW_THRESHOLD;
        private bool _isStarving;

        /// <summary>Fires whenever CurrentValue changes (passes the new value). HUD subscribes.</summary>
        public event Action<float> OnValueChanged;

        /// <summary>
        /// Fires only on transitions of IsStarving (true when value first hits 0;
        /// false when it rises above 0).
        /// </summary>
        public event Action<bool> OnStarvingChanged;

        public float MaxValue => _maxValue;
        public bool IsStarving => _isStarving;

        public NeedHungerMath(float startValue)
        {
            _currentValue = Mathf.Clamp(startValue, 0f, _maxValue);
            _isStarving = _currentValue <= 0f;
        }

        public virtual float CurrentValue
        {
            get => _currentValue;
            set
            {
                float clamped = Mathf.Clamp(value, 0f, _maxValue);
                if (Mathf.Approximately(clamped, _currentValue)) return;
                _currentValue = clamped;
                OnValueChanged?.Invoke(_currentValue);
                UpdateStarvingFlag();
            }
        }

        public void IncreaseValue(float amount) => CurrentValue = _currentValue + amount;
        public void DecreaseValue(float amount) => CurrentValue = _currentValue - amount;

        public bool IsLow() => _currentValue <= _lowThreshold;

        private void UpdateStarvingFlag()
        {
            bool nowStarving = _currentValue <= 0f;
            if (nowStarving == _isStarving) return;
            _isStarving = nowStarving;
            OnStarvingChanged?.Invoke(_isStarving);
        }
    }
}
