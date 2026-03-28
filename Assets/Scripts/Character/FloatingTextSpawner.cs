using UnityEngine;

/// <summary>
/// Generic floating text system for a Character.
/// Provides a public API for any subsystem to spawn floating text (damage, status effects, healing, etc.)
/// and auto-subscribes to CharacterCombat.OnDamageTaken for damage numbers.
/// </summary>
public class FloatingTextSpawner : CharacterSystem
{
    [Header("Prefab")]
    [SerializeField] private GameObject _floatingTextPrefab;

    [Header("Spawn Settings")]
    [SerializeField] private float _horizontalSpread = 0.4f;
    [SerializeField] private float _verticalOffset = 0.2f;

    [Header("Damage Colors")]
    [SerializeField] private Color _physicalColor = Color.white;
    [SerializeField] private Color _fireColor = new Color(1f, 0.5f, 0.1f);       // Orange
    [SerializeField] private Color _iceColor = new Color(0.4f, 0.85f, 1f);       // Cyan
    [SerializeField] private Color _lightningColor = new Color(1f, 1f, 0.3f);    // Yellow
    [SerializeField] private Color _holyColor = new Color(1f, 0.95f, 0.6f);      // Gold
    [SerializeField] private Color _darkColor = new Color(0.7f, 0.3f, 0.9f);     // Purple

    private void Start()
    {
        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnDamageTaken += HandleDamageTaken;
        }
    }

    private void OnDestroy()
    {
        if (_character != null && _character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnDamageTaken -= HandleDamageTaken;
        }
    }

    private void HandleDamageTaken(float amount, DamageType type)
    {
        Color color = GetColorForDamageType(type);
        string text = Mathf.RoundToInt(amount).ToString();
        SpawnText(text, color);
    }

    /// <summary>
    /// Spawns a floating text above the character with the given message and color.
    /// Use this from any system: status effects, healing, XP gains, etc.
    /// </summary>
    public void SpawnText(string message, Color color)
    {
        SpawnText(message, color, 1f);
    }

    /// <summary>
    /// Spawns a floating text with a custom scale factor.
    /// scaleFactor > 1 = bigger text (e.g., critical hits), less than 1 = smaller (e.g., subtle info).
    /// </summary>
    public void SpawnText(string message, Color color, float scaleFactor)
    {
        if (_floatingTextPrefab == null)
        {
            Debug.LogWarning($"[FloatingTextSpawner] No prefab assigned on {_character?.gameObject.name}");
            return;
        }

        Vector3 spawnPos = GetSpawnPosition();

        GameObject go = Instantiate(_floatingTextPrefab, spawnPos, Quaternion.identity);
        FloatingTextElement element = go.GetComponent<FloatingTextElement>();

        if (element == null)
        {
            Debug.LogError("[FloatingTextSpawner] Prefab is missing FloatingTextElement component.");
            Destroy(go);
            return;
        }

        element.Initialize(message, color, scaleFactor);
    }

    private Vector3 GetSpawnPosition()
    {
        Vector3 basePos;

        if (_character.CharacterVisual != null)
        {
            basePos = _character.CharacterVisual.GetVisualExtremity(Vector3.up);
        }
        else
        {
            basePos = _character.transform.position + Vector3.up * 2f;
        }

        float randomX = Random.Range(-_horizontalSpread, _horizontalSpread);
        return basePos + new Vector3(randomX, _verticalOffset, 0f);
    }

    private Color GetColorForDamageType(DamageType type)
    {
        switch (type)
        {
            case DamageType.Fire:      return _fireColor;
            case DamageType.Ice:       return _iceColor;
            case DamageType.Lightning: return _lightningColor;
            case DamageType.Holy:      return _holyColor;
            case DamageType.Dark:      return _darkColor;
            default:                   return _physicalColor;
        }
    }
}
