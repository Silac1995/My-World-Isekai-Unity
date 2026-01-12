using UnityEngine;

public class CharacterPickUpItem : CharacterAction
{
    private ItemInstance _item;
    private GameObject _worldObject; // On stocke l'objet à détruire

    public CharacterPickUpItem(Character character, ItemInstance item, GameObject worldObject) : base(character, 0.5f)
    {
        _item = item;
        _worldObject = worldObject;
    }

    public override void OnStart()
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            animHandler.Animator.SetTrigger("Trigger_pickUpItem");

            // On attend la fin de la frame pour que l'Animator passe sur le nouvel état
            // Ou plus simplement, on définit une durée basée sur le clip connu
            this.Duration = animHandler.GetClipDuration("Pickup");
        }
    }

    public override void OnApplyEffect()
    {
        var inventory = character.CharacterEquipment.GetInventory();

        if (inventory != null && inventory.AddItem(_item))
        {
            // Le ramassage a réussi, on détruit l'objet au sol
            if (_worldObject != null)
            {
                Object.Destroy(_worldObject);
            }
        }
        else
        {
            Debug.LogWarning("Ramassage échoué : l'objet reste au sol.");
        }
    }
    public override bool CanExecute()
    {
        // On vérifie si le personnage a un inventaire
        var inventory = character.CharacterEquipment.GetInventory();

        if (inventory == null)
        {
            Debug.LogWarning($"[Action] {character.CharacterName} n'a pas de sac pour ramasser.");
            return false;
        }

        // Tu peux même ajouter une vérification de place disponible ici
        // if (!inventory.HasFreeSpaceForItem(_item)) return false;

        return true;
    }
}