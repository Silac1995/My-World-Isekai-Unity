using UnityEngine;
using UnityEngine.UI;

public class UI_InteractionCharacterScript : UI_InteractionScript
{
    [Header("Character Actions")]
    [SerializeField] private Button followButton;
    [SerializeField] private TMPro.TextMeshProUGUI targetNameText;

    [Header("Settings")]
    [SerializeField] private Vector3 _offset = new Vector3(0, 2.5f, 0); 

    [SerializeField] private Character _targetCharacter;

    protected override void Awake()
    {
        // On appelle base.Awake() pour s'assurer que le closeButton est bien écouté
        base.Awake();

        // Vérification immédiate si le bouton est assigné
        if (closeButton == null)
            Debug.LogError($"<color=red>[UI Error]</color> closeButton n'est pas assigné dans l'inspecteur sur {gameObject.name}");
        else
            Debug.Log($"<color=green>[UI]</color> closeButton détecté et prêt sur {gameObject.name}");
    }

    public void Initialize(Character initiator, Character target)
    {
        base.Initialize(initiator);
        this._targetCharacter = target;

        if (targetNameText != null)
            targetNameText.text = target.CharacterName;

        if (followButton != null)
            followButton.onClick.AddListener(OnFollowClicked);

        initiator.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;
        target.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;

        Debug.Log($"<color=cyan>[UI]</color> Initialisée pour suivre {target.CharacterName}");
    }

    private void Update()
    {
        if (_targetCharacter == null) return;
        FollowTarget();
    }

    public override void Close()
    {
        Debug.Log("<color=yellow>[UI]</color> Bouton Close cliqué ou méthode Close appelée.");

        if (character != null && character.CharacterInteraction.IsInteracting)
        {
            Debug.Log($"<color=yellow>[UI]</color> Demande de fin d'interaction à {character.CharacterName}");
            character.CharacterInteraction.EndInteraction();
        }
        else
        {
            Debug.Log("<color=yellow>[UI]</color> Pas d'interaction active, fermeture directe de l'UI.");
            CloseUI();
        }
    }

    private void HandleInteractionChanged(Character partner, bool isStarting)
    {
        if (!isStarting)
        {
            Debug.Log("<color=orange>[UI]</color> Event reçu : Fin d'interaction détectée.");
            CloseUI();
        }
    }

    private void CloseUI()
    {
        Debug.Log("<color=red>[UI]</color> Exécution de CloseUI (Destruction de l'objet).");

        if (character != null)
            character.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;

        if (_targetCharacter != null)
            _targetCharacter.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;

        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (character != null)
            character.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;

        if (_targetCharacter != null)
            _targetCharacter.CharacterInteraction.OnInteractionStateChanged -= HandleInteractionChanged;
    }

    private void FollowTarget()
    {
        transform.position = _targetCharacter.transform.position + _offset;
    }

    private void OnFollowClicked()
    {
        Debug.Log("<color=green>[UI]</color> Bouton Follow cliqué.");
        if (character != null && _targetCharacter != null)
        {
            ICharacterInteractionAction followAction = new InteractionAskToFollow();
            character.CharacterInteraction.PerformInteraction(followAction);
        }

        Close();
    }
}