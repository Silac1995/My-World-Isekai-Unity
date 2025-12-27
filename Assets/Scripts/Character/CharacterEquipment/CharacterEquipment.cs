using UnityEngine;

public class CharacterEquipment : MonoBehaviour
{
    [SerializeField] private Character character;

    public Character Character
    {
        get => character;
        set => character = value;
    }

    // Tes couches assignées manuellement via [SerializeReference]
    [SerializeReference] private UnderwearLayer underwearLayer;
    [SerializeReference] private ClothingLayer clothingLayer;
    [SerializeReference] private ArmorLayer armorLayer;

    // Getters publics
    public UnderwearLayer UnderwearLayer => underwearLayer;
    public ClothingLayer ClothingLayer => clothingLayer;
    public ArmorLayer ArmorLayer => armorLayer;

    public void Equip(ItemInstance itemInstance)
    {
        if (itemInstance is EquipmentInstance equipmentInstance)
        {
            if (equipmentInstance.ItemSO is EquipmentSO equipmentData)
            {
                EquipmentLayer targetLayer = GetTargetLayer(equipmentData.EquipmentLayer);

                if (targetLayer != null)
                {
                    // --- AJOUT DE LA GESTION DU DOUBLON ---
                    // On demande au Layer si l'instance est déjà équipée dans le slot correspondant
                    if (targetLayer.IsAlreadyEquipped(equipmentInstance))
                    {
                        //Debug.Log($"[Equip] {equipmentData.ItemName} est déjà équipé. On ne fait rien.");
                        return;
                    }

                    Debug.Log($"<color=green>[Equip]</color> Envoi de {equipmentData.ItemName} vers la couche {equipmentData.EquipmentLayer}");
                    targetLayer.Equip(equipmentInstance);
                }
            }
        }
    }

    /// <summary>
    /// Retire un équipement spécifique en fonction de sa couche (Layer) et de son emplacement (Slot).
    /// Gère la destruction de l'instance, la libération du slot et la mise à jour visuelle.
    /// </summary>
    /// <param name="layerType">La couche concernée (Underwear, Clothing, Armor).</param>
    /// <param name="slotType">La partie du corps à libérer (Helmet, Armor, Boots, etc.).</param>
    /// 

    public void Unequip(EquipmentLayerEnum layerType, EquipmentType slotType)
    {
        // 1. On identifie la couche cible
        EquipmentLayer targetLayer = GetTargetLayer(layerType);

        if (targetLayer != null)
        {
            // 2. On vérifie si le slot n'est pas déjà vide pour éviter les calculs inutiles
            if (targetLayer.GetInstance(slotType) == null)
            {
                Debug.Log($"[Unequip] Le slot {slotType} de la couche {layerType} est déjà vide.");
                return;
            }

            // 3. On délègue le nettoyage au Layer
            targetLayer.Unequip(slotType);

            Debug.Log($"<color=orange>[Unequip]</color> Retrait réussi : <b>{slotType}</b> sur <b>{layerType}</b>");
        }
        else
        {
            Debug.LogError($"[Unequip] Impossible de trouver la couche {layerType} sur {gameObject.name}");
        }
    }

    // Logique basée sur l'Enum EquipmentLayerEnum
    private EquipmentLayer GetTargetLayer(EquipmentLayerEnum layerType)
    {
        switch (layerType)
        {
            case EquipmentLayerEnum.Underwear:
                return underwearLayer;
            case EquipmentLayerEnum.Clothing:
                return clothingLayer;
            case EquipmentLayerEnum.Armor:
                return armorLayer;
            default:
                return null;
        }
    }
}