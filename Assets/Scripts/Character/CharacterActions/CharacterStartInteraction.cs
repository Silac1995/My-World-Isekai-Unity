using UnityEngine;

public class CharacterStartInteraction : CharacterAction
{
    private Character _target;

    // On passe par base(character, 0f) car l'interaction est instantanee
    public CharacterStartInteraction(Character character, Character target) : base(character, 0f)
    {
        _target = target ?? throw new System.ArgumentNullException(nameof(target));
    }

    public override void OnStart()
    {
        if (_target == null)
        {
            Finish();
            return;
        }

        // 1. Visuel : Se faire face
        character.CharacterVisual?.FaceTarget(_target.transform.position);
        _target.CharacterVisual?.FaceTarget(character.transform.position);

        // 2. Logique : Creer le lien
        character.CharacterInteraction.StartInteractionWith(_target);

        // --- CORRECTION ICI ---
        // Le NPC est bloqué nativement via Controller.Freeze() appelé par CharacterInteraction.
        // On n'utilise plus InteractBehaviour.

        if (character.IsPlayer()) ShowInteractionUI();

        Finish();
    }

    // OBLIGATOIRE : Meme si c'est vide, on doit l'implementer
    public override void OnApplyEffect()
    {
        // Rien a faire ici pour une interaction de dialogue
    }

    private void ShowInteractionUI()
    {
        GameObject prefabUI = _target.CharacterInteraction.InteractionActionPrefab;
        if (prefabUI == null) return;

        GameObject worldCanvas = GameObject.Find("WorldUIManager");

        // On utilise Object.Instantiate car CharacterAction n'est pas un MonoBehaviour
        GameObject uiInstance = Object.Instantiate(prefabUI, worldCanvas != null ? worldCanvas.transform : null);

        if (uiInstance.TryGetComponent(out UI_InteractionCharacterScript uiScript))
        {
            uiScript.Initialize(character, _target);
        }
    }
}