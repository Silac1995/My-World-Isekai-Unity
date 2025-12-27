using UnityEngine;

public class WorldItem : MonoBehaviour
{
    // C'est ici qu'on stocke l'instance (donnée pure)
    [SerializeField] private ItemInstance itemInstance;
    public ItemInstance ItemInstance => itemInstance;

    public void Initialize(ItemInstance instance)
    {
        itemInstance = instance;
    }
}