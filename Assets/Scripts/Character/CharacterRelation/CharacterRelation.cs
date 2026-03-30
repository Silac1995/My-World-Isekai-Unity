using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public struct RelationSyncData : INetworkSerializable, IEquatable<RelationSyncData>
{
    public ulong TargetId;
    public int RelationValue;
    public RelationshipType RelationType;
    public bool HasMet;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref TargetId);
        serializer.SerializeValue(ref RelationValue);
        serializer.SerializeValue(ref RelationType);
        serializer.SerializeValue(ref HasMet);
    }

    public bool Equals(RelationSyncData other) 
    {
        return TargetId == other.TargetId && 
               RelationValue == other.RelationValue && 
               RelationType == other.RelationType && 
               HasMet == other.HasMet;
    }
}

public class CharacterRelation : CharacterSystem, ICharacterSaveData<RelationSaveData>
{
    [SerializeField] private List<Relationship> _relationships = new List<Relationship>();

    /// <summary>
    /// Dormant relationship entries loaded from save data whose target characters
    /// are not present in the current world. Re-serialized on next save.
    /// </summary>
    private List<RelationshipSaveEntry> _dormantRelationships = new List<RelationshipSaveEntry>();

    private NetworkList<RelationSyncData> _networkRelations;

    [Header("Notifications")]
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _relationNotificationChannel;

    public Character Character => _character;
    public List<Relationship> Relationships => _relationships;

    public event Action OnRelationsUpdated;

    protected override void OnEnable()
    {
        base.OnEnable();
        Character.OnCharacterSpawned += HandleCharacterSpawned;
    }

    protected override void OnDisable()
    {
        Character.OnCharacterSpawned -= HandleCharacterSpawned;
        base.OnDisable();
    }

