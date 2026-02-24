using System.Collections.Generic;
using UnityEngine;

public class CharacterRelation : MonoBehaviour
{
    [SerializeField] private Character _character;
    [SerializeField] private List<Relationship> _relationships = new List<Relationship>();

    public Character Character => _character;
    public List<Relationship> Relationships => _relationships;

    public Relationship GetRelationshipWith(Character otherCharacter)
    {
        return _relationships.Find(r => r.RelatedCharacter == otherCharacter);
    }

    // --- CHECKERS ---

    public bool IsFriend(Character other)
    {
        Relationship rel = GetRelationshipWith(other);
        if (rel == null) return false;
        
        return rel.RelationType == RelationshipType.Friend || 
               rel.RelationType == RelationshipType.Lover || 
               rel.RelationType == RelationshipType.Soulmate;
    }

    public bool IsEnemy(Character other)
    {
        Relationship rel = GetRelationshipWith(other);
        if (rel == null) return false;
        
        return rel.RelationType == RelationshipType.Enemy;
    }

    // Ajoute une nouvelle relation (Bilatéral : ils se connaissent)
    public Relationship AddRelationship(Character otherCharacter)
    {
        Relationship existing = GetRelationshipWith(otherCharacter);
        if (existing != null) return existing;

        Relationship newRel = new Relationship(Character, otherCharacter);
        _relationships.Add(newRel);

        Debug.Log($"<color=cyan>[Relation]</color> {_character.CharacterName} a rencontré {otherCharacter.CharacterName}");

        var targetRelationSystem = otherCharacter.GetComponentInChildren<CharacterRelation>();
        if (targetRelationSystem != null)
        {
            targetRelationSystem.AddRelationship(_character);
        }

        return newRel;
    }

    public void UpdateRelation(Character target, int amount)
    {
        Relationship rel = GetRelationshipWith(target);

        if (rel == null)
        {
            rel = AddRelationship(target);
        }

        // --- MODIFICATEURS DE PERSONNALITÉ ---
        float finalAmount = amount;
        if (_character.CharacterProfile != null && target.CharacterProfile != null)
        {
            int compatibility = _character.CharacterProfile.GetCompatibilityWith(target.CharacterProfile);
            
            if (amount > 0) // Gain
            {
                if (compatibility > 0) finalAmount *= 1.5f;      // Compatible : +50% gain
                else if (compatibility < 0) finalAmount *= 0.5f; // Incompatible : -50% gain
            }
            else if (amount < 0) // Perte
            {
                if (compatibility > 0) finalAmount *= 0.5f;      // Compatible : -50% perte (perd moins)
                else if (compatibility < 0) finalAmount *= 1.5f; // Incompatible : +50% perte (perd plus)
            }
        }

        int roundedAmount = Mathf.RoundToInt(finalAmount);

        if (roundedAmount >= 0)
        {
            rel.IncreaseRelationValue(roundedAmount);
        }
        else
        {
            rel.DecreaseRelationValue(-roundedAmount);
        }

        Debug.Log($"<color=white>[Sentiment]</color> L'avis de {_character.CharacterName} sur {target.CharacterName} est maintenant de {rel.RelationValue} ({rel.RelationType}) [Modif: {amount} -> {roundedAmount}]");
    }
}
