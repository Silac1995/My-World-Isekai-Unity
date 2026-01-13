using UnityEngine;

public class CharacterPickUpItem : CharacterAction
{
    private ItemInstance _item;
    private GameObject _worldObject; // On stocke l'objet à détruire

    public CharacterPickUpItem(Character character, ItemInstance item, GameObject worldObject) : base(character, 3f)
    {
        _item = item;
        _worldObject = worldObject;

        // On récupère la durée ici, AVANT le OnStart
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler != null)
        {
            float duration = animHandler.GetCachedDuration("Female_Humanoid_Pickup_from_ground_00");
            if (duration > 0)
            {
                this.Duration = duration;
            }
        }
    }

    public override void OnStart()
    {
        var animHandler = character.CharacterVisual?.CharacterAnimator;
        if (animHandler?.Animator != null)
        {
            // 1. Utilisation du Hash pour le Trigger (PickUpItem)
            animHandler.Animator.SetTrigger(CharacterAnimator.ActionTrigger);

        }

        // Sécurité physique sur l'objet au sol
        if (_worldObject != null)
        {
            var rb = _worldObject.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }
    }

    public override void OnApplyEffect()
    {
        var inventory = character.CharacterEquipment.GetInventory();

        if (inventory != null && inventory.AddItem(_item, character))
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
        // 1. On vérifie si le personnage a un inventaire
        var inventory = character.CharacterEquipment.GetInventory();

        if (inventory == null)
        {
            Debug.LogWarning($"[Action] {character.CharacterName} n'a pas de sac pour ramasser.");
            return false;
        }

        // 2. Vérification de la place disponible selon le type d'item
        if (!inventory.HasFreeSpaceForItem(_item))
        {
            Debug.LogWarning($"[Action] Pas de place dans l'inventaire pour {_item.CustomizedName}.");
            // Optionnel : Tu pourrais ici déclencher un message UI "Inventaire Plein"
            return false;
        }

        return true;
    }
}