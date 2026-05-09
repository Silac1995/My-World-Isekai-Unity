using UnityEngine;
using UnityEngine.U2D.Animation;

public abstract class WearableHandlerBase : MonoBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected GameObject _visualContainer;
    [SerializeField] protected string _libraryCategory;
    [SerializeField] protected SpriteLibraryAsset _spriteLibraryAsset;

    /// <summary>
    /// Each child (Pants, Chest) must provide its list of GameObjects here.
    /// </summary>
    protected abstract GameObject[] GetAllParts();

    public void Initialize(SpriteLibraryAsset asset)
    {
        _spriteLibraryAsset = asset;
        UpdateAllSpriteLibraries();
    }

    public void SetLibraryCategory(string category)
    {
        _libraryCategory = category;

        // Update on the global container
        if (_visualContainer != null) UpdatePartCategory(_visualContainer);

        // Update on the specific parts
        foreach (GameObject part in GetAllParts())
        {
            UpdatePartCategory(part);
        }
    }

    private void UpdatePartCategory(GameObject partRoot)
    {
        if (partRoot == null) return;

        ApplyCategory(partRoot);

        foreach (Transform child in partRoot.transform)
        {
            ApplyCategory(child.gameObject);
        }
    }

    private void ApplyCategory(GameObject go)
    {
        if (go.TryGetComponent(out SpriteResolver resolver))
        {
            resolver.SetCategoryAndLabel(_libraryCategory, resolver.GetLabel());
        }
    }

    public void SetPrimaryColor(Color color) => ApplyColorToAll("Color_Primary", color);
    public void SetSecondaryColor(Color color) => ApplyColorToAll("Color_Secondary", color);
    public void SetMainColor(Color color) => ApplyColorToAll("Color_Main", color);

    private void ApplyColorToAll(string subPartName, Color color)
    {
        // 1. First check inside the global visual container (Bag case)
        if (_visualContainer != null)
        {
            ApplyColorToSpecificTransform(_visualContainer.transform, subPartName, color);
        }

        // 2. Also check inside the specific parts (Chest/Pants case)
        foreach (GameObject part in GetAllParts())
        {
            if (part == null) continue;
            ApplyColorToSpecificTransform(part.transform, subPartName, color);
        }
    }

    public void SetVisibility(bool isVisible)
    {
        // 1. Global container
        if (_visualContainer != null)
        {
            ApplyVisibilityToRenderers(_visualContainer, isVisible);
        }

        // 2. Specific parts
        foreach (GameObject part in GetAllParts())
        {
            if (part == null) continue;
            ApplyVisibilityToRenderers(part, isVisible);
        }
    }

    private void ApplyVisibilityToRenderers(GameObject root, bool isVisible)
    {
        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in renderers)
        {
            sr.enabled = isVisible;
        }
    }

    // Extracted helper to avoid duplicating the logic
    private void ApplyColorToSpecificTransform(Transform root, string subPartName, Color color)
    {
        Transform child = root.Find(subPartName);
        if (child != null && child.TryGetComponent(out SpriteRenderer sr))
        {
            sr.color = color;
        }
    }

    private void UpdateAllSpriteLibraries()
    {
        SpriteLibrary[] libraries = GetComponentsInChildren<SpriteLibrary>(true);
        foreach (var lib in libraries)
        {
            lib.spriteLibraryAsset = _spriteLibraryAsset;
        }
    }
}
