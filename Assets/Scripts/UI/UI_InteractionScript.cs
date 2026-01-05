using UnityEngine;
using UnityEngine.UI;

public abstract class UI_InteractionScript : MonoBehaviour
{
    [Header("Base UI")]
    [SerializeField] protected Button closeButton;

    protected Character character; // Le personnage qui déclenche l'interaction

    protected virtual void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    public virtual void Initialize(Character initiator)
    {
        this.character = initiator;
    }

    public void Close()
    {
        Destroy(gameObject);
    }
}