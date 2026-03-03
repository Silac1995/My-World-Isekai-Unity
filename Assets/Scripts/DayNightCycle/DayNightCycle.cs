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
    private Quaternion _rotNight = Quaternion.Euler(-9, 1.5f, 0);      // Nuit (00:00)
    private Quaternion _rotMorning = Quaternion.Euler(-8, -10, 0);    // Matin (06:00) - Reste sombre
    private Quaternion _rotFullDay = Quaternion.Euler(22, -9.5f, 0);  // Pleine Journée (12:00)
    private Quaternion _rotAfternoon = Quaternion.Euler(185, 8, 0);    // Afternoon (18:00)
    private Quaternion _rotEvening = Quaternion.Euler(-4.59f, -15, 0); // Soirée (21:00)


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
        // t = 0 -> Minuit (00h)
        // t = 0.25 -> Matin (06h)
        // t = 0.5 -> Midi (12h)
        // t = 0.75 -> Afternoon (18h)
        // t = 0.875 -> Evening (21h)
        // t = 1.0 -> Minuit (00h)

        if (t < 0.25f)
        {
            // 00h -> 06h : Night to Morning (Reste sombre)
            return Quaternion.Slerp(_rotNight, _rotMorning, t / 0.25f);
        }
        else if (t < 0.5f)
        {
            // 06h -> 12h : Morning to Full Day (Lever rapide)
            return Quaternion.Slerp(_rotMorning, _rotFullDay, (t - 0.25f) / 0.25f);
        }
        else if (t < 0.75f)
        {
            // 12h -> 18h : Full Day to Afternoon
            return Quaternion.Slerp(_rotFullDay, _rotAfternoon, (t - 0.5f) / 0.25f);
        }
        else if (t < 0.875f)
        {
            // 18h -> 21h : Afternoon to Evening
            return Quaternion.Slerp(_rotAfternoon, _rotEvening, (t - 0.75f) / 0.125f);
        }
        else
        {
            // 21h -> 00h : Evening to Night
            return Quaternion.Slerp(_rotEvening, _rotNight, (t - 0.875f) / 0.125f);
        }
    }

    private void UpdateVisuals(float t)
    {
        // Force intensity to 0 if light is below horizon (Rotation X < 0)
        float currentX = transform.eulerAngles.x;
        // Euler angles are 0-360. -10 is 350.
        if (currentX > 180 && currentX < 359) // Under the horizon (negative values)
        {
            _directionalLight.intensity = 0;
            return;
        }

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
