using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class NeedToWearClothing : CharacterNeed
{
    public NeedToWearClothing(Character character) : base(character) { }

    // Le besoin est actif si le torse OU le bas est exposé
    public override bool IsActive() =>
        _character.CharacterEquipment.IsChestExposed() || _character.CharacterEquipment.IsGroinExposed();

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
        // 1. On définit ce qu'on cherche
        List<WearableType> missingTypes = new List<WearableType>();

        if (_character.CharacterEquipment.IsGroinExposed()) missingTypes.Add(WearableType.Pants);
        if (_character.CharacterEquipment.IsChestExposed()) missingTypes.Add(WearableType.Armor);

        Debug.Log($"<color=white>[Need]</color> {npc.name} cherche : {string.Join(" & ", missingTypes)}");

        // 2. On cherche l'item le plus proche parmi les types manquants
        ItemInteractable targetInteractable = FindClosestWearable(npc.transform.position, missingTypes);

        if (targetInteractable != null && targetInteractable.ItemInstance is EquipmentInstance equip)
        {
            GameObject rootObject = targetInteractable.RootGameObject;

            Action atArrival = () => {
                if (targetInteractable == null) return;

                // On essaie de "réserver" la collecte
                if (targetInteractable.TryCollect())
                {
                    // SI TRUE : On est le premier !
                    Debug.Log($"<color=green>[WINNER]</color> {npc.name} a ramassé l'objet en premier !");

                    if (equip != null)
                    {
                        npc.Character.CharacterEquipment.Equip(equip);
                    }

                    if (rootObject != null)
                    {
                        UnityEngine.Object.Destroy(rootObject);
                    }
                }
                else
                {
                    // SI FALSE : Quelqu'un d'autre l'a pris durant cette frame ou la précédente
                    Debug.Log($"<color=red>[LOSER]</color> {npc.name} a touché l'objet mais un autre a été plus rapide !");
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
        if (typesToFind.Count == 0) return null;

        return UnityEngine.Object.FindObjectsByType<ItemInteractable>(FindObjectsSortMode.None)
            .Where(interactable =>
            {
                if (interactable.ItemInstance is WearableInstance wearable && wearable.ItemSO is WearableSO data)
                {
                    // Est-ce que le type de cet item est dans notre liste de besoins ?
                    return typesToFind.Contains(data.WearableType);
                }
                return false;
            })
            .OrderBy(interactable => Vector3.Distance(currentPosition, interactable.transform.position))
            .FirstOrDefault();
    }
}