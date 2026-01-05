using UnityEngine;

public class CharacterStartInteraction : CharacterAction
{
    private Character _target;

    public CharacterStartInteraction(Character character, Character target) : base(character)
    {
        _target = target ?? throw new System.ArgumentNullException(nameof(target));
    }

    public override void PerformAction()
    {
        if (_target == null) return;

        // 1. Visuel : Se faire face (Synchronisé pour tous les joueurs)
        character.CharacterVisual?.FaceTarget(_target.transform.position);
        _target.CharacterVisual?.FaceTarget(character.transform.position);

        // 2. Logique : Créer le lien
        character.CharacterInteraction.StartInteractionWith(_target);

        // 3. IA : Stopper les mouvements (Wander -> Interact)
        _target.Controller?.SetBehaviour(new InteractBehaviour());
        character.Controller?.SetBehaviour(new InteractBehaviour());

        // 4. UI : Affichage LOCAL (Seulement pour le joueur qui contrôle 'character')
        if (character.IsPlayer())
        {
            ShowInteractionUI();
        }

        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} interagit avec {_target.CharacterName}");
    }

    private void ShowInteractionUI()
    {
        GameObject prefabUI = _target.CharacterInteraction.InteractionActionPrefab;
        if (prefabUI == null) return;

        // On cherche le WorldUIManager dans la scène
        GameObject worldCanvas = GameObject.Find("WorldUIManager");
        if (worldCanvas == null)
        {
            Debug.LogError("WorldUIManager non trouvé ! L'UI sera instanciée sans parent.");
        }

        // Instanciation dans le WorldUIManager
        GameObject uiInstance = Object.Instantiate(prefabUI, worldCanvas != null ? worldCanvas.transform : null);

        if (uiInstance.TryGetComponent(out UI_InteractionCharacterScript uiScript))
        {
            uiScript.Initialize(character, _target);
        }
    }
}