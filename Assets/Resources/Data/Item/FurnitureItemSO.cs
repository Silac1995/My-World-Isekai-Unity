using UnityEngine;

[CreateAssetMenu(fileName = "New Furniture Item", menuName = "Scriptable Objects/Items/Furniture")]
public class FurnitureItemSO : ItemSO
{
    [Header("Furniture")]
    [Tooltip("The prefab instantiated when this furniture is placed in the world.")]
    [SerializeField] private Furniture _installedFurniturePrefab;

    public Furniture InstalledFurniturePrefab => _installedFurniturePrefab;

    public override System.Type InstanceType => typeof(FurnitureItemInstance);

    public override ItemInstance CreateInstance()
    {
        return new FurnitureItemInstance(this);
    }
}
