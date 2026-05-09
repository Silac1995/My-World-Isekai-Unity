using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Core
{
    /// <summary>
    /// Base class for every UI window that can be opened and closed.
    /// Automatically wires up the binding with a close button.
    /// </summary>
    public abstract class ClosableWindow : MonoBehaviour
    {
        [Header("Closable Window Settings")]
        [SerializeField] private Button _closeButton;

        protected virtual void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Close);
            }
        }

        /// <summary>
        /// Shows the window.
        /// </summary>
        public virtual void Open()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Hides the window.
        /// </summary>
        public virtual void Close()
        {
            gameObject.SetActive(false);
        }
    }
}
