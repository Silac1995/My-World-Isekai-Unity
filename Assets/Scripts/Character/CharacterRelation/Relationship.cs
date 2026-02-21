using UnityEngine;

[System.Serializable]
public class Relationship
{
    [SerializeField] private Character _character; // Le propriétaire (celui qui a ce sentiment)
    [SerializeField] private Character _relatedCharacter; // La cible du sentiment
    [SerializeField] private int _relationValue;
    [SerializeField] private RelationshipType _relationshipType = RelationshipType.Stranger;
    [SerializeField] private bool _hasMet = false;

    // Constructeur mis à jour avec le propriétaire
    public Relationship(Character owner, Character relatedCharacter, int initialValue = 0, RelationshipType initialType = RelationshipType.Stranger)
    {
        _character = owner;
        _relatedCharacter = relatedCharacter;
        _relationValue = Mathf.Clamp(initialValue, -100, 100);
        _relationshipType = initialType;
        _hasMet = false;

        UpdateRelationshipType();
    }

    public Character Character => _character;
    public Character RelatedCharacter => _relatedCharacter;
    public RelationshipType RelationType => _relationshipType;
    public bool HasMet => _hasMet;

    public int RelationValue
    {
        get => _relationValue;
        set 
        {
            _relationValue = Mathf.Clamp(value, -100, 100);
            UpdateRelationshipType();
        }
    }

    /// <summary>
    /// Vérifie si les deux personnages se sont rencontrés mutuellement.
    /// </summary>
    public bool BothHaveMet()
    {
        // 1. Ma propre perception (on est déjà dans cet objet)
        bool iKnowHim = _hasMet;

        // 2. La perception de l'autre
        if (_relatedCharacter == null) return false;

        var otherRelationSystem = _relatedCharacter.GetComponentInChildren<CharacterRelation>();
        if (otherRelationSystem == null) return false;

        // On demande au système de l'autre sa relation AVEC le propriétaire de cette classe
        Relationship otherRel = otherRelationSystem.GetRelationshipWith(_character);

        return iKnowHim && otherRel != null && otherRel.HasMet;
    }

    // --- Méthodes de statut ---

    public void SetAsMet() => _hasMet = true;
    public void SetAsNotMet() => _hasMet = false;
    public void ToggleMetStatus() => _hasMet = !_hasMet;

    // --- Évolution de la valeur ---

    public void IncreaseRelationValue(int amount)
    {
        if (amount < 0) amount = -amount;
        RelationValue += amount;
    }

    public void DecreaseRelationValue(int amount)
    {
        if (amount < 0) amount = -amount;
        RelationValue -= amount;
    }

    public void SetRelationshipType(RelationshipType newType)
    {
        _relationshipType = newType;
    }

    private void UpdateRelationshipType()
    {
        // On ne rétrograde pas automatiquement un Lover ou Soulmate via le score simple
        if (_relationshipType == RelationshipType.Lover || _relationshipType == RelationshipType.Soulmate)
            return;

        if (_relationValue <= -10)
        {
            _relationshipType = RelationshipType.Enemy;
        }
        else if (_relationValue >= 40)
        {
            _relationshipType = RelationshipType.Friend;
        }
        else if (_relationValue >= 10)
        {
            _relationshipType = RelationshipType.Acquaintance;
        }
        else
        {
            _relationshipType = RelationshipType.Stranger;
        }
    }
}
