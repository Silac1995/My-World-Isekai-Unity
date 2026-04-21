using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Marks a Character as an animal and carries its tameability state.
/// Implements IInteractionProvider (exposes the "Tame" option) and
/// ICharacterSaveData&lt;AnimalSaveData&gt; (persists tamed state through hibernation).
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
        // Implemented in Task 6.
        return new List<InteractionOption>();
    }

    // ── ICharacterSaveData<AnimalSaveData> (Task 5) ─────────────────────
    public string SaveKey => "CharacterAnimal";
    public int LoadPriority => 40;

    public AnimalSaveData Serialize() => new AnimalSaveData();
    public void Deserialize(AnimalSaveData data) { }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
