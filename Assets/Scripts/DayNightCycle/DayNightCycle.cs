using UnityEngine;
using MWI.Time;

public class DayNightCycle : MonoBehaviour
{
    [Header("Visual Settings")]
    [SerializeField] private Gradient _lightColor;
    [SerializeField] private AnimationCurve _intensityCurve;
    
    [Header("Control (Via TimeManager)")]
    [SerializeField] private TimeManager _timeManager;
    private Light _directionalLight;

    public TimeManager EffectiveTimeManager => _timeManager != null ? _timeManager : TimeManager.Instance;

    // rotation points
    private Quaternion _rotMidi = Quaternion.Euler(160, 0, 0);
    private Quaternion _rotHorizonGauche = Quaternion.Euler(180, -20, 270);
    private Quaternion _rotCouche = Quaternion.Euler(200, 0, 175);
    private Quaternion _rotHorizonDroite = Quaternion.Euler(180, 20, 95);

    private void Awake()
    {
        _directionalLight = GetComponent<Light>();
    }

    private void Update()
    {
        TimeManager manager = EffectiveTimeManager;
        if (manager == null) return;

        float time = manager.CurrentTime01;

        // rotation
        transform.rotation = GetRotationAtTime(time);

        UpdateVisuals(time);
    }

    private Quaternion GetRotationAtTime(float t)
    {
        // PHASE INVERSE : Droite -> Gauche

        // 1. Midi vers Horizon DROITE (0.0 to 0.25)
        if (t < 0.25f) return Quaternion.Slerp(_rotMidi, _rotHorizonDroite, t / 0.25f);

        // 2. Horizon DROITE vers Couche (0.25 to 0.5)
        if (t < 0.5f) return Quaternion.Slerp(_rotHorizonDroite, _rotCouche, (t - 0.25f) / 0.25f);

        // 3. Couche vers Horizon GAUCHE (0.5 to 0.75)
        if (t < 0.75f) return Quaternion.Slerp(_rotCouche, _rotHorizonGauche, (t - 0.5f) / 0.25f);

        // 4. Horizon GAUCHE vers Midi (0.75 to 1.0)
        return Quaternion.Slerp(_rotHorizonGauche, _rotMidi, (t - 0.75f) / 0.25f);
    }

    private void UpdateVisuals(float t)
    {
        if (_lightColor != null)
        {
            _directionalLight.color = _lightColor.Evaluate(t);
        }

        if (_intensityCurve != null)
        {
            _directionalLight.intensity = _intensityCurve.Evaluate(t);
        }
        else
        {
            // Fallback dot product
            float dot = Vector3.Dot(transform.forward, Vector3.down);
            if (dot > 0)
                _directionalLight.intensity = Mathf.SmoothStep(0, 1.2f, dot * 2.5f);
            else
                _directionalLight.intensity = 0;
        }
    }
}
