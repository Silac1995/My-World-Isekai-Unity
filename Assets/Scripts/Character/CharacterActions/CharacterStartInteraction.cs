using UnityEngine;

public class CharacterStartInteraction : CharacterAction
{
    private Character _target;

    // On passe par base(character, 0f) car l'interaction est instantanée
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

        // 2. Logique : Créer le lien
        character.CharacterInteraction.StartInteractionWith(_target);

        // 3. IA : Stopper les mouvements
        _target.Controller?.SetBehaviour(new InteractBehaviour());
        character.Controller?.SetBehaviour(new InteractBehaviour());

        // 4. UI : Affichage LOCAL
        if (character.IsPlayer())
        {
            ShowInteractionUI();
        }

        Debug.Log($"<color=cyan>[Action]</color> {character.CharacterName} interagit avec {_target.CharacterName}");
    }

    // OBLIGATOIRE : Même si c'est vide, on doit l'implémenter
    public override void OnApplyEffect()
    {
        // Rien à faire ici pour une interaction de dialogue
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