using UnityEngine;
using UnityEngine.UI;

public class UI_InteractionCharacterScript : UI_InteractionScript
{
    [Header("Character Actions")]
    [SerializeField] private Button followButton;
    [SerializeField] private Button talkButton;
    [SerializeField] private Button _fightButton;
    [SerializeField] private Button _insultButton;
    [SerializeField] private TMPro.TextMeshProUGUI targetNameText;

    [Header("Settings")]
    [SerializeField] private Vector3 _offset = new Vector3(0, 2.5f, 0); 

    [SerializeField] private Character _targetCharacter;

    protected override void Awake()
    {
        base.Awake();
        if (_fightButton == null)
            Debug.LogWarning($"<color=orange>[UI]</color> _fightButton n'est pas assigné sur {gameObject.name}");
    }

    public void Initialize(Character initiator, Character target)
    {
        base.Initialize(initiator);
        this._targetCharacter = target;

        if (targetNameText != null)
            targetNameText.text = target.CharacterName;

        if (followButton != null)
            followButton.onClick.AddListener(OnFollowClicked);

        if (talkButton != null)
            talkButton.onClick.AddListener(OnTalkClicked);

        if (_fightButton != null)
            _fightButton.onClick.AddListener(OnFightClicked);

        if (_insultButton != null)
            _insultButton.onClick.AddListener(OnInsultClicked);

        initiator.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;
        target.CharacterInteraction.OnInteractionStateChanged += HandleInteractionChanged;

        Debug.Log($"<color=cyan>[UI]</color> Initialisée pour {initiator.CharacterName} vs {target.CharacterName}");
    }

    private void Update()
    {
        if (_targetCharacter == null) return;
        FollowTarget();
    }

    public override void Close()
    {
        if (character != null && character.CharacterInteraction.IsInteracting)
        {
            character.CharacterInteraction.EndInteraction();
        }
        else
        {
            CloseUI();
        }
    }

    private void HandleInteractionChanged(Character partner, bool isStarting)
    {
        if (!isStarting) CloseUI();
    }

    private void CloseUI()
    {
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
        if (character != null && _targetCharacter != null)
        {
            ICharacterInteractionAction followAction = new InteractionAskToFollow();
            character.CharacterInteraction.PerformInteraction(followAction);
        }
        Close();
    }

    private void OnTalkClicked()
    {
        if (character != null && _targetCharacter != null)
        {
            ICharacterInteractionAction talkAction = new InteractionTalk();
            character.CharacterInteraction.PerformInteraction(talkAction);
        }
    }

    private void OnInsultClicked()
    {
        if (character != null && _targetCharacter != null)
        {
            ICharacterInteractionAction insultAction = new InteractionInsult();
            character.CharacterInteraction.PerformInteraction(insultAction);
        }
    }

    private void OnFightClicked()
    {
        if (character != null && _targetCharacter != null)
        {
            character.CharacterCombat.StartFight(_targetCharacter);
        }
        Close();
    }
}
