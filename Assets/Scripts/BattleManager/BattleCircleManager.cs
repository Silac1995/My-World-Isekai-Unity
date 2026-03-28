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
    [SerializeField] private Material _partyMaterial;
    [SerializeField] private Material _enemyMaterial;

    private readonly Dictionary<Character, BattleGroundCircle> _activeCircles = new();
    private BattleManager _cachedBattleManager;
    private bool _isSubscribed;

    #region Lifecycle

    protected override void OnEnable()
    {
        base.OnEnable();
        if (_isSubscribed) return;
        if (_character == null) return;

        if (_character.CharacterCombat != null)
        {
            _character.CharacterCombat.OnBattleJoined += HandleBattleJoined;
            _character.CharacterCombat.OnBattleLeft += HandleBattleLeft;
        }

        if (_character.CharacterParty != null)
        {
            _character.CharacterParty.OnJoinedParty += HandlePartyJoined;
            _character.CharacterParty.OnLeftParty += HandlePartyLeft;
            _character.CharacterParty.OnPartyRosterChanged += HandlePartyRosterChanged;
        }

        _isSubscribed = true;
    }

    protected override void OnDisable()
    {
        if (_isSubscribed && _character != null)
        {
            if (_character.CharacterCombat != null)
            {
                _character.CharacterCombat.OnBattleJoined -= HandleBattleJoined;
                _character.CharacterCombat.OnBattleLeft -= HandleBattleLeft;
            }

            if (_character.CharacterParty != null)
            {
                _character.CharacterParty.OnJoinedParty -= HandlePartyJoined;
                _character.CharacterParty.OnLeftParty -= HandlePartyLeft;
                _character.CharacterParty.OnPartyRosterChanged -= HandlePartyRosterChanged;
            }

            _isSubscribed = false;
        }
        CleanupAll();
        base.OnDisable();
    }

    #endregion

    #region Battle Events

    private void HandleBattleJoined(BattleManager manager)
    {
        if (!ShouldManageCircles()) return;

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
        if (!ShouldManageCircles()) return;

        if (_cachedBattleManager != null)
        {
            _cachedBattleManager.OnParticipantAdded -= HandleParticipantAdded;
        }
        _cachedBattleManager = null;

        if (_character.IsInParty())
        {
            // Keep party member circles, remove everyone else (self + enemies + non-party allies).
            // Then re-initialize kept circles with the party material (they had battle colors).
            var partyId = _character.CharacterParty.PartyData.PartyId;
            var toRemove = new List<Character>();

            foreach (var kvp in _activeCircles)
            {
                Character c = kvp.Key;
                bool isPartyMember = c != null && c != _character
                    && c.IsInParty()
                    && c.CharacterParty.PartyData.PartyId == partyId;

                if (isPartyMember)
                {
                    // Swap material back to party green
                    kvp.Value.Initialize(_partyMaterial);
                }
                else
                {
                    toRemove.Add(c);
                }
            }

            foreach (var c in toRemove)
                RemoveCircle(c);
        }
        else
        {
            CleanupAll();
        }
    }

    private void HandleParticipantAdded(Character newParticipant)
    {
        if (!ShouldManageCircles() || _cachedBattleManager == null) return;
        if (_activeCircles.ContainsKey(newParticipant)) return;

        BattleTeam localTeam = _cachedBattleManager.BattleTeams.FirstOrDefault(t => t.IsAlly(_character));
        if (localTeam == null) return;

        SpawnCircleFor(newParticipant, localTeam.IsAlly(newParticipant));
    }

    #endregion

    #region Party Events

    private void HandlePartyJoined(PartyData data)
    {
        if (!ShouldManageCircles()) return;
        // Don't show party circles while in battle — battle circles take priority
        if (_cachedBattleManager != null) return;
        RefreshPartyCircles();
    }

    private void HandlePartyLeft()
    {
        if (!ShouldManageCircles()) return;
        if (_cachedBattleManager != null) return;
        // No longer in a party — remove all party circles
        CleanupAll();
    }

    private void HandlePartyRosterChanged()
    {
        if (!ShouldManageCircles()) return;
        if (_cachedBattleManager != null) return;
        RefreshPartyCircles();
    }

    /// <summary>
    /// Spawns green circles on all party members (including self). Called outside of combat.
    /// Cleans up any stale circles first, then spawns fresh ones.
    /// </summary>
    private void RefreshPartyCircles()
    {
        CleanupAll();

        if (!_character.IsInParty()) return;

        PartyData party = _character.CharacterParty.PartyData;
        foreach (string memberId in party.MemberIds)
        {
            if (memberId == _character.CharacterId) continue; // never on self
            Character member = Character.FindByUUID(memberId);
            if (member == null) continue;
            SpawnCircleFor(member, true);
        }
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
        Material material = PickMaterial(target, isAlly);
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

    /// <summary>
    /// True only for the local human player's character.
    /// IsLocalPlayer is set by NetworkObject.SpawnAsPlayerObject() — NPCs and remote
    /// players' characters always return false.
    /// </summary>
    private bool ShouldManageCircles()
    {
        return _character != null && _character.IsSpawned && _character.IsLocalPlayer;
    }

    /// <summary>
    /// Party member (green) > Ally (blue) > Enemy (red).
    /// A character is a "party member" if they share the same party as the local player.
    /// The local player themselves also gets the party color when in a party.
    /// </summary>
    private Material PickMaterial(Character target, bool isAlly)
    {
        if (!isAlly)
            return _enemyMaterial;

        // Check if both the local player and the target share a party
        if (_partyMaterial != null
            && _character.IsInParty()
            && target.IsInParty()
            && _character.CharacterParty.PartyData.PartyId == target.CharacterParty.PartyData.PartyId)
        {
            return _partyMaterial;
        }

        return _allyMaterial;
    }

    private void RemoveCircle(Character target)
    {
        if (!_activeCircles.TryGetValue(target, out BattleGroundCircle circle)) return;

        if (target != null)
        {
            target.OnIncapacitated -= HandleCharacterIncapacitated;
            target.OnWakeUp -= HandleCharacterWakeUp;
        }
        if (circle != null)
            circle.Cleanup();

        _activeCircles.Remove(target);
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