    protected override void Awake()
    {
        base.Awake();
        _networkRelations = new NetworkList<RelationSyncData>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkRelations.OnListChanged += HandleNetworkRelationsChanged;

        if (IsClient && !IsServer)
        {
            foreach (var syncData in _networkRelations)
            {
                ApplySyncDataLocal(syncData, isInitialSync: true);
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (_networkRelations != null)
        {
            _networkRelations.OnListChanged -= HandleNetworkRelationsChanged;
        }
    }

    /// <summary>
    /// When a character spawns, check if any dormant relationships reference it.
    /// If so, resolve them into live Relationship instances.
    /// </summary>
    private void HandleCharacterSpawned(Character spawnedCharacter)
    {
        if (_dormantRelationships.Count == 0) return;
        if (spawnedCharacter == null || spawnedCharacter == _character) return;

        bool resolved = false;

        for (int i = _dormantRelationships.Count - 1; i >= 0; i--)
        {
            var dormant = _dormantRelationships[i];

            if (dormant.targetCharacterId == spawnedCharacter.CharacterId)
            {
                // Avoid duplicating an already-live relationship
                Relationship existing = _relationships.Find(r => r.RelatedCharacter == spawnedCharacter);
                if (existing == null)
                {
                    var rel = new Relationship(_character, spawnedCharacter, dormant.relationValue, (RelationshipType)dormant.relationshipType);
                    if (dormant.hasMet) rel.SetAsMet();
                    _relationships.Add(rel);

                    if (IsServer)
                    {
                        UpdateNetworkList(rel);
                    }

                    Debug.Log($"<color=cyan>[Relation]</color> Dormant relationship resolved: {_character.CharacterName} -> {spawnedCharacter.CharacterName} (value: {dormant.relationValue})");
                }

                _dormantRelationships.RemoveAt(i);
                resolved = true;
            }
        }

        if (resolved)
        {
            OnRelationsUpdated?.Invoke();
        }
    }

    private void HandleNetworkRelationsChanged(NetworkListEvent<RelationSyncData> changeEvent)
    {
        if (IsServer) return; // Server manages local list naturally
        ApplySyncDataLocal(changeEvent.Value, isInitialSync: false);
    }

    private void ApplySyncDataLocal(RelationSyncData syncData, bool isInitialSync)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(syncData.TargetId, out var netObj))
        {
            Character target = netObj.GetComponent<Character>();
            if (target == null) return;

            Relationship existing = _relationships.Find(r => r.RelatedCharacter == target);
            if (existing == null)
            {
                existing = new Relationship(Character, target, syncData.RelationValue, syncData.RelationType);
                if (syncData.HasMet) existing.SetAsMet();
                
                if (!isInitialSync)
                {
                    existing.IsNewlyAdded = true;
                }
                
                _relationships.Add(existing);

                if (!isInitialSync && _relationNotificationChannel != null && _character.IsPlayer() && _character.IsOwner)
                {
                    _relationNotificationChannel.Raise();
                }

                OnRelationsUpdated?.Invoke();
            }
            else
            {
                if (existing.RelationValue != syncData.RelationValue)
                {
                    int difference = syncData.RelationValue - existing.RelationValue;
                    existing.RelationValue = syncData.RelationValue;

                    if (!isInitialSync && _toastChannel != null && _character.IsPlayer() && _character.IsOwner)
                    {
                        string sign = difference >= 0 ? "+" : "";
                        var toastType = difference >= 0 ? MWI.UI.Notifications.ToastType.Success : MWI.UI.Notifications.ToastType.Warning;
                        
                        _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                            message: $"{_character.CharacterName} \u2192 {target.CharacterName}: {sign}{difference} Relation",
                            type: toastType,
                            duration: 3f,
                            icon: null
                        ));
                    }
                }
                
                existing.SetRelationshipType(syncData.RelationType);
                
                if (syncData.HasMet) existing.SetAsMet();
                else existing.SetAsNotMet();
                
                OnRelationsUpdated?.Invoke();
            }
        }
    }

    private void UpdateNetworkList(Relationship rel)
    {
        if (!IsServer || rel == null || rel.RelatedCharacter == null || rel.RelatedCharacter.NetworkObject == null) return;

        ulong targetId = rel.RelatedCharacter.NetworkObject.NetworkObjectId;
        RelationSyncData syncData = new RelationSyncData
        {
            TargetId = targetId,
            RelationValue = rel.RelationValue,
            RelationType = rel.RelationType,
            HasMet = rel.HasMet
        };

        for (int i = 0; i < _networkRelations.Count; i++)
        {
            if (_networkRelations[i].TargetId == targetId)
            {
                if (_networkRelations[i].RelationValue != syncData.RelationValue || 
                    _networkRelations[i].RelationType != syncData.RelationType || 
                    _networkRelations[i].HasMet != syncData.HasMet)
                {
                    _networkRelations[i] = syncData;
                }
                return;
            }
        }
        
        _networkRelations.Add(syncData);
    }

    public void SyncRelationshipToNetwork(Relationship rel)
    {
        if (IsServer)
        {
            UpdateNetworkList(rel);
        }
    }

    public Relationship GetRelationshipWith(Character otherCharacter)
    {
        Relationship rel = _relationships.Find(r => r.RelatedCharacter == otherCharacter);
        
        // Late-joiner safety: If we don't have it locally, check if it's in the NetworkList
        if (rel == null && !IsServer && otherCharacter != null && otherCharacter.NetworkObject != null)
        {
            ulong targetId = otherCharacter.NetworkObject.NetworkObjectId;
            foreach (var syncData in _networkRelations)
            {
                if (syncData.TargetId == targetId)
                {
                    rel = new Relationship(Character, otherCharacter, syncData.RelationValue, syncData.RelationType);
                    if (syncData.HasMet) rel.SetAsMet();
                    _relationships.Add(rel);
                    break;
                }
            }
        }
        
        return rel;
    }

    public void InitializeNotifications(MWI.UI.Notifications.NotificationChannel relationChannel, MWI.UI.Notifications.ToastNotificationChannel toastChannel = null)
    {
        _relationNotificationChannel = relationChannel;
        if (toastChannel != null) _toastChannel = toastChannel;
    }

    public void ClearNotifications()
    {
        if (_relationNotificationChannel != null)
        {
            _relationNotificationChannel.Clear();
        }
    }

    public bool HasNewRelations()
    {
        return _relationships.Exists(r => r.IsNewlyAdded);
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

    /// <summary>
    /// Returns the total number of friends (including lovers and soulmates).
    /// </summary>
    public int GetFriendCount()
    {
        int count = 0;
        foreach (var rel in _relationships)
        {
            if (IsFriend(rel.RelatedCharacter))
            {
                count++;
            }
        }
        return count;
    }

    // Ajoute une nouvelle relation (Bilatéral : ils se connaissent)
    public Relationship AddRelationship(Character otherCharacter)
    {
        if (!IsServer) return GetRelationshipWith(otherCharacter);
        
        Relationship existing = GetRelationshipWith(otherCharacter);
        if (existing != null) return existing;

        Relationship newRel = new Relationship(Character, otherCharacter);
        newRel.IsNewlyAdded = true;
        _relationships.Add(newRel);

        Debug.Log($"<color=cyan>[Relation]</color> {_character.CharacterName} a rencontré {otherCharacter.CharacterName}");

        var targetRelationSystem = otherCharacter.GetComponentInChildren<CharacterRelation>();
        if (targetRelationSystem != null)
        {
            targetRelationSystem.AddRelationship(_character);
        }

        OnRelationsUpdated?.Invoke();

        if (_relationNotificationChannel != null && _character.IsPlayer() && _character.IsOwner)
        {
            _relationNotificationChannel.Raise();
        }

        UpdateNetworkList(newRel);

        return newRel;
    }

    // --- ICharacterSaveData<RelationSaveData> IMPLEMENTATION ---

    public string SaveKey => "CharacterRelation";
    public int LoadPriority => 50;

    public RelationSaveData Serialize()
    {
        var data = new RelationSaveData();

        // Serialize live relationships
        foreach (var rel in _relationships)
        {
            if (rel.RelatedCharacter == null) continue;

            var entry = new RelationshipSaveEntry
            {
                targetCharacterId = rel.RelatedCharacter.CharacterId,
                targetWorldGuid = rel.RelatedCharacter.OriginWorldGuid ?? "",
                relationshipType = (int)rel.RelationType,
                relationValue = rel.RelationValue,
                hasMet = rel.HasMet
            };
            data.relationships.Add(entry);
        }

        // Re-serialize dormant relationships that were not resolved this session
        foreach (var dormant in _dormantRelationships)
        {
            // Avoid duplicates — skip if already serialized from a live relationship
            bool alreadySerialized = data.relationships.Exists(e => e.targetCharacterId == dormant.targetCharacterId);
            if (!alreadySerialized)
            {
                data.relationships.Add(dormant);
            }
        }

        return data;
    }

    public void Deserialize(RelationSaveData data)
    {
        if (data == null || data.relationships == null) return;

        _dormantRelationships.Clear();

        foreach (var entry in data.relationships)
        {
            // Attempt to find the target character in the current world
            Character target = Character.FindByUUID(entry.targetCharacterId);

            if (target != null)
            {
                // Resolve immediately — create a live relationship
                Relationship existing = _relationships.Find(r => r.RelatedCharacter == target);
                if (existing == null)
                {
                    var rel = new Relationship(_character, target, entry.relationValue, (RelationshipType)entry.relationshipType);
                    if (entry.hasMet) rel.SetAsMet();
                    _relationships.Add(rel);
                }
                else
                {
                    existing.RelationValue = entry.relationValue;
                    existing.SetRelationshipType((RelationshipType)entry.relationshipType);
                    if (entry.hasMet) existing.SetAsMet();
                    else existing.SetAsNotMet();
                }
            }
            else
            {
                // Store as dormant — target not present in current world
                _dormantRelationships.Add(entry);
            }
        }

        OnRelationsUpdated?.Invoke();
    }

    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);

    // --- RELATION LOGIC ---

    public void UpdateRelation(Character target, int amount)
    {
        if (!IsServer) return;
        
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

        // Debug.Log($"<color=white>[Sentiment]</color> L'avis de {_character.CharacterName} sur {target.CharacterName} est maintenant de {rel.RelationValue} ({rel.RelationType}) [Modif: {amount} -> {roundedAmount}]");

        OnRelationsUpdated?.Invoke();

        if (_toastChannel != null && _character.IsPlayer() && _character.IsOwner)
        {
            string sign = roundedAmount >= 0 ? "+" : "";
            var toastType = roundedAmount >= 0 ? MWI.UI.Notifications.ToastType.Success : MWI.UI.Notifications.ToastType.Warning;
            
            _toastChannel.Raise(new MWI.UI.Notifications.ToastNotificationPayload(
                message: $"{_character.CharacterName} \u2192 {target.CharacterName}: {sign}{roundedAmount} Relation",
                type: toastType,
                duration: 3f,
                icon: null
            ));
        }

        UpdateNetworkList(rel);
    }
}
