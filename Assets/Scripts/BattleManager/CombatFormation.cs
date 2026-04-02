using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Calculates organic combat positions based on character role (melee/ranged)
/// relative to the engagement's opponent center and anchor point.
/// No fixed slots — positions evolve dynamically.
/// </summary>
public class CombatFormation
{
    // Scale: 11 units = 1.67m. 1 unit ≈ 15cm.
    private const float MELEE_PREFERRED_DISTANCE = 20f;   // ~3m
    private const float MELEE_SPACING = 14f;              // ~2.1m between melee allies
    private const float RANGED_MIN_DISTANCE = 45f;        // ~6.8m
    private const float RANGED_SPACING = 12f;             // ~1.8m between ranged allies
    private const float Z_SPREAD = 7f;                    // ~1m depth stagger

    private Dictionary<Character, Vector3> _lastAssignedPositions;

    public CombatFormation()
    {
        _lastAssignedPositions = new Dictionary<Character, Vector3>();
    }

    /// <summary>
    /// Calculates the ideal position for a character within their engagement.
    /// </summary>
    public Vector3 GetOrganicPosition(Character character, IReadOnlyList<Character> allies,
        Vector3 opponentCenter, Vector3 anchorPoint, float teamSideSign)
    {
        bool isRanged = IsRangedCharacter(character);

        // Find this character's index among same-role allies for spacing
        int roleIndex = 0;
        int roleCount = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] == null || !allies[i].IsAlive()) continue;
            bool allyIsRanged = IsRangedCharacter(allies[i]);
            if (allyIsRanged == isRanged)
            {
                if (allies[i] == character) roleIndex = roleCount;
                roleCount++;
            }
        }

        float distance = isRanged ? RANGED_MIN_DISTANCE : MELEE_PREFERRED_DISTANCE;
        float spacing = isRanged ? RANGED_SPACING : MELEE_SPACING;

        // Position on our team's side of the engagement
        Vector3 dirFromOpponent = (anchorPoint - opponentCenter).normalized;
        if (dirFromOpponent.sqrMagnitude < 0.01f)
            dirFromOpponent = new Vector3(teamSideSign, 0, 0);

        Vector3 basePosition = opponentCenter + dirFromOpponent * distance;

        // Spread allies along Z axis
        float zOffset = 0f;
        if (roleCount > 1)
        {
            float totalSpread = (roleCount - 1) * spacing;
            zOffset = -totalSpread / 2f + roleIndex * spacing;
        }

        basePosition.z += zOffset;

        // Deterministic jitter based on instance ID — stable across frames
        float jitterSeed = character.GetInstanceID() * 0.1f;
        basePosition.x += Mathf.Sin(jitterSeed) * 3f;  // ~45cm jitter
        basePosition.z += Mathf.Cos(jitterSeed) * 2f;  // ~30cm jitter

        _lastAssignedPositions[character] = basePosition;
        return basePosition;
    }

    public bool TryGetLastPosition(Character character, out Vector3 position)
    {
        return _lastAssignedPositions.TryGetValue(character, out position);
    }

    public void RemoveCharacter(Character character)
    {
        _lastAssignedPositions.Remove(character);
    }

    public void Clear()
    {
        _lastAssignedPositions.Clear();
    }

    private bool IsRangedCharacter(Character character)
    {
        if (character?.CharacterCombat?.CurrentCombatStyleExpertise?.Style == null)
            return false;
        return character.CharacterCombat.CurrentCombatStyleExpertise.Style is RangedCombatStyleSO;
    }
}
