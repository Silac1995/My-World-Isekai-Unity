[System.Serializable]
public class KeyInstance : MiscInstance
{
    /// <summary>
    /// Runtime LockId override. If set, takes priority over KeySO.LockId.
    /// Used for building keys where the LockId is a runtime GUID.
    /// </summary>
    private string _runtimeLockId;

    public KeyInstance(ItemSO data) : base(data) { }

    /// <summary>
    /// Typed accessor for the KeySO data. Returns null if ItemSO is not a KeySO.
    /// </summary>
    public KeySO KeyData => ItemSO as KeySO;

    /// <summary>
    /// The effective LockId: runtime override if set, otherwise falls back to KeySO.LockId.
    /// </summary>
    public string LockId => !string.IsNullOrEmpty(_runtimeLockId) ? _runtimeLockId : KeyData?.LockId;

    /// <summary>
    /// Sets a runtime LockId, overriding the SO's value.
    /// Call this when creating keys for specific building instances.
    /// </summary>
    public void SetLockId(string lockId) => _runtimeLockId = lockId;
}
