using UnityEngine;

[System.Serializable]
public class Relationship
{
    [SerializeField] private Character _character; // Le propriétaire (celui qui a ce sentiment)
    [SerializeField] private Character _relatedCharacter; // La cible du sentiment
    [SerializeField] private int _relationValue;
    [SerializeField] private RelationshipType _relationshipType = RelationshipType.Stranger;
    [SerializeField] private bool _hasMet = false;
    public bool IsNewlyAdded { get; set; } = false;

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
            int oldValue = _relationValue;
            _relationValue = Mathf.Clamp(value, -100, 100);
            
            if (oldValue != _relationValue)
            {
                Debug.Log($"[Relation Debug] {_character.CharacterName} -> {_relatedCharacter.CharacterName} : {oldValue} -> {_relationValue}");
                UpdateRelationshipType();
            }
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

    // --- LOGIQUE SOCIALE CENTRALISÉE ---

    /// <summary>
    /// Calcule la chance (0-1) que ce personnage veuille initier une interaction.
    /// </summary>
    public float GetInteractionChance()
    {
        // On convertit le score (-100 à 100) en probabilité
        // Base de 2% pour les inconnus, augmentant avec la relation
        float chance = Mathf.Clamp(0.02f + (_relationValue / 80f), 0.02f, 0.5f);
        if (_relationValue <= -10) chance = 0.01f; // Très rare d'initier avec un ennemi sans raison
        return chance;
    }

    /// <summary>
    /// Calcule la chance (0-1) que ce personnage accepte de répondre pendant un échange.
    /// </summary>
    public float GetResponseChance()
    {
        // Chance de base : 50%
        // +1% par point de relation (max 100% à 50+ relation)
        // -1% par point de relation négatif (min 0% à -50- relation)
        return Mathf.Clamp(0.5f + (_relationValue / 100f), 0.1f, 0.95f);
    }

    /// <summary>
    /// Calcule la chance (0-1) que l'interaction soit positive (Talk) plutôt que négative (Insult).
    /// </summary>
    public float GetFavorableToneChance()
    {
        float baseChance = 0.6f; // Stranger / Acquaintance

        if (_relationshipType == RelationshipType.Enemy) 
            baseChance = 0.35f;
        else if (_relationshipType == RelationshipType.Friend || _relationshipType == RelationshipType.Lover || _relationshipType == RelationshipType.Soulmate)
            baseChance = 0.85f;

        // Formule : Base + Points de relation (chaque point = 1%)
        float finalChance = baseChance + (_relationValue / 100f);

        // --- BIAIS DE PERSONNALITÉ ---
        if (_character.CharacterProfile != null && _relatedCharacter != null && _relatedCharacter.CharacterProfile != null)
        {
            int compatibility = _character.CharacterProfile.GetCompatibilityWith(_relatedCharacter.CharacterProfile);
            if (compatibility > 0) finalChance += 0.2f;      // Compatible : +20% chance de "Talk"
            else if (compatibility < 0) finalChance -= 0.15f; // Incompatible : -20% chance de "Talk"
        }

        // Contrainte User : 10% minimum, 90% maximum
        return Mathf.Clamp(finalChance, 0.1f, 0.9f);
    }

    private void UpdateRelationshipType()
    {
        RelationshipType lastType = _relationshipType;

        // On ne rétrograde pas automatiquement un Lover ou Soulmate via le score simple
        if (_relationshipType == RelationshipType.Lover || _relationshipType == RelationshipType.Soulmate)
            return;

        if (_relationValue <= -45)
        {
            _relationshipType = RelationshipType.Enemy;
        }
        else if (_relationValue >= 20)
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

        if (lastType != _relationshipType)
        {
            Debug.Log($"<color=orange>[Relation Status]</color> {_character.CharacterName} voit maintenant {_relatedCharacter.CharacterName} comme un **{_relationshipType}**");
        }
    }
}
