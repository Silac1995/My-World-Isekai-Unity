using UnityEngine;
using UnityEngine.UI;

public abstract class UI_InteractionScript : MonoBehaviour
{
    [Header("Base UI")]
    [SerializeField] protected Button closeButton;

	[SerializeField] protected Character character; // Le personnage qui déclenche l'interaction
    public Character Character => character;

	protected virtual void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public virtual void Initialize(Character initiator)
    {
        this.character = initiator;
    }

    public virtual void Close()
    {
        Destroy(gameObject);
    }
}