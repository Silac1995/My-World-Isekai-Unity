using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Local-player orchestrator for battle ground circle indicators.
/// Extends CharacterSystem — lives on a dedicated child GameObject of the Character prefab.
/// Only activates for human player characters that are the local owner.
/// NPCs never activate this (they have no CharacterGameController).
/// </summary>
public class BattleCircleManager : CharacterSystem
{
    [Header("Battle Circle Settings")]
    [SerializeField] private GameObject _battleCirclePrefab;
    [SerializeField] private Material _allyMaterial;
    [SerializeField] private Material _enemyMaterial;

    private readonly Dictionary<Character, BattleGroundCircle> _activeCircles = new();
    private BattleManager _cachedBattleManager;

    #region Lifecycle

    protected override void OnEnable()
    {
        base.OnEnable();
        // Only subscribe for human player characters (Controller is null on NPCs)
        if (_character != null && _character.CharacterCombat != null && _character.Controller != null)
        {
            _character.CharacterCombat.OnBattleJoined += HandleBattleJoined;
            _character.CharacterCombat.OnBattleLeft += HandleBattleLeft;
        }
    }

    protected override void OnDisable()
    {
        if (_character != null && _character.CharacterCombat != null && _character.Controller != null)
        {
            _character.CharacterCombat.OnBattleJoined -= HandleBattleJoined;
            _character.CharacterCombat.OnBattleLeft -= HandleBattleLeft;
        }
        CleanupAll();
        base.OnDisable();
    }

    #endregion

    #region Battle Events

    private void HandleBattleJoined(BattleManager manager)
    {
        if (!IsOwner) return;

        // Defensive: clear any leftover circles from a prior battle (rapid re-engagement)
        CleanupAll();

        _cachedBattleManager = manager;

        // Resolve local player's team
        BattleTeam localTeam = manager.BattleTeams.FirstOrDefault(t => t.IsAlly(_character));
        if (localTeam == null)
        {
            Debug.LogError($"<color=red>[BattleCircleManager]</color> Could not resolve local team for {_character.CharacterName}");
            return;
        }

        // Spawn circles for all characters in both teams
        foreach (BattleTeam team in manager.BattleTeams)
        {
            foreach (Character character in team.CharacterList)
            {
                SpawnCircleFor(character, localTeam.IsAlly(character));
            }
        }

        // Subscribe to mid-battle joiners
        manager.OnParticipantAdded += HandleParticipantAdded;
    }

    private void HandleBattleLeft()
    {
        if (!IsOwner) return;

        // Unsubscribe from cached BattleManager (null-safe — BattleManager may be destroyed)
        if (_cachedBattleManager != null)
        {
            _cachedBattleManager.OnParticipantAdded -= HandleParticipantAdded;
        }

        CleanupAll();
        _cachedBattleManager = null;
    }

    private void HandleParticipantAdded(Character newParticipant)
    {
        if (!IsOwner || _cachedBattleManager == null) return;
        if (_activeCircles.ContainsKey(newParticipant)) return;

        BattleTeam localTeam = _cachedBattleManager.BattleTeams.FirstOrDefault(t => t.IsAlly(_character));
        if (localTeam == null) return;

        SpawnCircleFor(newParticipant, localTeam.IsAlly(newParticipant));
    }

    #endregion

    #region Per-Character Events

    private void HandleCharacterIncapacitated(Character character)
    {
        if (!IsOwner) return;
        if (_activeCircles.TryGetValue(character, out BattleGroundCircle circle))
        {
            circle.Dim();
        }
    }

    private void HandleCharacterWakeUp(Character character)
    {
        if (!IsOwner) return;
        if (_activeCircles.TryGetValue(character, out BattleGroundCircle circle))
        {
            circle.Restore();
        }
    }

    #endregion

    #region Circle Management

    private void SpawnCircleFor(Character target, bool isAlly)
    {
        if (target == null || _battleCirclePrefab == null) return;
        if (_activeCircles.ContainsKey(target)) return;

        // If another BattleCircleManager already spawned a circle on this character
        // (can happen in testing when two player-characters are in the same scene),
        // skip — first spawner wins and owns the cleanup.
        if (target.GetComponentInChildren<BattleGroundCircle>() != null) return;

        // Parent to character's root transform (not visual transform — avoids sprite flip issues).
        // World rotation Euler(-90,0,0) lays the quad flat in the XZ plane regardless of parent orientation.
        // Small Y offset prevents z-fighting with the ground mesh.
        GameObject circleGO = Instantiate(_battleCirclePrefab, target.transform);
        circleGO.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        circleGO.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        // Scale the circle so it's 10 world units wider than the character's ground footprint.
        float charRadius = GetCharacterGroundRadius(target);
        float diameter   = charRadius * 2f + 10f;
        circleGO.transform.localScale = new Vector3(diameter, diameter, 1f);

        BattleGroundCircle circle = circleGO.GetComponent<BattleGroundCircle>();
        Material material = isAlly ? _allyMaterial : _enemyMaterial;
        circle.Initialize(material);

        _activeCircles[target] = circle;

        // Subscribe to incapacitated/wakeup for this specific character
        target.OnIncapacitated += HandleCharacterIncapacitated;
        target.OnWakeUp += HandleCharacterWakeUp;

        // If character is already incapacitated at spawn time, dim immediately
        if (target.IsIncapacitated)
        {
            circle.Dim();
        }
    }

    /// <summary>
    /// Returns the character's approximate ground radius (half their widest XZ extent).
    /// Checks CapsuleCollider first, then any Collider, then Renderer bounds as fallback.
    /// </summary>
    private float GetCharacterGroundRadius(Character target)
    {
        CapsuleCollider capsule = target.GetComponentInChildren<CapsuleCollider>();
        if (capsule != null)
            return Mathf.Max(capsule.radius, capsule.bounds.extents.x, capsule.bounds.extents.z);

        Collider col = target.GetComponentInChildren<Collider>();
        if (col != null)
            return Mathf.Max(col.bounds.extents.x, col.bounds.extents.z);

        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null)
            return Mathf.Max(rend.bounds.extents.x, rend.bounds.extents.z);

        return 0.5f; // sensible default if no bounds found
    }

    private void CleanupAll()
    {
        foreach (var kvp in _activeCircles)
        {
            if (kvp.Key != null)
            {
                kvp.Key.OnIncapacitated -= HandleCharacterIncapacitated;
                kvp.Key.OnWakeUp -= HandleCharacterWakeUp;
            }

            if (kvp.Value != null)
            {
                kvp.Value.Cleanup();
            }
        }

        _activeCircles.Clear();
    }

    #endregion
}
