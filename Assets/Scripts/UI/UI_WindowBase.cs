using UnityEngine;
using UnityEngine.UI;

public class UI_WindowBase : MonoBehaviour
{
    [Header("Window Base")]
    [SerializeField] private Button _buttonClose;

    protected virtual void Awake()
    {
        if (_buttonClose != null)
        {
            _buttonClose.onClick.AddListener(CloseWindow);
        }
    }

    protected virtual void OnDestroy()
    {
        if (_buttonClose != null)
        {
            _buttonClose.onClick.RemoveListener(CloseWindow);
        }
    }

    public virtual void CloseWindow()
    {
        gameObject.SetActive(false);
    }
}
