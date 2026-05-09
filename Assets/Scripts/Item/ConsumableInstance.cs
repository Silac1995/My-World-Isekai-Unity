using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ConsumableInstance : MiscInstance
{
    // Attribut priv� pour stocker les effets � l'instance
    [SerializeField] private List<string> _effects = new List<string>();

    // Raccourci vers les donn�es SO
    public ConsumableSO ConsumableData => _itemSO as ConsumableSO;

    // Getter pour acc�der aux effets de l'instance
    public List<string> Effects => _effects;

    public ConsumableInstance(ConsumableSO data) : base(data)
    {
        // Initialisation de la liste en reprenant les donn�es du SO
        if (data != null && data.Effects != null)
        {
            _effects = new List<string>(data.Effects);
        }
    }

    /// <summary>
    /// Ajoute un effet suppl�mentaire � cette instance sp�cifique (ex: enchantement temporaire)
    /// </summary>
    public void AddEffect(string newEffect)
    {
        if (!string.IsNullOrWhiteSpace(newEffect))
        {
            _effects.Add(newEffect);
            Debug.Log($"<color=orange>[Item]</color> Effet '{newEffect}' ajout� � {CustomizedName}");
        }
    }

    /// <summary>
    /// Applies this consumable's runtime effect to the given character.
    /// Default no-op; subclasses (FoodInstance, PotionInstance, …) override.
    /// Called from <see cref="Character.UseConsumable"/> after the use animation completes.
    /// </summary>
    public virtual void ApplyEffect(Character character)
    {
        // No-op default. Specific consumable subtypes override.
    }
}
