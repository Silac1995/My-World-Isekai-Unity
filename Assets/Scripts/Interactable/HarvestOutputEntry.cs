using UnityEngine;

/// <summary>
/// One (Item, Count) pair on a Harvestable's yield or destruction output list.
/// Replaces the legacy <c>(List&lt;ItemSO&gt;, int singleCount)</c> pair so each item can
/// declare its own drop quantity (e.g. an apple tree dropping 3 apples + 1 seed on harvest,
/// or destruction giving 5 wood + 2 sticks).
/// </summary>
[System.Serializable]
public struct HarvestOutputEntry
{
    public ItemSO Item;
    [Min(1)] public int Count;

    public HarvestOutputEntry(ItemSO item, int count)
    {
        Item = item;
        Count = Mathf.Max(1, count);
    }
}
