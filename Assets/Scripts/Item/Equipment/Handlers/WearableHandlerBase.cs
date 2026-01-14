using UnityEngine;
using UnityEngine.U2D.Animation;

public abstract class WearableHandlerBase : MonoBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected GameObject _visualContainer;
    [SerializeField] protected string _libraryCategory;
    [SerializeField] protected SpriteLibraryAsset _spriteLibraryAsset;

    /// <summary>
    /// Chaque enfant (Pants, Chest) devra fournir sa liste de GameObjects ici.
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

        // Update sur le container global
        if (_visualContainer != null) UpdatePartCategory(_visualContainer);

        // Update sur les parties spécifiques
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
        // 1. On vérifie d'abord dans le container visuel global (cas du Sac)
        if (_visualContainer != null)
        {
            ApplyColorToSpecificTransform(_visualContainer.transform, subPartName, color);
        }

        // 2. On vérifie aussi dans les parties spécifiques (cas du Chest/Pants)
        foreach (GameObject part in GetAllParts())
        {
            if (part == null) continue;
            ApplyColorToSpecificTransform(part.transform, subPartName, color);
        }
    }

    // Extraction de la logique pour éviter la répétition
    private void ApplyColorToSpecificTransform(Transform root, string subPartName, Color color)
    {
        // Find ne cherche que dans les enfants directs
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