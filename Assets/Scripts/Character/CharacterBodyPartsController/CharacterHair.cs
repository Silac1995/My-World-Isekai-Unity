using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public class CharacterHair : CharacterBodyPart
{
    [SerializeField] protected GameObject _hairObject;
    [SerializeField] protected SpriteRenderer _renderer;
    [SerializeField] protected SpriteResolver _resolver;
    [SerializeField] protected string _category;
    [SerializeField] protected string _label;

    public CharacterHair(CharacterBodyPartsController controller, GameObject obj, string category, string label)
        : base(controller)
    {
        _hairObject = obj;
        _category = category;
        _label = label;

        if (_hairObject != null)
        {
            _renderer = _hairObject.GetComponent<SpriteRenderer>();
            _resolver = _hairObject.GetComponent<SpriteResolver>();
        }

        InitializeSprite();
    }

    public virtual void InitializeSprite()
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
}