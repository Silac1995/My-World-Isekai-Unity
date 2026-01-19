using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public class CharacterEar : CharacterBodyPart
{
    [SerializeField] private GameObject _earObject;
    [SerializeField] private SpriteRenderer _renderer;
    [SerializeField] private SpriteResolver _resolver;
    [SerializeField] private string _category;
    [SerializeField] private string _label;

    public CharacterEar(CharacterBodyPartsController controller, GameObject obj, string category, string label)
        : base(controller)
    {
        _earObject = obj;
        _category = category;
        _label = label;

        if (_earObject != null)
        {
            _renderer = _earObject.GetComponent<SpriteRenderer>();
            _resolver = _earObject.GetComponent<SpriteResolver>();
        }

        InitializeSprite();
    }

    public void InitializeSprite()
    {
        if (_resolver != null)
        {
            _resolver.SetCategoryAndLabel(_category, _label);
        }
    }

    public void SetColor(Color color)
    {
        if (_renderer != null) _renderer.color = color;
    }

    public void SetCategory(string newCategory)
    {
        if (_resolver == null) return;

        _category = newCategory;
        // On utilise _label (qui est Ear_L ou Ear_R) plutôt que GetLabel()
        _resolver.SetCategoryAndLabel(_category, _label);

        // Force la mise à jour immédiate pour éviter les décalages de frame
        _resolver.ResolveSpriteToSpriteRenderer();
    }

    public void SetLabel(string newLabel)
    {
        if (_resolver == null) return;

        _label = newLabel;
        // On récupère la catégorie actuelle proprement
        string currentCategory = _resolver.GetCategory();
        _resolver.SetCategoryAndLabel(currentCategory, _label);
    }

    public void SetCategoryAndLabel(string newCategory, string newLabel)
    {
        if (_resolver == null) return;

        _category = newCategory;
        _label = newLabel;
        _resolver.SetCategoryAndLabel(_category, _label);
    }
}