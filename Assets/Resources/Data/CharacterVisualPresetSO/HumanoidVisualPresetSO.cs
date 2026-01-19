using UnityEngine;

[CreateAssetMenu(fileName = "HumanoidVisualPreset", menuName = "Scriptable Objects/Visual/Humanoid Preset")]
public class HumanoidVisualPresetSO : CharacterVisualPresetSO
{
    [Header("Sprite Library Categories")]
    [SerializeField] private string _earCategory = "01";

    [Header("Morphology Settings")]
    [SerializeField] private bool _hasTail;
    [SerializeField] private Color _defaultSkinColor = Color.white;

    public string EarCategory => _earCategory;
    public Color DefaultSkinColor => _defaultSkinColor;
}