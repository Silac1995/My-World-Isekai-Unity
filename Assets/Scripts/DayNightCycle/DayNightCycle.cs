using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("Contrôle du Temps")]
    [SerializeField] private float _cycleSpeed = 0.1f;

    [Header("Paramètres d'Ambiance")]
    [SerializeField] private float _maxIntensity = 1.2f;

    private Light _directionalLight;
    private float _timer = 0f;

    // Tes points de rotation (conservés tels quels)
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
        // Le timer avance
        _timer += Time.deltaTime * _cycleSpeed;
        if (_timer > 1f) _timer = 0f;

        // On appelle la fonction avec l'ordre inversé
        transform.rotation = GetRotationAtTime(_timer);

        UpdateIntensity();
    }

    private Quaternion GetRotationAtTime(float t)
    {
        // PHASE INVERSÉE : On passe par la Droite avant la Gauche

        // 1. Midi vers Horizon DROITE (0.0 à 0.25)
        if (t < 0.25f) return Quaternion.Slerp(_rotMidi, _rotHorizonDroite, t / 0.25f);

        // 2. Horizon DROITE vers Couché (0.25 à 0.5)
        if (t < 0.5f) return Quaternion.Slerp(_rotHorizonDroite, _rotCouche, (t - 0.25f) / 0.25f);

        // 3. Couché vers Horizon GAUCHE (0.5 à 0.75)
        if (t < 0.75f) return Quaternion.Slerp(_rotCouche, _rotHorizonGauche, (t - 0.5f) / 0.25f);

        // 4. Horizon GAUCHE vers Midi (0.75 à 1.0)
        return Quaternion.Slerp(_rotHorizonGauche, _rotMidi, (t - 0.75f) / 0.25f);
    }

    private void UpdateIntensity()
    {
        // On calcule la direction vers le bas (0 = horizon, 1 = zénith vertical)
        float dot = Vector3.Dot(transform.forward, Vector3.down);

        // Puisque ton Midi est à X=160 (légèrement incliné), 
        // on ajuste pour que l'intensité soit maximale au point Midi
        if (dot > 0)
        {
            _directionalLight.intensity = Mathf.SmoothStep(0, _maxIntensity, dot * 2.5f);
        }
        else
        {
            _directionalLight.intensity = 0;
        }
    }
}