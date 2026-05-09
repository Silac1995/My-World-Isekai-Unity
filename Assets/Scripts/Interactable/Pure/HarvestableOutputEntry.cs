using UnityEngine;

namespace MWI.Interactables
{
    /// <summary>
    /// Pure-asmdef-compatible (Item, Count) entry on a <see cref="HarvestableSO"/>'s yield
    /// or destruction output list. Item slot is typed as <see cref="ScriptableObject"/> rather
    /// than ItemSO because Pure asmdefs cannot reference Assembly-CSharp; consumers cast
    /// back to ItemSO at use sites (see <c>CropHarvestable.CastEntryList</c>).
    ///
    /// Replaced the previous <c>MWI.Farming.CropHarvestOutput</c> in the 2026-04-29
    /// unification (HarvestableSO became the base class for CropSO + future ore/mine SOs).
    /// Existing serialised <c>CropSO</c> assets keep working without migration: Unity
    /// serialises struct lists by field shape rather than type name, so the on-disk
    /// <c>_harvestOutputs:</c> / <c>_destructionEntries:</c> YAML deserialises cleanly
    /// into the new struct as long as the <c>Item</c> + <c>Count</c> field names match.
    /// </summary>
    [System.Serializable]
    public struct HarvestableOutputEntry
    {
        public ScriptableObject Item;
        [Min(1)] public int Count;
    }
}
