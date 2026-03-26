using UnityEngine;

[System.Serializable]
public class KeyInstance : MiscInstance
{
    public KeyInstance(ItemSO data) : base(data) { }

    /// <summary>
    /// Typed accessor for the KeySO data. Returns null if ItemSO is not a KeySO.
    /// </summary>
    public KeySO KeyData => ItemSO as KeySO;
}
