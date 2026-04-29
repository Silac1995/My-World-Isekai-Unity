using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Pure-side mirror of <c>HarvestOutputEntry</c>. The MWI.Farming.Pure asmdef cannot
    /// reference Assembly-CSharp where ItemSO lives, so we type the item slot as
    /// ScriptableObject and cast at use sites — same pattern as <c>CropSO._destructionOutputs</c>
    /// and <c>CropSO._produceItem</c> before this rework.
    /// </summary>
    [System.Serializable]
    public struct CropHarvestOutput
    {
        public ScriptableObject Item;
        [Min(1)] public int Count;
    }
}
