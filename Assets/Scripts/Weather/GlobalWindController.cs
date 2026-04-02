using System;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Weather
{
    public class GlobalWindController : NetworkBehaviour
    {
        public static GlobalWindController Instance { get; private set; }

        public NetworkVariable<Vector2> WindDirection = new(Vector2.right);
        public NetworkVariable<float> WindStrength = new(0.3f);

        [Header("Drift Settings")]
        [SerializeField] private float _driftSpeed = 0.01f;
        [SerializeField] private float _gustFrequency = 0.1f;
        [SerializeField] private float _maxGustStrength = 0.2f;

        public event Action<Vector2, float> OnWindChanged;

        private float _driftAngle;
        private float _gustTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            WindDirection.OnValueChanged += (_, _) => OnWindChanged?.Invoke(WindDirection.Value, WindStrength.Value);
            WindStrength.OnValueChanged += (_, _) => OnWindChanged?.Invoke(WindDirection.Value, WindStrength.Value);

            if (IsServer)
            {
                _driftAngle = UnityEngine.Random.Range(0f, 360f);
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            // Gradual wind direction drift using simulation time.
            // Clamped to 0.5f to prevent wind snapping at Giga Speed (CLAUDE.md Rule 26).
            float dt = Mathf.Min(Time.deltaTime, 0.5f);
            _driftAngle += _driftSpeed * dt * 10f;
            float rad = _driftAngle * Mathf.Deg2Rad;
            WindDirection.Value = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)).normalized;

            // Random gusts
            _gustTimer -= dt;
            if (_gustTimer <= 0f)
            {
                _gustTimer = 1f / Mathf.Max(0.01f, _gustFrequency);
                float gust = UnityEngine.Random.Range(0f, _maxGustStrength);
                WindStrength.Value = Mathf.Clamp01(0.3f + gust);
            }
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }
    }
}
