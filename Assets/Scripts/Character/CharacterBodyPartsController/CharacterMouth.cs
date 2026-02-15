using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public class CharacterMouth : CharacterBodyPart
{
    [SerializeField] private GameObject _mouthObject;
    [SerializeField] private string _mouthCategory = "01";

    private SpriteRenderer _renderer;
    private SpriteResolver _resolver;

    public CharacterMouth(CharacterBodyPartsController controller, GameObject mouthObject, string category = "01")
        : base(controller)
    {
        _mouthObject = mouthObject;
        _mouthCategory = category;

        _renderer = _mouthObject?.GetComponent<SpriteRenderer>();
        _resolver = _mouthObject?.GetComponent<SpriteResolver>();
    }

    public void SetSprite(string labelName)
    {
        if (_resolver != null)
        {
            _resolver.SetCategoryAndLabel(_mouthCategory, labelName);
            _resolver.ResolveSpriteToSpriteRenderer();
        }
    }

    public void SetColor(Color color)
    {
        if (_renderer != null) _renderer.color = color;
    }
}