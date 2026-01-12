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
        // Empêche le NPC de pousser l'objet physiquement pendant qu'il le ramasse
        var rb = _worldObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true; // L'objet ne bougera plus par la physique
        }

        // Lance l'animation
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            animHandler.Animator.SetTrigger("Trigger_pickUpItem");
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