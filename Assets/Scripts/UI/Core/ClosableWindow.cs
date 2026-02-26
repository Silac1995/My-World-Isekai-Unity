using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Core
{
    /// <summary>
    /// Classe de base pour toutes les fenêtres UI qui peuvent être ouvertes et fermées.
    /// Gère automatiquement la liaison avec un bouton de fermeture.
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
        /// Affiche la fenêtre.
        /// </summary>
        public virtual void Open()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// Masque la fenêtre.
        /// </summary>
        public virtual void Close()
        {
            gameObject.SetActive(false);
        }
    }
}
