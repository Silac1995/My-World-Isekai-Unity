using System.Collections.Generic;
using UnityEngine;

public class UI_CharacterRelations : MonoBehaviour
{
    [SerializeField] private Character _character; // Reference to the owner character
    
    [Header("UI References")]
    [SerializeField] private Transform _slotContainer;
    [SerializeField] private GameObject _relationshipSlotPrefab;

    private List<UI_RelationshipSlot> _spawnedSlots = new List<UI_RelationshipSlot>();

    public void Initialize(Character character)
    {
        if (_character != null && _character.CharacterRelation != null)
        {
            _character.CharacterRelation.OnRelationsUpdated -= HandleRelationsUpdated;
        }

        _character = character;

        if (_character != null && _character.CharacterRelation != null)
        {
            _character.CharacterRelation.OnRelationsUpdated += HandleRelationsUpdated;
            RefreshDisplay();
        }
        else
        {
            ClearSlots();
        }
    }

    private void OnEnable()
    {
        if (_character != null)
        {
            RefreshDisplay();
        }
    }

    private void HandleRelationsUpdated()
    {
        if (gameObject.activeInHierarchy)
        {
            RefreshDisplay();
        }
    }

    public void RefreshDisplay()
    {
        if (_character == null || _character.CharacterRelation == null || _relationshipSlotPrefab == null || _slotContainer == null) 
            return;

        ClearSlots();

        foreach (var relationship in _character.CharacterRelation.Relationships)
        {
            GameObject newSlotObj = Instantiate(_relationshipSlotPrefab, _slotContainer);
            UI_RelationshipSlot slotScript = newSlotObj.GetComponent<UI_RelationshipSlot>();
            
            if (slotScript != null)
            {
                slotScript.Setup(relationship);
                _spawnedSlots.Add(slotScript);
            }
        }
    }

    private void ClearSlots()
    {
        foreach (var slot in _spawnedSlots)
        {
            if (slot != null && slot.gameObject != null)
            {
                Destroy(slot.gameObject);
            }
        }
        _spawnedSlots.Clear();
    }

    private void OnDestroy()
    {
        if (_character != null && _character.CharacterRelation != null)
        {
            _character.CharacterRelation.OnRelationsUpdated -= HandleRelationsUpdated;
        }
    }
}
