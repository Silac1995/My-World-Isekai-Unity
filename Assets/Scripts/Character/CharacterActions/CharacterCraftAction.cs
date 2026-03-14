using UnityEngine;

public class CharacterCraftAction : CharacterAction
{
    private CraftingStation _station;
    private ItemSO _itemToCraft;
    private Color _primaryColor;
    private Color _secondaryColor;

    public CharacterCraftAction(Character character, ItemSO itemToCraft, Color primaryColor = default, Color secondaryColor = default, float duration = 1.0f) 
        : base(character, duration)
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
        if (_station != null && _itemToCraft != null)
        {
            _station.Craft(_itemToCraft, character, _primaryColor, _secondaryColor);

            // Si c'est un NPC, il libère la station une fois fini. (Le joueur la libère en fermant la fenêtre UI).
            if (character.Controller is NPCController)
            {
                _station.Release();
            }
        }
        else
        {
            Debug.LogWarning($"<color=orange>[Action]</color> {character.CharacterName} a annulé son craft car la station ou l'objet n'existe plus.");
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
