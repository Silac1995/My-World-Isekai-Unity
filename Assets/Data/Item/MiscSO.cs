using UnityEngine;

[CreateAssetMenu(fileName = "MiscItem", menuName = "Scriptable Objects/Items/Misc")]
public class MiscSO : ItemSO
{
    public override System.Type InstanceType => typeof(MiscInstance);
    public override ItemInstance CreateInstance() => new MiscInstance(this);
}