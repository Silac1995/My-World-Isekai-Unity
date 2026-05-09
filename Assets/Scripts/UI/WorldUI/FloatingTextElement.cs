using UnityEngine;
using TMPro;

/// <summary>
/// Handles the float-up + fade-out animation for a single floating text instance.
/// Spawned by FloatingTextSpawner, self-destroys after lifetime expires.
/// </summary>
public class FloatingTextElement : MonoBehaviour
{
    [SerializeField] private TextMeshPro _text;

    [Header("Animation")]
    [SerializeField] private float _lifetime = 1.2f;
    [SerializeField] private float _floatSpeed = 1.5f;
    [SerializeField] private float _decelerationFactor = 2f;
    [SerializeField] private float _fadeStartNormalized = 0.4f;

    [Header("Scale Punch")]
    [SerializeField] private float _punchScale = 1.4f;
    [SerializeField] private float _punchDuration = 0.15f;

    [Header("Outline")]
    [SerializeField] private float _outlineWidth = 0.2f;

    private float _elapsed;
    private Color _baseColor;
    private Vector3 _baseScale;
    private Transform _cameraTransform;

    public void Initialize(string message, Color color)
    {
        Initialize(message, color, Color.clear, 1f);
    }

    public void Initialize(string message, Color color, float scaleFactor)
    {
        Initialize(message, color, Color.clear, scaleFactor);
    }

    public void Initialize(string message, Color color, Color outlineColor, float scaleFactor = 1f)
    {
        if (_text == null) _text = GetComponent<TextMeshPro>();

        _text.text = message;
        _baseColor = color;
        _text.color = color;
        _baseScale = transform.localScale * scaleFactor;
        transform.localScale = _baseScale * _punchScale;

        if (outlineColor.a > 0f)
        {
            _text.fontMaterial.SetColor("_OutlineColor", outlineColor);
            _text.fontMaterial.SetFloat("_OutlineWidth", _outlineWidth);
        }
        else
        {
            _text.fontMaterial.SetFloat("_OutlineWidth", 0f);
        }

        Camera cam = Camera.main;
        if (cam != null) _cameraTransform = cam.transform;
    }

    private void Update()
    {
        // Visual feedback uses unscaled time (Rule 24 — not affected by GameSpeedController)
        float dt = Time.unscaledDeltaTime;
        _elapsed += dt;

        float t = _elapsed / _lifetime;

        // --- Float upward with deceleration ---
        float speedMultiplier = Mathf.Max(0f, 1f - (_decelerationFactor * t));
        transform.position += Vector3.up * (_floatSpeed * speedMultiplier * dt);

        // --- Scale punch (pop in, then settle) ---
        if (_elapsed < _punchDuration)
        {
            float punchT = _elapsed / _punchDuration;
            float scale = Mathf.Lerp(_punchScale, 1f, punchT);
            transform.localScale = _baseScale * scale;
        }

        // --- Fade out ---
        if (t > _fadeStartNormalized)
        {
            float fadeT = (t - _fadeStartNormalized) / (1f - _fadeStartNormalized);
            float alpha = Mathf.Lerp(1f, 0f, fadeT);
            _text.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
        }

        // --- Billboard toward camera ---
        if (_cameraTransform != null)
        {
            transform.forward = _cameraTransform.forward;
        }

        if (_elapsed >= _lifetime)
        {
            Destroy(gameObject);
        }
    }
}
