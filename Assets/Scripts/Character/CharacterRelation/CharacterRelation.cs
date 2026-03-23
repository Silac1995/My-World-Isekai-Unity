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

public class CharacterRelation : CharacterSystem
{
    [SerializeField] private Character _character;
    [SerializeField] private List<Relationship> _relationships = new List<Relationship>();
    
    private NetworkList<RelationSyncData> _networkRelations;

    [Header("Notifications")]
    [SerializeField] private MWI.UI.Notifications.ToastNotificationChannel _toastChannel;
    [SerializeField] private MWI.UI.Notifications.NotificationChannel _relationNotificationChannel;

    public Character Character => _character;
    public List<Relationship> Relationships => _relationships;

    public event Action OnRelationsUpdated;

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
                ApplySyncDataLocal(syncData);
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

    private void HandleNetworkRelationsChanged(NetworkListEvent<RelationSyncData> changeEvent)
    {
        if (IsServer) return; // Server manages local list naturally
        ApplySyncDataLocal(changeEvent.Value);
    }

    private void ApplySyncDataLocal(RelationSyncData syncData)
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
                _relationships.Add(existing);

                if (_relationNotificationChannel != null && _character.IsPlayer())
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

                    if (_toastChannel != null && _character.IsPlayer())
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

        if (_relationNotificationChannel != null && _character.IsPlayer())
        {
            _relationNotificationChannel.Raise();
        }

        UpdateNetworkList(newRel);

        return newRel;
    }

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

        Debug.Log($"<color=white>[Sentiment]</color> L'avis de {_character.CharacterName} sur {target.CharacterName} est maintenant de {rel.RelationValue} ({rel.RelationType}) [Modif: {amount} -> {roundedAmount}]");

        OnRelationsUpdated?.Invoke();

        if (_toastChannel != null && _character.IsPlayer())
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
