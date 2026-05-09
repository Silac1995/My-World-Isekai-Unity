using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Marks a Character as an animal and carries its tameability state.
/// Implements IInteractionProvider (exposes the "Tame" option) and
/// ICharacterSaveData<AnimalSaveData> (persists tamed state through hibernation).
/// See wiki/systems/character-animal.md for the full architecture notes.
/// </summary>
public class CharacterAnimal : CharacterSystem,
    IInteractionProvider,
    ICharacterSaveData<AnimalSaveData>
{
    // ── Network State ───────────────────────────────────────────────────
    private NetworkVariable<bool>  _isTameable     = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<float> _tameDifficulty = new(0.5f,  NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool>  _isTamed        = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<FixedString64Bytes> _ownerProfileId =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Public Read-Only API ────────────────────────────────────────────
    public bool   IsTameable     => _isTameable.Value;
    public float  TameDifficulty => _tameDifficulty.Value;
    public bool   IsTamed        => _isTamed.Value;
    public string OwnerProfileId => _ownerProfileId.Value.ToString();

    // ── Random seam (overridable for tests) ─────────────────────────────
    private IRandomProvider _random = new UnityRandomProvider();
    public void SetRandomProvider(IRandomProvider random) => _random = random ?? new UnityRandomProvider();

    // ── Lifecycle ───────────────────────────────────────────────────────
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        // Seed archetype-derived fields on the server.
        var archetype = _character != null ? _character.Archetype : null;
        if (archetype == null)
        {
            Debug.LogWarning($"[CharacterAnimal] No archetype on '{_character?.CharacterName ?? gameObject.name}' — leaving defaults.");
            return;
        }

        _isTameable.Value     = archetype.IsTameable;
        _tameDifficulty.Value = archetype.TameDifficulty;
    }

    // ── IInteractionProvider (Task 6) ───────────────────────────────────
    public List<InteractionOption> GetInteractionOptions(Character interactor)
    {
        var options = new List<InteractionOption>();

        if (interactor == null || _character == null) return options;
        if (!IsTameable || IsTamed) return options;
        if (interactor == _character) return options;

        // Capture locals for the closure.
        Character interactorRef = interactor;

        options.Add(new InteractionOption("Tame", () =>
        {
            // Queue the action on the interactor. CharacterTameAction's OnApplyEffect
            // routes to the target's server-side tame RPC (Task 8).
            if (interactorRef.CharacterActions == null)
            {
                Debug.LogWarning($"[CharacterAnimal] '{interactorRef.CharacterName}' has no CharacterActions — cannot tame.");
                return;
            }

            interactorRef.CharacterActions.ExecuteAction(new CharacterTameAction(interactorRef, _character));
        }));

        return options;
    }

    // ── ICharacterSaveData<AnimalSaveData> (Task 5) ─────────────────────
    public string SaveKey => "CharacterAnimal";
    public int LoadPriority => 40;

    public AnimalSaveData Serialize()
    {
        return new AnimalSaveData
        {
            IsTamed        = _isTamed.Value,
            OwnerProfileId = _ownerProfileId.Value.ToString()
        };
    }

    public void Deserialize(AnimalSaveData data)
    {
        if (data == null)
        {
            Debug.LogWarning($"[CharacterAnimal] Deserialize called with null data on '{_character?.CharacterName ?? gameObject.name}'.");
            return;
        }

        if (!IsServer)
        {
            // NetworkVariables are server-write only; a client-side Deserialize call is a no-op
            // (NVs will sync from server naturally). This branch is likely unreachable under
            // the current CharacterDataCoordinator flow — verify during manual testing and
            // remove if confirmed dead.
            Debug.Log($"[CharacterAnimal] Deserialize on non-server for '{_character?.CharacterName}' — skipping NV writes.");
            return;
        }

        try
        {
            _isTamed.Value = data.IsTamed;
            _ownerProfileId.Value = string.IsNullOrEmpty(data.OwnerProfileId)
                ? default
                : new FixedString64Bytes(data.OwnerProfileId);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError($"[CharacterAnimal] Failed to restore save data on '{_character?.CharacterName ?? gameObject.name}' — " +
                           $"IsTamed={data.IsTamed}, OwnerProfileId='{data.OwnerProfileId}'");
        }
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    // ── Server-Authoritative Tame Flow (called from CharacterTameAction) ──

    [Rpc(SendTo.Server)]
    public void RequestTameServerRpc(NetworkObjectReference interactorRef)
    {
        TryTameOnServer(interactorRef);
    }

    /// <summary>
    /// Server-side gate + roll. Called directly when the tame action runs on
    /// the server, or via RequestTameServerRpc when it runs on a client.
    /// </summary>
    public void TryTameOnServer(NetworkObjectReference interactorRef)
    {
        if (!IsServer)
        {
            Debug.LogError($"[CharacterAnimal] TryTameOnServer called on non-server — ignored.");
            return;
        }

        if (!interactorRef.TryGet(out NetworkObject interactorNetObj))
        {
            Debug.LogWarning($"[CharacterAnimal] Server could not resolve interactor NetworkObject — rejecting tame.");
            return;
        }

        Character interactor = interactorNetObj.GetComponent<Character>();
        if (interactor == null || _character == null)
        {
            Debug.LogWarning($"[CharacterAnimal] Missing Character on interactor or target — rejecting tame.");
            return;
        }

        // Re-validate server-side (defends against stale client state).
        if (!IsTameable)
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is not tameable — rejecting.");
            return;
        }
        if (IsTamed)
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is already tamed — rejecting.");
            return;
        }
        if (_character.IsPlayer())
        {
            Debug.Log($"[CharacterAnimal] '{_character.CharacterName}' is currently player-driven — tame blocked.");
            return;
        }

        float range = _character.Archetype != null ? _character.Archetype.DefaultInteractionRange : 3.5f;
        float dist = Vector3.Distance(interactor.transform.position, _character.transform.position);
        if (dist > range)
        {
            Debug.Log($"[CharacterAnimal] '{interactor.CharacterName}' too far to tame '{_character.CharacterName}' (dist={dist:F2}, range={range:F2}).");
            return;
        }

        // Roll.
        bool success = _random.Value() > _tameDifficulty.Value;

        Debug.Log($"<color=cyan>[CharacterAnimal]</color> Roll for '{_character.CharacterName}' — " +
                  $"difficulty={_tameDifficulty.Value:F2}, success={success}.");

        if (success)
        {
            _isTamed.Value = true;

            string profileId = interactor.CharacterId;
            if (string.IsNullOrEmpty(profileId))
            {
                Debug.LogWarning($"[CharacterAnimal] Interactor '{interactor.CharacterName}' has empty CharacterId — " +
                                 "OwnerProfileId will be blank until identity resolves.");
                _ownerProfileId.Value = default;
            }
            else
            {
                if (profileId.Length > 63)
                {
                    Debug.LogWarning($"[CharacterAnimal] ProfileId '{profileId}' exceeds FixedString64Bytes capacity — truncating.");
                    profileId = profileId.Substring(0, 63);
                }
                _ownerProfileId.Value = new FixedString64Bytes(profileId);
            }
        }

        // Spawn the floating text locally on the server, then broadcast to non-server clients.
        // Matches CharacterActions.cs convention (SendTo.NotServer + explicit server-local call).
        SpawnTameResultText(success);
        ShowTameResultClientRpc(success);
    }

    [Rpc(SendTo.NotServer)]
    private void ShowTameResultClientRpc(bool success)
    {
        SpawnTameResultText(success);
    }

    private void SpawnTameResultText(bool success)
    {
        var spawner = _character != null ? _character.FloatingTextSpawner : null;
        if (spawner == null) return;

        if (success)
            spawner.SpawnText("Tamed!", Color.green);
        else
            spawner.SpawnText("Failed!", new Color(1f, 0.4f, 0.4f));
    }
}
