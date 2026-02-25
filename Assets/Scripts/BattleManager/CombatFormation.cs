using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gère les positions (slots) d'une équipe autour d'un point focal (généralement la cible ennemie).
/// Assure que les attaquants s'espacent correctement sur plusieurs rangées (mêlée, distance).
/// </summary>
public class CombatFormation
{
    private class SlotPosition
    {
        public Vector3 LocalOffset;
        public Vector3 Jitter; // Bruit fixe par slot, calculé une seule fois
        public Character Occupant;
    }

    private Dictionary<Character, SlotPosition> _assignedSlots = new Dictionary<Character, SlotPosition>();
    
    // Configuration des rangées (Rows)
    private const float ROW_0_RADIUS = 5.0f; // Mêlée
    private const float ROW_1_RADIUS = 8.0f; // Allonge / Mid-range
    private const float ROW_2_RADIUS = 11.0f; // Distance / Caster
    
    // Nombre de slots par rangée
    private const int SLOTS_ROW_0 = 4; // 4 places max en mêlée pure (N, S, E, O)
    private const int SLOTS_ROW_1 = 8;
    private const int SLOTS_ROW_2 = 12;

    private List<SlotPosition> _allAvailableSlots;

    public CombatFormation()
    {
        InitializeSlots();
    }

    private void InitializeSlots()
    {
        _allAvailableSlots = new List<SlotPosition>();

        // Création de la Row 0 (Mêlée)
        CreateRing(ROW_0_RADIUS, SLOTS_ROW_0, 0f);
        // Création de la Row 1 (Mid-range)
        CreateRing(ROW_1_RADIUS, SLOTS_ROW_1, 45f); // Décalé de 45° pour voir entre les persos de mêlée
        // Création de la Row 2 (Distance)
        CreateRing(ROW_2_RADIUS, SLOTS_ROW_2, 0f);
    }

    private void CreateRing(float radius, int count, float angleOffsetDeg)
    {
        float angleStep = 360f / count;
        for (int i = 0; i < count; i++)
        {
            float angleRad = (i * angleStep + angleOffsetDeg) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angleRad) * radius, 0, Mathf.Sin(angleRad) * radius);
            // Jitter fixe par slot, calculé une seule fois
            Vector3 jitter = new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
            _allAvailableSlots.Add(new SlotPosition { LocalOffset = offset, Jitter = jitter, Occupant = null });
        }
    }

    /// <summary>
    /// Assigne une place au personnage de manière déterministe ou cherche la plus proche disponible.
    /// </summary>
    public void AddCharacter(Character character)
    {
        if (_assignedSlots.ContainsKey(character)) return; // Déjà placé

        // TODO: Prendre en compte la stat preferredRange du personnage pour choisir la Ring (Mêlée vs Distance)
        // Pour l'instant, on remplit du centre vers l'extérieur
        
        // On essaie d'utiliser l'ID pour un placement "préféré" pour que le perso vise toujours le même côté
        int preferredIndex = Mathf.Abs(character.GetInstanceID()) % _allAvailableSlots.Count;
        
        SlotPosition bestSlot = null;
        
        // 1. Cherche en partant du slot préféré
        for (int i = 0; i < _allAvailableSlots.Count; i++)
        {
            int checkIndex = (preferredIndex + i) % _allAvailableSlots.Count;
            if (_allAvailableSlots[checkIndex].Occupant == null)
            {
                bestSlot = _allAvailableSlots[checkIndex];
                break;
            }
        }

        // Si tout est plein (combat épique), on ne le rajoute pas stricto sensu à un slot précis (il devra s'entasser)
        if (bestSlot != null)
        {
            bestSlot.Occupant = character;
            _assignedSlots.Add(character, bestSlot);
        }
        else
        {
            Debug.LogWarning($"<color=yellow>[Formation]</color> Plus de slots disponibles pour {character.CharacterName} !");
        }
    }

    public void RemoveCharacter(Character character)
    {
        if (_assignedSlots.TryGetValue(character, out SlotPosition slot))
        {
            slot.Occupant = null;
            _assignedSlots.Remove(character);
        }
    }

    /// <summary>
    /// Calcule la coordonnée monde cible pour le personnage autour du point focal donné.
    /// </summary>
    public Vector3 GetWorldPosition(Character character, Vector3 focalPoint)
    {
        if (_assignedSlots.TryGetValue(character, out SlotPosition slot))
        {
            return focalPoint + slot.LocalOffset + slot.Jitter;
        }

        // Fallback si pas de slot : position stable autour de ROW_2 basée sur l'ID
        // Évite que les NPCs marchent directement sur la cible
        float angle = (Mathf.Abs(character.GetInstanceID()) % 360) * Mathf.Deg2Rad;
        Vector3 overflowOffset = new Vector3(Mathf.Cos(angle) * ROW_2_RADIUS, 0, Mathf.Sin(angle) * ROW_2_RADIUS);
        return focalPoint + overflowOffset;
    }

    public bool HasCharacter(Character character)
    {
        return _assignedSlots.ContainsKey(character);
    }
}
