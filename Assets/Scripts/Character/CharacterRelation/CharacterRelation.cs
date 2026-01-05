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

    // Ajoute une nouvelle relation (Bilatéral : ils se connaissent)
    public Relationship AddRelationship(Character otherCharacter)
    {
        // 1. On vérifie si on connaît déjà l'autre
        Relationship existing = GetRelationshipWith(otherCharacter);
        if (existing != null) return existing;

        // 2. On l'ajoute à NOTRE liste (notre perception de lui)
        Relationship newRel = new Relationship(Character, otherCharacter);
        _relationships.Add(newRel);

        Debug.Log($"<color=cyan>[Relation]</color> {_character.CharacterName} a rencontré {otherCharacter.CharacterName}");

        // 3. RÉCIPROCITÉ : On s'assure que l'autre nous connaît aussi
        var targetRelationSystem = otherCharacter.GetComponentInChildren<CharacterRelation>();
        if (targetRelationSystem != null)
        {
            // Cette ligne va appeler AddRelationship chez lui. 
            // Sa propre vérification "existing != null" arrêtera la récursion.
            targetRelationSystem.AddRelationship(_character);
        }

        return newRel;
    }

    // Met à jour la valeur (Unilatéral : SEUL mon avis sur l'autre change)
    public void UpdateRelation(Character target, int amount)
    {
        Relationship rel = GetRelationshipWith(target);

        if (rel == null)
        {
            rel = AddRelationship(target);
        }

        // Ici, on ne modifie QUE notre propre sentiment
        if (amount >= 0)
            rel.IncreaseRelationValue(amount);
        else
            rel.DecreaseRelationValue(amount);

        Debug.Log($"<color=white>[Sentiment]</color> L'avis de {_character.CharacterName} sur {target.CharacterName} est maintenant de {rel.RelationValue}");
    }
}