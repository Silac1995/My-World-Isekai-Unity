using System;
using UnityEngine;

namespace MWI.UI.Notifications
{
    public enum ToastType
    {
        Info,
        Warning,
        Error,
        Success
    }

    [Serializable]
    public struct ToastNotificationPayload
    {
        public string Title;
        public string Message;
        public Sprite Icon;
        public ToastType Type;
        public float Duration;

        public ToastNotificationPayload(string message, ToastType type = ToastType.Info, float duration = 3f, string title = "", Sprite icon = null)
        {
            Title = title;
            Message = message;
            Icon = icon;
            Type = type;
            Duration = duration;
        }
    }
}
