using UnityEngine;

[System.Serializable]
public class FoodInstance : ConsumableInstance
{
    public FoodSO FoodData => _itemSO as FoodSO;

    public FoodInstance(FoodSO data) : base(data)
    {
    }

    public override void ApplyEffect(Character character)
    {
        if (character == null)
        {
            Debug.LogWarning("<color=orange>[FoodInstance]</color> ApplyEffect called with null character.");
            return;
        }

        if (FoodData == null)
        {
            Debug.LogError($"<color=red>[FoodInstance]</color> {CustomizedName} has no FoodSO. Skipping effect.");
            return;
        }

        if (character.CharacterNeeds == null)
        {
            Debug.LogWarning($"<color=orange>[FoodInstance]</color> {character.CharacterName} has no CharacterNeeds component.");
            return;
        }

        var hunger = character.CharacterNeeds.GetNeed<NeedHunger>();
        if (hunger == null)
        {
            Debug.LogWarning($"<color=orange>[FoodInstance]</color> {character.CharacterName} has no NeedHunger. Was it registered in CharacterNeeds.Start?");
            return;
        }

        hunger.IncreaseValue(FoodData.HungerRestored);
        Debug.Log($"<color=green>[FoodInstance]</color> {character.CharacterName} ate {CustomizedName} → +{FoodData.HungerRestored} hunger.");
    }
}
