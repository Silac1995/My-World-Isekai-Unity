using System;

/// <summary>
/// Save-data twin of DeliveredMaterialEntry. Keys by ItemSO AssetGuid (not requirement
/// index) so the snapshot survives a designer-time edit to _constructionRequirements
/// ordering between save and load.
///
/// Authored 2026-05-06 — see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md.
/// </summary>
[Serializable]
public class DeliveredMaterialEntryDTO
{
    public string ItemAssetGuid;
    public int Delivered;
}
