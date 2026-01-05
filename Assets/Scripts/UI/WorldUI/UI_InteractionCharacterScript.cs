using UnityEngine;
using UnityEngine.UI;

public class UI_InteractionCharacterScript : UI_InteractionScript
{
    [Header("Character Actions")]
    [SerializeField] private Button followButton;
    [SerializeField] private Button talkButton;
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

        // --- Listeners des boutons ---
        if (followButton != null)
            followButton.onClick.AddListener(OnFollowClicked);

        if (talkButton != null)
            talkButton.onClick.AddListener(OnTalkClicked); // Nouvel ajout

        initiator.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;
        target.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;

        Debug.Log($"<color=cyan>[UI]</color> Initialisée pour {initiator.CharacterName} interagissant avec {target.CharacterName}");
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

    // --- Logique du bouton Talk ---
    private void OnTalkClicked()
    {
        Debug.Log("<color=green>[UI]</color> Bouton Talk cliqué.");
        if (character != null && _targetCharacter != null)
        {
            // On crée l'action de discussion
            ICharacterInteractionAction talkAction = new InteractionTalk();

            // On l'exécute (ce qui augmentera la relation de +1 des deux côtés)
            character.CharacterInteraction.PerformInteraction(talkAction);

            // Optionnel : Tu peux choisir de ne pas appeler Close() ici 
            // pour que le joueur puisse continuer à interagir.
            // Si tu veux que l'UI se ferme, ajoute : Close();
        }
    }
}