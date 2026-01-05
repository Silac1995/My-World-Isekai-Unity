using UnityEngine;
using UnityEngine.UI;

public class UI_InteractionCharacterScript : UI_InteractionScript
{
    [Header("Character Actions")]
    [SerializeField] private Button followButton;
    [SerializeField] private TMPro.TextMeshProUGUI targetNameText;

    [Header("Settings")]
    [SerializeField] private Vector3 _offset = new Vector3(0, 2.5f, 0); // Hauteur au-dessus du perso

    [SerializeField] private Character _targetCharacter;

    public void Initialize(Character initiator, Character target)
    {
        base.Initialize(initiator);
        this._targetCharacter = target;

        if (targetNameText != null)
            targetNameText.text = target.CharacterName;

        if (followButton != null)
            followButton.onClick.AddListener(OnTalkClicked);

        // --- ABONNEMENT ---
        // On écoute le changement d'état sur l'initiateur (le joueur)
        initiator.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;

        // Optionnel : on écoute aussi la cible au cas où c'est elle qui coupe
        target.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;

        Debug.Log($"<color=cyan>[UI]</color> Initialisée pour suivre {target.CharacterName}");
    }

    private void Update()
    {
        // Si le personnage est détruit ou l'interaction coupée, on évite les erreurs
        if (_targetCharacter == null) return;

        // Suivi de la position du personnage
        FollowTarget();
    }

    private void HandleInteractionChanged(Character partner, bool isStarting)
    {
        // Si isStarting est false, l'interaction est terminée
        if (!isStarting)
        {
            CloseUI();
        }
    }

    private void CloseUI()
    {
        // On se désabonne pour éviter les fuites de mémoire
        if (character != null)
            character.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;

        if (_targetCharacter != null)
            _targetCharacter.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;

        // Destruction propre
        Destroy(gameObject);
        Debug.Log("<color=orange>[UI]</color> Menu d'interaction fermé et détruit.");
    }

    // N'oublie pas de nettoyer aussi si l'objet est détruit autrement
    private void OnDestroy()
    {
        if (character != null)
            character.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;
    }

    private void FollowTarget()
    {
        // On aligne la position de l'UI sur celle du perso + l'offset
        transform.position = _targetCharacter.transform.position + _offset;
    }

    private void OnTalkClicked()
    {
        // Note: Utilisation de 'character' (l'initiateur hérité de UI_InteractionScript) 
        // et '_targetCharacter'
        Debug.Log($"<color=green>[Action]</color> Ask to follow entre {character.CharacterName} et {_targetCharacter.CharacterName}");
        Close();
    }
}