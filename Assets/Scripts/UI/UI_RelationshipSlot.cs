using TMPro;
using UnityEngine;

public class UI_RelationshipSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _typeText;
    [SerializeField] private TextMeshProUGUI _valueText;

    private Relationship _relationship;

    public void Setup(Relationship relationship)
    {
        _relationship = relationship;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_relationship == null) return;

        if (_nameText != null)
            _nameText.text = _relationship.RelatedCharacter != null ? _relationship.RelatedCharacter.CharacterName : "Unknown";
        
        if (_typeText != null)
            _typeText.text = _relationship.RelationType.ToString();
        
        if (_valueText != null)
        {
            _valueText.text = _relationship.RelationValue.ToString();
            
            // Optional: Color code the value
            if (_relationship.RelationValue > 0)
                _valueText.color = Color.green;
            else if (_relationship.RelationValue < 0)
                _valueText.color = Color.red;
            else
                _valueText.color = Color.white;
        }
    }
}
