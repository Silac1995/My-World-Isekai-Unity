using UnityEngine;

[CreateAssetMenu(fileName = "FoodSO", menuName = "Scriptable Objects/Items/Food")]
public class FoodSO : ConsumableSO
{
    [Header("Food Settings")]
    [Tooltip("How many points of NeedHunger this food restores when consumed.")]
    [SerializeField] private float _hungerRestored = 30f;

    [Tooltip("Category of food. Used by future cooking/quality systems; ignored in v1.")]
    [SerializeField] private FoodCategory _foodCategory = FoodCategory.Raw;

    public float HungerRestored => _hungerRestored;
    public FoodCategory FoodCategory => _foodCategory;

    public override System.Type InstanceType => typeof(FoodInstance);
    public override ItemInstance CreateInstance() => new FoodInstance(this);
}
