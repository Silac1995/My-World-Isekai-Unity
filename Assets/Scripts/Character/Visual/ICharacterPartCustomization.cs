using UnityEngine;

/// <summary>
/// Optional interface for visuals that support part/skin customization.
/// Covers equipment layers, dismemberment, per-part coloring, and skin combining.
/// All color changes must use Material Property Blocks to preserve batching.
/// </summary>
public interface ICharacterPartCustomization
{
    void SetPart(string slotName, string attachmentName);
    void RemovePart(string slotName);
    void SetPartColor(string slotName, Color color);
    void SetPartPalette(string slotName, Texture2D paletteLUT);

    void ApplySkinSet(string skinName);
    void CombineSkins(params string[] skinNames);
}
