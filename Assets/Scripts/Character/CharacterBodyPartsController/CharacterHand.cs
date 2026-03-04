using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public class CharacterHand : CharacterBodyPart
{
    [SerializeField] private GameObject _thumbObject;
    [SerializeField] private GameObject _fingersObject;
    [SerializeField] private SpriteRenderer _thumbRenderer;
    [SerializeField] private SpriteRenderer _fingersRenderer;
    [SerializeField] private SpriteResolver _thumbResolver;
    [SerializeField] private SpriteResolver _fingersResolver;
    [SerializeField] private string _category;
    [SerializeField] private string _thumbLabel;
    [SerializeField] private string _fingersLabel;
    [SerializeField] private string _side; // "L" ou "R"

    public GameObject ThumbObject => _thumbObject;
    public GameObject FingersObject => _fingersObject;
    public SpriteRenderer ThumbRenderer => _thumbRenderer;
    public SpriteRenderer FingersRenderer => _fingersRenderer;
    public string Side => _side;

    public CharacterHand(CharacterBodyPartsController controller, GameObject thumbObj, GameObject fingersObj, string category, string side)
        : base(controller)
    {
        _thumbObject = thumbObj;
        _fingersObject = fingersObj;
        _category = category;
        _side = side;
        _thumbLabel = $"Thumb_{side}_normal";
        _fingersLabel = $"Fingers_{side}_normal";

        if (_thumbObject != null)
        {
            _thumbRenderer = _thumbObject.GetComponent<SpriteRenderer>();
            _thumbResolver = _thumbObject.GetComponent<SpriteResolver>();
        }

        if (_fingersObject != null)
        {
            _fingersRenderer = _fingersObject.GetComponent<SpriteRenderer>();
            _fingersResolver = _fingersObject.GetComponent<SpriteResolver>();
        }

        InitializeSprites();
    }

    public void InitializeSprites()
    {
        if (_thumbResolver != null)
            _thumbResolver.SetCategoryAndLabel(_category, _thumbLabel);

        if (_fingersResolver != null)
            _fingersResolver.SetCategoryAndLabel(_category, _fingersLabel);
    }

    public void SetColor(Color color)
    {
        if (_thumbRenderer != null) _thumbRenderer.color = color;
        if (_fingersRenderer != null) _fingersRenderer.color = color;
    }

    public void SetCategory(string newCategory)
    {
        _category = newCategory;

        if (_thumbResolver != null)
        {
            _thumbResolver.SetCategoryAndLabel(_category, _thumbLabel);
            _thumbResolver.ResolveSpriteToSpriteRenderer();
        }

        if (_fingersResolver != null)
        {
            _fingersResolver.SetCategoryAndLabel(_category, _fingersLabel);
            _fingersResolver.ResolveSpriteToSpriteRenderer();
        }
    }

    public void SetCategoryAndLabel(string newCategory, string thumbLabel, string fingersLabel)
    {
        _category = newCategory;
        _thumbLabel = thumbLabel;
        _fingersLabel = fingersLabel;

        if (_thumbResolver != null)
            _thumbResolver.SetCategoryAndLabel(_category, _thumbLabel);

        if (_fingersResolver != null)
            _fingersResolver.SetCategoryAndLabel(_category, _fingersLabel);
    }

    /// <summary>
    /// Change la pose de la main (ex: "normal", "fist").
    /// Met à jour le Thumb et les Fingers de manière synchronisée.
    /// Labels résultants : Thumb_{side}_{pose} / Fingers_{side}_{pose}
    /// </summary>
    public void SetPose(string pose)
    {
        _thumbLabel = $"Thumb_{_side}_{pose}";
        _fingersLabel = $"Fingers_{_side}_{pose}";

        if (_thumbResolver != null)
        {
            string currentCategory = _thumbResolver.GetCategory();
            _thumbResolver.SetCategoryAndLabel(currentCategory, _thumbLabel);
            _thumbResolver.ResolveSpriteToSpriteRenderer();
        }

        if (_fingersResolver != null)
        {
            string currentCategory = _fingersResolver.GetCategory();
            _fingersResolver.SetCategoryAndLabel(currentCategory, _fingersLabel);
            _fingersResolver.ResolveSpriteToSpriteRenderer();
        }
    }
}
