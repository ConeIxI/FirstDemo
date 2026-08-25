using System;

namespace GameMain2.Scripts.UI
{
    public sealed class UIConfirmData
    {
        public string Title { get; }
        public string Message { get; }
        public string ConfirmText { get; }
        public string CancelText { get; }
        public Action OnConfirm { get; }
        public Action OnCancel { get; }

        public UIConfirmData(
            string title,
            string message,
            Action onConfirm,
            Action onCancel = null,
            string confirmText = "确定",
            string cancelText = "取消")
        {
            Title = string.IsNullOrEmpty(title) ? "确认" : title;
            Message = string.IsNullOrEmpty(message) ? string.Empty : message;
            ConfirmText = string.IsNullOrEmpty(confirmText) ? "确定" : confirmText;
            CancelText = string.IsNullOrEmpty(cancelText) ? "取消" : cancelText;
            OnConfirm = onConfirm;
            OnCancel = onCancel;
        }
    }
}
