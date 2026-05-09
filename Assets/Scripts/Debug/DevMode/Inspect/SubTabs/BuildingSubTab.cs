using UnityEngine;

/// <summary>
/// Base class for one category of the Building inspector. Mirrors
/// <see cref="CharacterSubTab"/>: the host inspector dispatches a per-frame
/// <see cref="Refresh(Building)"/> on the active sub-tab and skips inactive ones.
/// Exception isolation lives here so a thrown sub-tab cannot wipe the rest of the
/// dev panel.
/// </summary>
public abstract class BuildingSubTab : MonoBehaviour
{
    /// <summary>
    /// Refresh the sub-tab with the given building. Safe to call every frame.
    /// Centralises null-target handling and the try/catch so subclasses can
    /// just override <see cref="DoRefresh"/>.
    /// </summary>
    public void Refresh(Building b)
    {
        if (b == null) { DoClear(); return; }
        try
        {
            DoRefresh(b);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e, this);
        }
    }

    /// <summary>Inspector detached. Override to clear caches / widget state.</summary>
    public virtual void Clear() => DoClear();

    /// <summary>Concrete sub-tab work — read building state and update widgets / text.</summary>
    protected abstract void DoRefresh(Building b);

    /// <summary>Concrete sub-tab cleanup — wipe text/widgets to a "no target" state.</summary>
    protected virtual void DoClear() { }
}
