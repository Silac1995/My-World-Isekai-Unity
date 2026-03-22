using System.Collections.Generic;
using UnityEngine;

namespace MWI.UI.Notifications
{
    /// <summary>
    /// Listens to the Toast Notification Channel and manages an Object Pool of active Toasts.
    /// Ensures we do not allocate memory continuously and properly cleans up events.
    /// </summary>
    public class UI_ToastManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ToastNotificationChannel _toastChannel;
        [SerializeField] private UI_ToastElement _toastPrefab;
        [SerializeField] private Transform _toastContainer;
        
        [Header("Settings")]
        [SerializeField] private int _initialPoolSize = 5;

        private Queue<UI_ToastElement> _toastPool = new Queue<UI_ToastElement>();
        private List<UI_ToastElement> _activeToasts = new List<UI_ToastElement>();

        private void OnEnable()
        {
            if (_toastChannel != null)
            {
                _toastChannel.OnToastRaised += HandleToastRaised;
            }
        }

        private void OnDisable()
        {
            if (_toastChannel != null)
            {
                _toastChannel.OnToastRaised -= HandleToastRaised;
            }
        }

        private void Start()
        {
            InitializePool();
        }

        private void InitializePool()
        {
            if (_toastPrefab == null || _toastContainer == null) return;

            for (int i = 0; i < _initialPoolSize; i++)
            {
                CreatePooledElement();
            }
        }

        private UI_ToastElement CreatePooledElement()
        {
            UI_ToastElement newToast = Instantiate(_toastPrefab, _toastContainer);
            newToast.gameObject.SetActive(false);
            _toastPool.Enqueue(newToast);
            return newToast;
        }

        private void HandleToastRaised(ToastNotificationPayload payload)
        {
            UI_ToastElement toastElement = GetAvailableToast();
            if (toastElement == null) return;

            toastElement.gameObject.SetActive(true);
            toastElement.transform.SetAsLastSibling(); // Ensure it appears at the bottom of a VerticalLayoutGroup
            _activeToasts.Add(toastElement);

            toastElement.Initialize(payload, ReturnToPool);
        }

        private UI_ToastElement GetAvailableToast()
        {
            if (_toastPool.Count > 0)
            {
                return _toastPool.Dequeue();
            }

            // Expand pool if necessary
            return CreatePooledElement();
        }

        private void ReturnToPool(UI_ToastElement element)
        {
            if (element != null)
            {
                element.gameObject.SetActive(false);
                _activeToasts.Remove(element);
                _toastPool.Enqueue(element);
            }
        }
    }
}
