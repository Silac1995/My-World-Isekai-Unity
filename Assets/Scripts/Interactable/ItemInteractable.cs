using UnityEngine;

public class ItemInteractable : InteractableObject
{
    public override void Interact()
    {
        Debug.Log("Item ramassé !");
        // TODO : Ajouter à l’inventaire
        Destroy(gameObject); // ou désactiver
    }
}
