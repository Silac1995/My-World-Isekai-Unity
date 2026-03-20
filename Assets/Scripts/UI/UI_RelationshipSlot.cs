using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class UI_RelationshipSlot : MonoBehaviour, IPointerEnterHandler
{
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _typeText;
    [SerializeField] private TextMeshProUGUI _valueText;
    [SerializeField] private GameObject _newBadge;

    private Relationship _relationship;
    private CharacterRelation _characterRelationComponent;

    public void Setup(Relationship relationship, CharacterRelation relationComponent = null)
    {
        _relationship = relationship;
        _characterRelationComponent = relationComponent;
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

        if (_newBadge != null)
        {
            _newBadge.SetActive(_relationship.IsNewlyAdded);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_relationship != null && _relationship.IsNewlyAdded)
        {
            _relationship.IsNewlyAdded = false;
            UpdateUI();

            if (_characterRelationComponent != null)
            {
                if (!_characterRelationComponent.HasNewRelations())
                {
                    _characterRelationComponent.ClearNotifications();
                }
            }
        }
    }
}
