using TMPro;
using UnityEngine;

/// <summary>
/// Base class for one category of the Character inspector. Each concrete sub-tab implements
/// <see cref="RenderContent"/> to produce a formatted string; exception isolation and the
/// error-line fallback are centralized here.
/// </summary>
public abstract class CharacterSubTab : MonoBehaviour
{
    [SerializeField] protected TMP_Text _content;

    /// <summary>
    /// Refresh the sub-tab with the given character. Safe to call every frame.
    /// Virtual so widget-based sub-tabs (e.g. <c>CharacterCityFoundingSubTab</c>) can
    /// bypass the text-only contract and render UGUI widgets directly under their
    /// own transform. The default implementation is text-only (TMP_Text via
    /// <c>RenderContent</c>); the 10 stock sub-tabs all rely on it.
    /// </summary>
    public virtual void Refresh(Character c)
    {
        if (_content == null) return;

        if (c == null)
        {
            _content.text = "<color=grey>No character selected.</color>";
            return;
        }

        try
        {
            _content.text = RenderContent(c);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
            _content.text = $"<color=red>⚠ {GetType().Name} failed — {e.Message}</color>";
        }
    }

    /// <summary>Called when the inspector detaches. Override to clear per-target caches if any.</summary>
    public virtual void Clear()
    {
        if (_content != null) _content.text = "<color=grey>No character selected.</color>";
    }

    /// <summary>Produce the formatted content for this sub-tab.</summary>
    protected abstract string RenderContent(Character c);
}
