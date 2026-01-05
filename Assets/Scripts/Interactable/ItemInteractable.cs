using NUnit.Framework.Internal.Execution;
using UnityEngine;

public class ItemInteractable : InteractableObject
{
    // On récupère le script WorldItem qui sert de conteneur
    private WorldItem _worldItem;
    public WorldItem WorldItem => _worldItem;

    public ItemInstance ItemInstance
    {
        get
        {
            // On cherche d'abord si on a déjà une référence
            if (_worldItem == null)
            {
                // On cherche sur le parent en priorité !
                _worldItem = GetComponentInParent<WorldItem>();

                // Si vraiment pas de parent, on cherche sur soi-même
                if (_worldItem == null) _worldItem = GetComponent<WorldItem>();
            }
            return (_worldItem != null) ? _worldItem.ItemInstance : null;
        }
    }

    public override void Interact(Character interactor)
    {
        if (ItemInstance == null)
        {
            Debug.LogError($"[FATAL] Aucun ItemInstance trouvé via WorldItem sur {name}.");
            return;
        }

        // Affiche le nom de l'item ET le nom de sa classe technique (ex: EquipmentInstance)
        Debug.Log($"[INTERACT] Objet : {ItemInstance.ItemSO.ItemName} | Type de classe : <color=yellow>{ItemInstance.GetType().Name}</color>");

        // Ta logique d'interaction...
    }
}