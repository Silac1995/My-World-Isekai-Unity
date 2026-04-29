using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MWI.UI.Interaction
{
    /// <summary>
    /// One row inside <see cref="UI_HarvestInteractionMenu"/>. Bound from a
    /// HarvestInteractionOption. See farming spec §6.2.
    /// </summary>
    public class UI_HarvestInteractionOptionRow : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _outputPreview;
        [SerializeField] private TMP_Text _unavailableReason;
        [SerializeField] private CanvasGroup _canvasGroup;

        public void Bind(HarvestInteractionOption opt, System.Action<HarvestInteractionOption> onSelected)
        {
            if (_icon != null) _icon.sprite = opt.Icon;
            if (_label != null) _label.text = opt.Label;
            if (_outputPreview != null) _outputPreview.text = opt.OutputPreview;
            if (_unavailableReason != null)
                _unavailableReason.text = opt.IsAvailable ? string.Empty : (opt.UnavailableReason ?? string.Empty);
            if (_button != null) _button.interactable = opt.IsAvailable;
            if (_canvasGroup != null) _canvasGroup.alpha = opt.IsAvailable ? 1f : 0.5f;

            if (_button != null)
            {
                _button.onClick.RemoveAllListeners();
                if (opt.IsAvailable)
                {
                    var captured = opt;
                    _button.onClick.AddListener(() => onSelected?.Invoke(captured));
                }
            }
        }
    }
}
