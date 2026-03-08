using System.Linq;
using UnityEngine;

/// <summary>
/// Besoin (Need) poussant un NPC à aller acheter un objet spécifique dans un magasin.
/// Peut être déclenché quand le NPC a de l'argent et manque de nourriture ou d'équipement.
/// </summary>
public class NeedShopping : CharacterNeed
{
    private const float BASE_URGENCY = 55f;
    private ItemSO _desiredItem;

    // Pour l'instant, on hardcode un désir (ex: le PNJ veut une épée ou une pomme)
    public NeedShopping(Character character, ItemSO itemToBuy) : base(character)
    {
        _desiredItem = itemToBuy;
    }

    public override bool IsActive()
    {
        // Actif si le personnage est un PNJ ET qu'il a ce désir défini.
        if (_character.Controller is PlayerController) return false;
        
        return _desiredItem != null; // TODO: Ajouter une vérif d'inventaire, d'argent, etc.
    }

    public override float GetUrgency()
    {
        return BASE_URGENCY;
    }

    public override bool Resolve(NPCController npc)
    {
        if (BuildingManager.Instance == null || BuildingManager.Instance.allBuildings.Count == 0)
        {
            return false;
        }

        // 1. Chercher un ShopBuilding qui vend ce qu'on veut
        foreach (var building in BuildingManager.Instance.allBuildings)
        {
            if (building is ShopBuilding shop && shop.ItemsToSell.Contains(_desiredItem))
            {
                // Vérifier s'il y est en stock
                if (shop.HasItemInStock(_desiredItem))
                {
                    Debug.Log($"<color=magenta>[NeedShopping]</color> {_character.CharacterName} part acheter {_desiredItem.ItemName} chez {shop.BuildingName}.");

                    // On lance le comportement pour aller au magasin et faire la queue
                    npc.PushBehaviour(new MoveToTargetBehaviour(npc, shop.gameObject, 3f, () =>
                    {
                        // Une fois arrivé au magasin, on démarre le mode "File d'attente"
                        npc.PushBehaviour(new WaitInQueueBehaviour(npc, shop, _desiredItem));
                    }));

                    return true;
                }
            }
        }

        // Aucun magasin trouvé (ou rupture de stock)
        return false;
    }
}
