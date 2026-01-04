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
        // 1. Sécurité supplémentaire (même si checké au constructeur)
        if (target == null) return;

        // 2. On lance la logique de lien via CharacterInteraction
        // Cela va remplir le "CurrentTarget" des deux personnages
        character.CharacterInteraction.StartInteractionWith(target);

        // 3. Changement de Behaviour pour le NPC (Cible)
        // On le force à s'arrêter et à regarder l'initiateur
        var targetController = target.GetComponent<CharacterGameController>();
        if (targetController != null)
        {
            targetController.SetBehaviour(new InteractBehaviour());
        }

        // 4. Changement de Behaviour pour l'initiateur (Joueur ou autre NPC)
        var sourceController = character.GetComponent<CharacterGameController>();
        if (sourceController != null)
        {
            sourceController.SetBehaviour(new InteractBehaviour());
        }

        Debug.Log($"{character.CharacterName} a ouvert une session d'interaction avec {target.CharacterName}.");

        // C'est ici que tu pourrais appeler ton UI de menu d'options
        // UI_InteractionMenu.Show(character, target);
    }
}