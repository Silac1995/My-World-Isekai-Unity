using UnityEngine;

public class CharacterStartInteraction : CharacterAction
{
    private Character target;

    public CharacterStartInteraction(Character character, Character target) : base(character)
    {
        this.target = target ?? throw new System.ArgumentNullException(nameof(target));
    }

    public override void PerformAction()
    {
        // 1. Sécurité supplémentaire
        if (target == null) return;

        // 2. Faire face à l'autre personnage
        // On utilise la nouvelle méthode FaceTarget du CharacterVisual
        if (character.CharacterVisual != null)
        {
            character.CharacterVisual.FaceTarget(target.transform.position);
        }

        if (target.CharacterVisual != null)
        {
            target.CharacterVisual.FaceTarget(character.transform.position);
        }

        // 3. Logique de lien via CharacterInteraction
        character.CharacterInteraction.StartInteractionWith(target);

        // 4. Changement de Behaviour pour la cible
        var targetController = target.GetComponent<CharacterGameController>();
        if (targetController != null)
        {
            targetController.SetBehaviour(new InteractBehaviour());
        }

        // 5. Changement de Behaviour pour l'initiateur
        var sourceController = character.GetComponent<CharacterGameController>();
        if (sourceController != null)
        {
            sourceController.SetBehaviour(new InteractBehaviour());
        }

        Debug.Log($"{character.CharacterName} et {target.CharacterName} se font désormais face.");

        // UI_InteractionMenu.Show(character, target);
    }
}