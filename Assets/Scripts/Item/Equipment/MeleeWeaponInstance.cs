using UnityEngine;

[System.Serializable]
public class MeleeWeaponInstance : WeaponInstance
{
    [SerializeField] private float _sharpness = 1f;
    [SerializeField] private float _maxSharpness = 1f;

    public MeleeWeaponInstance(ItemSO data) : base(data)
    {
        if (data is WeaponSO weaponData)
        {
            _maxSharpness = weaponData.MaxSharpness;
            _sharpness = _maxSharpness;
        }
    }

    public float Sharpness => _sharpness;
    public float MaxSharpness => _maxSharpness;
    public bool NeedSharpening => _sharpness < _maxSharpness * 0.3f;

    /// <summary>
    /// Degrades the sharpness after each melee attack.
    /// </summary>
    public void DegradeSharpness(float amount = 0.01f)
    {
        _sharpness = Mathf.Max(0f, _sharpness - amount);
    }

    /// <summary>
    /// Restores the sharpness (e.g. blacksmith, sharpening stone).
    /// </summary>
    public void Sharpen(float amount)
    {
        _sharpness = Mathf.Min(_maxSharpness, _sharpness + amount);
    }
}
