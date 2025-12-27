using UnityEngine;

public class ConsumableInstance : ItemInstance
{
    public ConsumableInstance(ItemSO data) : base(data)
    {
    }
    // Méthode spécifique aux consommables (potions, nourriture, etc.)
    public void UseOnCharacter(Character character)
    {
        if (character != null)
        {
            character.UseConsumable(this); // Attention au nom de ta méthode (Consumable avec un 'u')
            Debug.Log($"{CustomizedName} a été utilisé sur {character.name}");

            // Souvent, on détruit l'objet après usage ou on réduit la quantité
            // Destroy(gameObject); 
        }
    }
}