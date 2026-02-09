using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class NeedToWearClothing : CharacterNeed
{
    public NeedToWearClothing(Character character) : base(character) { }

    // Le besoin est actif si le torse OU le bas est exposé
    public override bool IsActive()
    {
        // 1. Vérification de base : est-ce qu'il manque des vêtements ?
        bool needsClothing = _character.CharacterEquipment.IsChestExposed() || _character.CharacterEquipment.IsGroinExposed();

        if (!needsClothing) return false;

        // 2. Vérification de l'action : est-il déjà en train de s'équiper ?
        // Si l'action actuelle est un EquipAction, on considère le besoin comme "en cours de traitement"
        if (_character.CharacterActions.CurrentAction is CharacterEquipAction)
        {
            return false;
        }

        return true;
    }

    public override float GetUrgency()
    {
        if (!IsActive()) return 0f;

        // Priorité maximale si le bas est exposé (Groin)
        if (_character.CharacterEquipment.IsGroinExposed()) return 100f;

        // Priorité moindre si c'est seulement le torse (Chest)
        // (On pourrait ajuster selon le genre via _character.CharacterBio.IsFemale ici)
        return 60f;
    }

    public override void Resolve(NPCController npc)
    {
        // SÉCURITÉ : Si le PNJ a déjà un comportement de mouvement vers un item, on ne fait rien
        // Cela évite d'empiler 5 ordres de marche pour 5 t-shirts différents
        if (npc.HasBehaviour<MoveToTargetBehaviour>()) return;
        // 1. On définit ce qu'on cherche
        List<WearableType> missingTypes = new List<WearableType>();

        if (_character.CharacterEquipment.IsGroinExposed()) missingTypes.Add(WearableType.Pants);
        if (_character.CharacterEquipment.IsChestExposed()) missingTypes.Add(WearableType.Armor);

        //Debug.Log($"<color=white>[Need]</color> {npc.name} cherche : {string.Join(" & ", missingTypes)}");

        // 2. On cherche l'item le plus proche parmi les types manquants
        ItemInteractable targetInteractable = FindClosestWearable(npc.transform.position, missingTypes);

        if (targetInteractable != null && targetInteractable.ItemInstance is EquipmentInstance equip)
        {
            GameObject rootObject = targetInteractable.RootGameObject;

            Action atArrival = () => {
                if (targetInteractable == null) return;

                // 1. Tentative de collecte (vérifie si l'item est toujours là)
                if (targetInteractable.TryCollect())
                {
                    Debug.Log($"<color=green>[Need]</color> {npc.name} a ramassé {equip.ItemSO.ItemName}. Enfilage en cours...");

                    // 2. ON PASSE PAR LE SYSTEME D'ACTIONS
                    // On crée l'action d'équipement (durée 0.8s définie dans ton CharacterEquipAction)
                    CharacterEquipAction equipAction = new CharacterEquipAction(npc.Character, equip);

                    // On demande au composant CharacterActions de l'exécuter
                    npc.Character.CharacterActions.ExecuteAction(equipAction);

                    // 3. Destruction du visuel au sol car l'item est maintenant "dans les mains/inventaire" du PNJ
                    if (rootObject != null)
                    {
                        UnityEngine.Object.Destroy(rootObject);
                    }
                }
                else
                {
                    Debug.Log($"<color=red>[Need]</color> {npc.name} a raté l'objet !");
                }
            };

            npc.PushBehaviour(new MoveToTargetBehaviour(npc, rootObject, 1.2f, atArrival));
        }
        else
        {
            Debug.LogWarning($"<color=red>[Need]</color> {npc.name} n'a rien trouvé pour se couvrir !");
        }
    }

    private ItemInteractable FindClosestWearable(Vector3 currentPosition, List<WearableType> typesToFind)
    {
        var awareness = _character.CharacterAwareness;
        if (awareness == null) return null;

        // On récupère uniquement les ITEM interactables dans la zone
        return awareness.GetVisibleInteractables<ItemInteractable>()
            .Where(item => {
                if (item.ItemInstance is WearableInstance w)
                    return typesToFind.Contains(((WearableSO)w.ItemSO).WearableType);
                return false;
            })
            .OrderBy(item => Vector3.Distance(currentPosition, item.transform.position))
            .FirstOrDefault();
    }
}