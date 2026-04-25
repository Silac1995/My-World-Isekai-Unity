using UnityEngine;

public class CharacterCraftAction : CharacterAction
{
    private CraftingStation _station;
    private ItemSO _itemToCraft;
    private Color _primaryColor;
    private Color _secondaryColor;

    public CharacterCraftAction(Character character, ItemSO itemToCraft, Color primaryColor = default, Color secondaryColor = default) 
        : base(character, itemToCraft != null ? itemToCraft.CraftingDuration : 1.0f)
    {
        _itemToCraft = itemToCraft;
        _primaryColor = primaryColor;
        _secondaryColor = secondaryColor;

        if (character != null && character.OccupyingFurniture is CraftingStation station)
        {
            _station = station;
        }
    }

    public override bool CanExecute()
    {
        if (_station == null || _itemToCraft == null || !_station.CanCraft(_itemToCraft))
            return false;

        // --- PROXIMITY CHECK ---
        // Canonical "close enough to interact" gate via InteractableObject.IsCharacterInInteractionZone
        // (project rule). When the station has a paired InteractableObject sibling with an
        // InteractionZone collider, require the character's transform.position to be inside it.
        // Skip the check if no interactable is paired — legacy stations / authored without a
        // CraftingFurnitureInteractable still work (the caller — typically JobBlacksmith — has
        // already done its own arrival validation).
        var stationInteractable = _station.GetComponent<InteractableObject>();
        if (stationInteractable != null && stationInteractable.InteractionZone != null
            && !stationInteractable.IsCharacterInInteractionZone(character))
        {
            Debug.LogWarning($"<color=orange>[Crafting]</color> {character.CharacterName} is not inside {_station.FurnitureName}'s InteractionZone — craft aborted. " +
                             $"Move the character into the zone before triggering CharacterCraftAction.");
            return false;
        }

        // --- SKILL CHECK ---
        if (_itemToCraft.RequiredCraftingSkill != null)
        {
            if (character.CharacterSkills == null) return false;

            if (!character.CharacterSkills.HasRequiredSkillLevel(_itemToCraft.RequiredCraftingSkill, _itemToCraft.RequiredCraftingLevel))
            {
                Debug.LogWarning($"<color=orange>[Crafting]</color> {character.CharacterName} n'a pas le niveau requis en {_itemToCraft.RequiredCraftingSkill.SkillName} pour crafter {_itemToCraft.ItemName}.");
                return false;
            }
        }

        // --- INGREDIENT CHECK ---
        // TODO: Implémenter la vérification d'inventaire quand le système d'inventaire sera disponible
        // Exemple:
        // foreach (var ingredient in _itemToCraft.CraftingRecipe)
        // {
        //     if (!character.Inventory.HasItem(ingredient.Item, ingredient.Amount)) return false;
        // }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} commence à crafter {_itemToCraft.ItemName}.");
    }

    public override void OnApplyEffect()
    {
        if (_station == null || _itemToCraft == null)
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} a annulé son craft car la station ou l'objet n'existe plus.");
            return;
        }

        // Re-validate proximity at apply time — the action's duration window leaves room for
        // the character to drift out of the InteractionZone (knockback, station picked up by
        // someone else, station despawned, etc.) between OnStart and OnApplyEffect. Without this
        // re-check, the craft would still fire and spawn the item even though the worker is no
        // longer at the station. Mirrors the CanExecute proximity gate.
        var stationInteractable = _station.GetComponent<InteractableObject>();
        if (stationInteractable != null && stationInteractable.InteractionZone != null
            && !stationInteractable.IsCharacterInInteractionZone(character))
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} drifted out of {_station.FurnitureName}'s InteractionZone before the craft completed — effect not applied.");
            return;
        }

        var actions = character.CharacterActions;
        if (actions != null && !actions.IsServer)
        {
            // Client owner: delegate to server via RPC
            actions.RequestCraftServerRpc(_itemToCraft.ItemId, _primaryColor, _secondaryColor, _station.transform.position);
        }
        else
        {
            // Server (or offline): execute directly
            _station.Craft(_itemToCraft, character, _primaryColor, _secondaryColor);
        }
    }

    public override void OnCancel()
    {
        base.OnCancel();
        
        // Sécurité : si le craft est annulé en cours de route et que c'est un NPC, on libère la station
        if (_station != null && character.Controller is NPCController)
        {
            if (_station.Occupant == character)
            {
                _station.Release();
            }
        }
    }
}
