using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class CharacterAbilities : CharacterSystem, ICharacterSaveData<AbilitiesSaveData>
{
    public const int ACTIVE_SLOT_COUNT = 6;
    public const int PASSIVE_SLOT_COUNT = 4;

    [Header("Known Abilities")]
    [SerializeField] private List<PhysicalAbilityInstance> _knownPhysicalAbilities = new List<PhysicalAbilityInstance>();
    [SerializeField] private List<SpellInstance> _knownSpells = new List<SpellInstance>();
    [SerializeField] private List<PassiveAbilityInstance> _knownPassives = new List<PassiveAbilityInstance>();

    [Header("Equipped Slots")]
    [SerializeField] private AbilityInstance[] _activeSlots = new AbilityInstance[ACTIVE_SLOT_COUNT];
    [SerializeField] private PassiveAbilityInstance[] _passiveSlots = new PassiveAbilityInstance[PASSIVE_SLOT_COUNT];

    // Network sync for equipped ability slot IDs
    private NetworkList<NetworkAbilitySlotData> _networkAbilitySlots;

    // Events
    public event Action<AbilitySO> OnAbilityLearned;
    public event Action<int, AbilityInstance> OnActiveSlotChanged;
    public event Action<int, PassiveAbilityInstance> OnPassiveSlotChanged;

    protected override void Awake()
    {
        base.Awake();
        _networkAbilitySlots = new NetworkList<NetworkAbilitySlotData>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkAbilitySlots.OnListChanged += OnAbilitySlotsChanged;

        // Server: push initial slot state
        if (IsServer)
        {
            SyncAllSlotsToNetwork();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkAbilitySlots.OnListChanged -= OnAbilitySlotsChanged;
    }

    #region Learning

    public void LearnAbility(AbilitySO ability)
    {
        if (ability == null || KnowsAbility(ability)) return;

        switch (ability)
        {
            case PhysicalAbilitySO physical:
                _knownPhysicalAbilities.Add(new PhysicalAbilityInstance(physical, _character));
                break;
            case SpellSO spell:
                _knownSpells.Add(new SpellInstance(spell, _character));
                break;
            case PassiveAbilitySO passive:
                _knownPassives.Add(new PassiveAbilityInstance(passive, _character));
                break;
            default:
                Debug.LogWarning($"[CharacterAbilities] Unknown ability type: {ability.GetType().Name}");
                return;
        }

        Debug.Log($"[CharacterAbilities] {_character.CharacterName} learned ability: {ability.AbilityName}");
        OnAbilityLearned?.Invoke(ability);
    }

    public void UnlearnAbility(AbilitySO ability)
    {
        if (ability == null) return;

        // Remove from equipped slots first
        for (int i = 0; i < ACTIVE_SLOT_COUNT; i++)
        {
            if (_activeSlots[i]?.Data == ability)
                UnequipActiveSlot(i);
        }
        for (int i = 0; i < PASSIVE_SLOT_COUNT; i++)
        {
            if (_passiveSlots[i]?.Data == ability)
                UnequipPassiveSlot(i);
        }

        // Remove from known pools
        switch (ability)
        {
            case PhysicalAbilitySO:
                _knownPhysicalAbilities.RemoveAll(a => a.Data == ability);
                break;
            case SpellSO:
                _knownSpells.RemoveAll(a => a.Data == ability);
                break;
            case PassiveAbilitySO:
                _knownPassives.RemoveAll(a => a.Data == ability);
                break;
        }
    }

    public bool KnowsAbility(AbilitySO ability)
    {
        if (ability == null) return false;

        return ability switch
        {
            PhysicalAbilitySO => _knownPhysicalAbilities.Any(a => a.Data == ability),
            SpellSO => _knownSpells.Any(a => a.Data == ability),
            PassiveAbilitySO => _knownPassives.Any(a => a.Data == ability),
            _ => false
        };
    }

    #endregion

    #region Slot Management

    public void EquipToActiveSlot(int slotIndex, AbilityInstance ability)
    {
        if (slotIndex < 0 || slotIndex >= ACTIVE_SLOT_COUNT) return;
        if (ability is PassiveAbilityInstance) return; // Passives go in passive slots

        _activeSlots[slotIndex] = ability;
        UpdateNetworkSlot(0, slotIndex, ability?.AbilityId);
        OnActiveSlotChanged?.Invoke(slotIndex, ability);
    }

    public void UnequipActiveSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= ACTIVE_SLOT_COUNT) return;

        _activeSlots[slotIndex] = null;
        UpdateNetworkSlot(0, slotIndex, "");
        OnActiveSlotChanged?.Invoke(slotIndex, null);
    }

    public void EquipToPassiveSlot(int slotIndex, PassiveAbilityInstance passive)
    {
        if (slotIndex < 0 || slotIndex >= PASSIVE_SLOT_COUNT) return;

        _passiveSlots[slotIndex] = passive;
        UpdateNetworkSlot(1, slotIndex, passive?.AbilityId);
        OnPassiveSlotChanged?.Invoke(slotIndex, passive);
    }

    public void UnequipPassiveSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= PASSIVE_SLOT_COUNT) return;

        _passiveSlots[slotIndex] = null;
        UpdateNetworkSlot(1, slotIndex, "");
        OnPassiveSlotChanged?.Invoke(slotIndex, null);
    }

    public AbilityInstance GetActiveSlot(int index)
    {
        if (index < 0 || index >= ACTIVE_SLOT_COUNT) return null;
        return _activeSlots[index];
    }

    public PassiveAbilityInstance GetPassiveSlot(int index)
    {
        if (index < 0 || index >= PASSIVE_SLOT_COUNT) return null;
        return _passiveSlots[index];
    }

    #endregion

    #region Queries

    /// <summary>
    /// Returns all physical abilities that match the currently equipped weapon type.
    /// </summary>
    public List<PhysicalAbilityInstance> GetAvailablePhysicalAbilities()
    {
        WeaponType equippedType = GetEquippedWeaponType();
        return _knownPhysicalAbilities.Where(a => a.PhysicalData.RequiredWeaponType == equippedType).ToList();
    }

    public IReadOnlyList<SpellInstance> GetAvailableSpells() => _knownSpells;
    public IReadOnlyList<PassiveAbilityInstance> GetKnownPassives() => _knownPassives;

    public IEnumerable<AbilityInstance> AllKnownAbilities()
    {
        foreach (var a in _knownPhysicalAbilities) yield return a;
        foreach (var a in _knownSpells) yield return a;
        foreach (var a in _knownPassives) yield return a;
    }

    private WeaponType GetEquippedWeaponType()
    {
        var weapon = _character?.CharacterEquipment?.CurrentWeapon;
        if (weapon != null && weapon.ItemSO is WeaponSO weaponSO)
            return weaponSO.WeaponType;
        return WeaponType.Barehands;
    }

    #endregion

    #region Cooldown Ticking

    private void Update()
    {
        if (!IsServer) return;

        float dt = UnityEngine.Time.deltaTime;

        // Tick spell cooldowns
        foreach (var spell in _knownSpells)
            spell.TickCooldown(dt);

        // Tick passive internal cooldowns
        foreach (var passive in _knownPassives)
            passive.TickCooldown(dt);
    }

    #endregion

    #region Passive Trigger Dispatch

    /// <summary>
    /// Called when a combat event occurs. Evaluates all equipped passives and triggers matching reactions.
    /// Server-only: passive effects are server-authoritative.
    /// </summary>
    /// <param name="condition">The type of event that occurred.</param>
    /// <param name="source">The character who caused the event.</param>
    /// <param name="target">The character affected by the event.</param>
    public void OnPassiveTriggerEvent(PassiveTriggerCondition condition, Character source, Character target)
    {
        if (!IsServer) return;

        for (int i = 0; i < PASSIVE_SLOT_COUNT; i++)
        {
            var passive = _passiveSlots[i];
            if (passive == null) continue;

            if (passive.TryTrigger(condition, source, target))
            {
                ExecutePassiveReaction(passive, source, target);
            }
        }
    }

    private void ExecutePassiveReaction(PassiveAbilityInstance passive, Character source, Character target)
    {
        var passiveData = passive.PassiveData;

        // Apply reaction status effects
        if (passiveData.ReactionEffects != null)
        {
            foreach (var effect in passiveData.ReactionEffects)
            {
                if (effect == null) continue;

                // Determine who receives the reaction effect based on the trigger
                Character effectTarget = DetermineReactionTarget(passiveData.TriggerCondition, source, target);
                if (effectTarget?.StatusManager != null)
                {
                    effectTarget.StatusManager.ApplyEffect(effect, _character);
                }
            }
        }

        // Apply status effects from base AbilitySO
        if (passiveData.StatusEffectsOnSelf != null)
        {
            foreach (var effect in passiveData.StatusEffectsOnSelf)
            {
                if (effect != null && _character?.StatusManager != null)
                    _character.StatusManager.ApplyEffect(effect, _character);
            }
        }

        if (passiveData.StatusEffectsOnTarget != null && source != null)
        {
            Character effectTarget = DetermineReactionTarget(passiveData.TriggerCondition, source, target);
            foreach (var effect in passiveData.StatusEffectsOnTarget)
            {
                if (effect != null && effectTarget?.StatusManager != null)
                    effectTarget.StatusManager.ApplyEffect(effect, _character);
            }
        }

        // Damage reflection
        if (passiveData.ReflectPercentage > 0f && source != null && source != _character)
        {
            // Reflect a % of damage back to the source (handled via TakeDamage context)
            // This will be connected in Phase 5 when we have the damage amount in the trigger context
        }

        Debug.Log($"[CharacterAbilities] {_character.CharacterName} passive '{passiveData.AbilityName}' triggered on {passiveData.TriggerCondition}");
    }

    private Character DetermineReactionTarget(PassiveTriggerCondition condition, Character source, Character target)
    {
        // For damage-taken passives, the reaction targets the attacker (source)
        // For kill/crit passives, the reaction targets self or the killed target
        return condition switch
        {
            PassiveTriggerCondition.OnDamageTaken => source,
            PassiveTriggerCondition.OnAllyDamaged => source,
            PassiveTriggerCondition.OnKill => _character,
            PassiveTriggerCondition.OnCriticalHitDealt => target,
            PassiveTriggerCondition.OnBattleStart => _character,
            PassiveTriggerCondition.OnInitiativeFull => _character,
            PassiveTriggerCondition.OnLowHPThreshold => _character,
            PassiveTriggerCondition.OnDodge => source,
            PassiveTriggerCondition.OnStatusEffectApplied => _character,
            _ => _character
        };
    }

    #endregion

    #region Network Sync

    private void SyncAllSlotsToNetwork()
    {
        if (!IsServer) return;

        _networkAbilitySlots.Clear();

        // Sync active slots (slotType 0)
        for (int i = 0; i < ACTIVE_SLOT_COUNT; i++)
        {
            string abilityId = _activeSlots[i]?.AbilityId ?? "";
            _networkAbilitySlots.Add(new NetworkAbilitySlotData
            {
                SlotType = 0,
                SlotIndex = (byte)i,
                AbilityId = new FixedString64Bytes(abilityId)
            });
        }

        // Sync passive slots (slotType 1)
        for (int i = 0; i < PASSIVE_SLOT_COUNT; i++)
        {
            string abilityId = _passiveSlots[i]?.AbilityId ?? "";
            _networkAbilitySlots.Add(new NetworkAbilitySlotData
            {
                SlotType = 1,
                SlotIndex = (byte)i,
                AbilityId = new FixedString64Bytes(abilityId)
            });
        }
    }

    private void UpdateNetworkSlot(byte slotType, int slotIndex, string abilityId)
    {
        if (!IsServer) return;

        // Find and update existing entry or add new
        for (int i = 0; i < _networkAbilitySlots.Count; i++)
        {
            var entry = _networkAbilitySlots[i];
            if (entry.SlotType == slotType && entry.SlotIndex == (byte)slotIndex)
            {
                _networkAbilitySlots[i] = new NetworkAbilitySlotData
                {
                    SlotType = slotType,
                    SlotIndex = (byte)slotIndex,
                    AbilityId = new FixedString64Bytes(abilityId ?? "")
                };
                return;
            }
        }

        // Not found — add
        _networkAbilitySlots.Add(new NetworkAbilitySlotData
        {
            SlotType = slotType,
            SlotIndex = (byte)slotIndex,
            AbilityId = new FixedString64Bytes(abilityId ?? "")
        });
    }

    private void OnAbilitySlotsChanged(NetworkListEvent<NetworkAbilitySlotData> changeEvent)
    {
        if (IsServer) return; // Server manages locally

        if (changeEvent.Type == NetworkListEvent<NetworkAbilitySlotData>.EventType.Add ||
            changeEvent.Type == NetworkListEvent<NetworkAbilitySlotData>.EventType.Insert ||
            changeEvent.Type == NetworkListEvent<NetworkAbilitySlotData>.EventType.Value)
        {
            ApplyNetworkSlotData(changeEvent.Value);
        }
    }

    private void ApplyNetworkSlotData(NetworkAbilitySlotData data)
    {
        string abilityId = data.AbilityId.ToString();
        if (string.IsNullOrEmpty(abilityId)) return;

        // Resolve ability SO from Resources
        AbilitySO[] allAbilities = Resources.LoadAll<AbilitySO>("Data/Abilities");
        AbilitySO abilitySO = allAbilities.FirstOrDefault(a => a.AbilityId == abilityId);
        if (abilitySO == null) return;

        // Learn if not known (client-side)
        if (!KnowsAbility(abilitySO))
            LearnAbility(abilitySO);

        // Find the matching instance
        AbilityInstance instance = AllKnownAbilities().FirstOrDefault(a => a.Data == abilitySO);
        if (instance == null) return;

        if (data.SlotType == 0)
        {
            _activeSlots[data.SlotIndex] = instance;
            OnActiveSlotChanged?.Invoke(data.SlotIndex, instance);
        }
        else if (data.SlotType == 1 && instance is PassiveAbilityInstance passive)
        {
            _passiveSlots[data.SlotIndex] = passive;
            OnPassiveSlotChanged?.Invoke(data.SlotIndex, passive);
        }
    }

    #endregion

    // ─── ICharacterSaveData Implementation ──────────────────────────

    public string SaveKey => "CharacterAbilities";
    public int LoadPriority => 20;

    public AbilitiesSaveData Serialize()
    {
        var data = new AbilitiesSaveData();

        // Known abilities — save their AbilityId from the SO
        foreach (var ability in _knownPhysicalAbilities)
        {
            if (ability?.Data != null)
                data.knownPhysicalAbilityIds.Add(ability.Data.AbilityId);
        }

        foreach (var spell in _knownSpells)
        {
            if (spell?.Data != null)
                data.knownSpellIds.Add(spell.Data.AbilityId);
        }

        foreach (var passive in _knownPassives)
        {
            if (passive?.Data != null)
                data.knownPassiveIds.Add(passive.Data.AbilityId);
        }

        // Active slots
        for (int i = 0; i < ACTIVE_SLOT_COUNT; i++)
        {
            if (_activeSlots[i]?.Data != null)
            {
                data.activeSlots.Add(new AbilitySlotEntry
                {
                    slotIndex = i,
                    abilityId = _activeSlots[i].Data.AbilityId
                });
            }
        }

        // Passive slots
        for (int i = 0; i < PASSIVE_SLOT_COUNT; i++)
        {
            if (_passiveSlots[i]?.Data != null)
            {
                data.passiveSlots.Add(new AbilitySlotEntry
                {
                    slotIndex = i,
                    abilityId = _passiveSlots[i].Data.AbilityId
                });
            }
        }

        return data;
    }

    public void Deserialize(AbilitiesSaveData data)
    {
        if (data == null) return;

        // Clear existing state
        _knownPhysicalAbilities.Clear();
        _knownSpells.Clear();
        _knownPassives.Clear();
        _activeSlots = new AbilityInstance[ACTIVE_SLOT_COUNT];
        _passiveSlots = new PassiveAbilityInstance[PASSIVE_SLOT_COUNT];

        // Load all ability SOs from Resources
        AbilitySO[] allAbilities = Resources.LoadAll<AbilitySO>("Data/Abilities");

        // Reconstruct known physical abilities
        foreach (var id in data.knownPhysicalAbilityIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var so = System.Array.Find(allAbilities, a => a.AbilityId == id) as PhysicalAbilitySO;
            if (so != null)
                _knownPhysicalAbilities.Add(new PhysicalAbilityInstance(so, _character));
            else
                Debug.LogWarning($"[CharacterAbilities] Could not resolve PhysicalAbilitySO with ID '{id}' during deserialization.");
        }

        // Reconstruct known spells
        foreach (var id in data.knownSpellIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var so = System.Array.Find(allAbilities, a => a.AbilityId == id) as SpellSO;
            if (so != null)
                _knownSpells.Add(new SpellInstance(so, _character));
            else
                Debug.LogWarning($"[CharacterAbilities] Could not resolve SpellSO with ID '{id}' during deserialization.");
        }

        // Reconstruct known passives
        foreach (var id in data.knownPassiveIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var so = System.Array.Find(allAbilities, a => a.AbilityId == id) as PassiveAbilitySO;
            if (so != null)
                _knownPassives.Add(new PassiveAbilityInstance(so, _character));
            else
                Debug.LogWarning($"[CharacterAbilities] Could not resolve PassiveAbilitySO with ID '{id}' during deserialization.");
        }

        // Restore active slot assignments
        foreach (var slotEntry in data.activeSlots)
        {
            if (string.IsNullOrEmpty(slotEntry.abilityId)) continue;
            if (slotEntry.slotIndex < 0 || slotEntry.slotIndex >= ACTIVE_SLOT_COUNT) continue;

            // Find the matching instance in known abilities (physical or spell)
            AbilityInstance instance = _knownPhysicalAbilities.Find(a => a.Data.AbilityId == slotEntry.abilityId);
            if (instance == null)
                instance = _knownSpells.Find(a => a.Data.AbilityId == slotEntry.abilityId);

            if (instance != null)
                _activeSlots[slotEntry.slotIndex] = instance;
            else
                Debug.LogWarning($"[CharacterAbilities] Active slot {slotEntry.slotIndex}: could not find known ability '{slotEntry.abilityId}'.");
        }

        // Restore passive slot assignments
        foreach (var slotEntry in data.passiveSlots)
        {
            if (string.IsNullOrEmpty(slotEntry.abilityId)) continue;
            if (slotEntry.slotIndex < 0 || slotEntry.slotIndex >= PASSIVE_SLOT_COUNT) continue;

            var passive = _knownPassives.Find(p => p.Data.AbilityId == slotEntry.abilityId);
            if (passive != null)
                _passiveSlots[slotEntry.slotIndex] = passive;
            else
                Debug.LogWarning($"[CharacterAbilities] Passive slot {slotEntry.slotIndex}: could not find known passive '{slotEntry.abilityId}'.");
        }

        // Push restored state to network if server
        if (IsServer && IsSpawned)
        {
            SyncAllSlotsToNetwork();
        }
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}

/// <summary>
/// Network-serializable struct for syncing ability slot assignments.
/// </summary>
public struct NetworkAbilitySlotData : INetworkSerializable, System.IEquatable<NetworkAbilitySlotData>
{
    public byte SlotType;  // 0 = active, 1 = passive
    public byte SlotIndex; // Index within the slot type
    public FixedString64Bytes AbilityId;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SlotType);
        serializer.SerializeValue(ref SlotIndex);
        serializer.SerializeValue(ref AbilityId);
    }

    public bool Equals(NetworkAbilitySlotData other)
    {
        return SlotType == other.SlotType && SlotIndex == other.SlotIndex && AbilityId == other.AbilityId;
    }
}
