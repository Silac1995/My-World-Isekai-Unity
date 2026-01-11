using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ConsumableInstance : MiscInstance
{
    // Attribut privé pour stocker les effets à l'instance
    [SerializeField] private List<string> _effects = new List<string>();

    // Raccourci vers les données SO
    public ConsumableSO ConsumableData => _itemSO as ConsumableSO;

    // Getter pour accéder aux effets de l'instance
    public List<string> Effects => _effects;

    public ConsumableInstance(ConsumableSO data) : base(data)
    {
        // Initialisation de la liste en reprenant les données du SO
        if (data != null && data.Effects != null)
        {
            _effects = new List<string>(data.Effects);
        }
    }

    /// <summary>
    /// Ajoute un effet supplémentaire à cette instance spécifique (ex: enchantement temporaire)
    /// </summary>
    public void AddEffect(string newEffect)
    {
        if (!string.IsNullOrWhiteSpace(newEffect))
        {
            _effects.Add(newEffect);
            Debug.Log($"<color=orange>[Item]</color> Effet '{newEffect}' ajouté à {CustomizedName}");
        }
    }
}