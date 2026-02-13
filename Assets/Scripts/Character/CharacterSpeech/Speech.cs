using UnityEngine;
using TMPro;

public class Speech : MonoBehaviour
{
    private Character _character;
    [SerializeField] private TextMeshProUGUI _textElement; // Assigne-le dans l'inspecteur du prefab

    public void Setup(Character owner, string message)
    {
        _character = owner;
        if (_textElement != null)
        {
            _textElement.text = message;
        }
    }
}