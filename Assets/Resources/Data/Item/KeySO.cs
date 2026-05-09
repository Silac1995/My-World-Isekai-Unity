using UnityEngine;

[CreateAssetMenu(fileName = "KeyItem", menuName = "Scriptable Objects/Items/Key")]
public class KeySO : MiscSO
{
    [Header("Key Settings")]
    [Tooltip("Shared ID between this key and compatible DoorLock components.")]
    [SerializeField] private string _lockId;

    public string LockId => _lockId;

    public override System.Type InstanceType => typeof(KeyInstance);
    public override ItemInstance CreateInstance() => new KeyInstance(this);
}
