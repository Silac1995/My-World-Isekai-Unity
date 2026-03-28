using System;
using System.Collections.Generic;
using UnityEngine;

using Unity.Collections;
using Unity.Netcode;

public class CharacterSkills : CharacterSystem
{

    [SerializeField]
    private List<SkillInstance> _skills = new List<SkillInstance>();

    public IReadOnlyList<SkillInstance> Skills => _skills;

    /// <summary>
    /// Pour recherche rapide des skills par leur SO.
    /// </summary>
    private Dictionary<SkillSO, SkillInstance> _skillMap = new Dictionary<SkillSO, SkillInstance>();

    private NetworkList<NetworkSkillSyncData> _networkSkills;

    protected override void Awake()
    {
        base.Awake();

        // Fallback: if CharacterSkills sits on the same GO as Character
        if (_character == null)
        {
            _character = GetComponent<Character>();
        }

        _networkSkills = new NetworkList<NetworkSkillSyncData>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        SyncSkillsFromInspector();
    }

    /// <summary>
    /// Permet de supporter l'ajout manuel de skills hors-code depuis l'inspecteur d'Unity (même en Play Mode).
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            SyncSkillsFromInspector();
        }
    }

    private Dictionary<CharacterBaseStats, float> _appliedSkillBonuses = new Dictionary<CharacterBaseStats, float>();

    private void SyncSkillsFromInspector()
    {
        if (_skillMap == null) _skillMap = new Dictionary<SkillSO, SkillInstance>();

        foreach (var skill in _skills)
        {
            if (skill != null && skill.Skill != null)
            {
                if (!_skillMap.ContainsKey(skill.Skill))
                {
                    _skillMap.Add(skill.Skill, skill);
                    skill.OnLevelUp += HandleSkillLevelUp; // S'abonner aux level ups
                }
            }
        }

        if (Application.isPlaying && _character != null && _character.Stats != null)
        {
            RecalculateAllSkillBonuses();
        }

        // Push inspector changes to clients
        if (Application.isPlaying && IsServer && IsSpawned)
        {
            SyncAllSkillsToNetwork();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkSkills.OnListChanged += OnNetworkSkillsChanged;

        if (IsServer)
        {
            SyncAllSkillsToNetwork();
        }
        else
        {
            // Client: rebuild local skills from network state
            FullSyncFromNetwork();
        }
    }

    public override void OnNetworkDespawn()
    {
        _networkSkills.OnListChanged -= OnNetworkSkillsChanged;
        base.OnNetworkDespawn();
    }

    private void Start()
    {
        // On s'assure que les bonus sont calculés au démarrage
        RecalculateAllSkillBonuses();
    }

    private void OnDestroy()
    {
        foreach (var skill in _skills)
        {
            if (skill != null)
            {
                skill.OnLevelUp -= HandleSkillLevelUp;
            }
        }
    }

    public void AddSkill(SkillSO newSkill, int startingLevel = 1)
    {
        if (newSkill == null || _skillMap.ContainsKey(newSkill)) return;

        SkillInstance instance = new SkillInstance(newSkill, startingLevel);
        _skills.Add(instance);
        _skillMap.Add(newSkill, instance);

        instance.OnLevelUp += HandleSkillLevelUp;

        RecalculateAllSkillBonuses();
        UpdateNetworkSkill(instance);
    }

    public bool HasSkill(SkillSO skill)
    {
        if (skill == null) return false;
        // First try direct reference match (fast, works on server)
        if (_skillMap.ContainsKey(skill)) return true;
        // Fallback: match by SkillID (handles client-side where the SkillSO
        // instance resolved from network may differ from the serialized reference)
        return HasSkillById(skill.SkillID);
    }

    public bool HasSkillById(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        foreach (var s in _skills)
        {
            if (s?.Skill != null && s.Skill.SkillID == skillId) return true;
        }
        return false;
    }

    public int GetSkillLevel(SkillSO skill)
    {
        if (skill == null) return 0;
        if (_skillMap.TryGetValue(skill, out var instance)) return instance.Level;
        // Fallback: match by SkillID for client-side reference mismatch
        foreach (var s in _skills)
        {
            if (s?.Skill != null && s.Skill.SkillID == skill.SkillID) return s.Level;
        }
        return 0;
    }

    /// <summary>
    /// Retourne l'Efficacité finale (Niveau du métier + Bonus des Statistiques (Agi, Str, etc.)).
    /// </summary>
    public float GetSkillProficiency(SkillSO skill)
    {
        if (skill == null || !_skillMap.TryGetValue(skill, out var instance)) return 0f;
        return instance.CalculateProficiency(_character.Stats);
    }

    public bool HasRequiredSkillLevel(SkillSO skill, int requiredLevel)
    {
        if (skill == null) return false;
        if (!_skillMap.TryGetValue(skill, out var instance)) return false;

        return instance.Level >= requiredLevel;
    }

    public void GainXP(SkillSO skill, int amount)
    {
        if (skill == null) return;

        if (_skillMap.TryGetValue(skill, out var instance))
        {
            instance.AddXP(amount);
            UpdateNetworkSkill(instance);
        }
        else
        {
            // Le personnage apprend la compétence lors du premier gain d'XP
            AddSkill(skill, 1);
            _skillMap[skill].AddXP(amount);
            UpdateNetworkSkill(_skillMap[skill]);
        }
    }

    /// <summary>
    /// Appelé par l'event OnLevelUp d'un SkillInstance
    /// </summary>
    private void HandleSkillLevelUp(SkillInstance skillInstance, int newLevel)
    {
        Debug.Log($"<color=cyan>[CharacterSkills]</color> {_character.CharacterName} est passé niveau {newLevel} en {skillInstance.Skill.SkillName}.");
        RecalculateAllSkillBonuses();
    }

    // ─── Network Sync ───────────────────────────────────────────────────

    /// <summary>
    /// Pushes the full local skill list to the NetworkList (server only).
    /// </summary>
    private void SyncAllSkillsToNetwork()
    {
        if (!IsServer || !IsSpawned) return;

        _networkSkills.Clear();
        foreach (var skill in _skills)
        {
            if (skill?.Skill == null) continue;
            _networkSkills.Add(new NetworkSkillSyncData
            {
                SkillID  = new FixedString64Bytes(skill.Skill.SkillID),
                Level    = skill.Level,
                CurrentXP = skill.XP,
                TotalXP  = skill.TotalXP
            });
        }
    }

    /// <summary>
    /// Updates or adds a single skill entry in the NetworkList (server only).
    /// </summary>
    private void UpdateNetworkSkill(SkillInstance skill)
    {
        if (!IsServer || !IsSpawned || skill?.Skill == null) return;

        string skillId = skill.Skill.SkillID;

        for (int i = 0; i < _networkSkills.Count; i++)
        {
            if (_networkSkills[i].SkillID.ToString() == skillId)
            {
                _networkSkills[i] = new NetworkSkillSyncData
                {
                    SkillID   = new FixedString64Bytes(skillId),
                    Level     = skill.Level,
                    CurrentXP = skill.XP,
                    TotalXP   = skill.TotalXP
                };
                return;
            }
        }

        _networkSkills.Add(new NetworkSkillSyncData
        {
            SkillID   = new FixedString64Bytes(skillId),
            Level     = skill.Level,
            CurrentXP = skill.XP,
            TotalXP   = skill.TotalXP
        });
    }

    /// <summary>
    /// Called on clients when the server's NetworkList changes.
    /// </summary>
    private void OnNetworkSkillsChanged(NetworkListEvent<NetworkSkillSyncData> changeEvent)
    {
        if (IsServer) return;

        switch (changeEvent.Type)
        {
            case NetworkListEvent<NetworkSkillSyncData>.EventType.Add:
            case NetworkListEvent<NetworkSkillSyncData>.EventType.Value:
            case NetworkListEvent<NetworkSkillSyncData>.EventType.Insert:
                ApplyNetworkSkillData(changeEvent.Value);
                break;

            case NetworkListEvent<NetworkSkillSyncData>.EventType.Clear:
                foreach (var stat in _appliedSkillBonuses.Keys)
                {
                    stat.RemoveAllModifiersFromSource(this);
                }
                _appliedSkillBonuses.Clear();
                foreach (var skill in _skills)
                {
                    if (skill != null) skill.OnLevelUp -= HandleSkillLevelUp;
                }
                _skills.Clear();
                _skillMap.Clear();
                break;
        }
    }

    /// <summary>
    /// Late-joining client: rebuild local skills from the current NetworkList snapshot.
    /// </summary>
    private void FullSyncFromNetwork()
    {
        foreach (var skill in _skills)
        {
            if (skill != null) skill.OnLevelUp -= HandleSkillLevelUp;
        }
        _skills.Clear();
        _skillMap.Clear();

        foreach (var data in _networkSkills)
        {
            ApplyNetworkSkillData(data);
        }
    }

    /// <summary>
    /// Resolves a SkillSO from Resources and creates/updates the local SkillInstance.
    /// </summary>
    private void ApplyNetworkSkillData(NetworkSkillSyncData data)
    {
        string skillId = data.SkillID.ToString();

        // Check if we already have this skill locally
        foreach (var skill in _skills)
        {
            if (skill?.Skill != null && skill.Skill.SkillID == skillId)
            {
                skill.UpdateFromNetwork(data.Level, data.CurrentXP, data.TotalXP);
                RecalculateAllSkillBonuses();
                return;
            }
        }

        // Resolve the SkillSO asset on the client from Resources/Data/Skills
        SkillSO[] allSkills = Resources.LoadAll<SkillSO>("Data/Skills");
        SkillSO skillSO = Array.Find(allSkills, s => s.SkillID == skillId);

        if (skillSO == null)
        {
            Debug.LogError($"<color=red>[CharacterSkills]</color> Client could not resolve SkillSO with ID '{skillId}'.");
            return;
        }

        SkillInstance instance = new SkillInstance(skillSO, data.Level, data.CurrentXP, data.TotalXP);
        _skills.Add(instance);
        _skillMap[skillSO] = instance;
        instance.OnLevelUp += HandleSkillLevelUp;

        RecalculateAllSkillBonuses();
    }

    // ─── Stat Bonuses ───────────────────────────────────────────────────

    /// <summary>
    /// Calcule de zéro tous les bonus conférés par les Skills actuels et leurs niveaux.
    /// Utilise ApplyModifier/RemoveModifier pour ne pas écraser les stats de base liées à la race ou aux niveaux.
    /// </summary>
    private void RecalculateAllSkillBonuses()
    {
        if (_character == null || _character.Stats == null) return;

        // 1. Retirer tous les anciens modificateurs appliqués par les skills
        foreach (var stat in _appliedSkillBonuses.Keys)
        {
            stat.RemoveAllModifiersFromSource(this);
        }
        _appliedSkillBonuses.Clear();

        // 2. Calculer la somme des nouveaux bonus à appliquer
        Dictionary<CharacterBaseStats, float> newBonuses = new Dictionary<CharacterBaseStats, float>();

        foreach (var skillInstance in _skills)
        {
            if (skillInstance == null || skillInstance.Skill == null || skillInstance.Skill.LevelBonuses == null) continue;

            foreach (var bonus in skillInstance.Skill.LevelBonuses)
            {
                if (skillInstance.Level >= bonus.RequiredLevel) // On applique tous les bonus débloqués
                {
                    var statToBoost = _character.Stats.GetBaseStat(bonus.StatToBoost);
                    if (statToBoost != null)
                    {
                        if (!newBonuses.ContainsKey(statToBoost)) newBonuses[statToBoost] = 0f;
                        newBonuses[statToBoost] += bonus.BonusValue;
                    }
                }
            }
        }

        // 3. Appliquer les nouveaux modificateurs et les enregistrer
        foreach (var kvp in newBonuses)
        {
            kvp.Key.ApplyModifier(new StatModifier(kvp.Value, this));
            _appliedSkillBonuses[kvp.Key] = kvp.Value;
            Debug.Log($"<color=green>[CharacterSkills]</color> Modificateur total appliqué : +{kvp.Value} sur {kvp.Key.StatName}.");
        }

        // 4. Demander un recalcul global des stats tertiaires/dynamiques (HP Max, etc.)
        _character.Stats.RecalculateTertiaryStats();
    }
}

/// <summary>
/// Lightweight struct sent over the network to sync skill state.
/// Clients resolve the SkillSO from Resources using the SkillID.
/// </summary>
public struct NetworkSkillSyncData : INetworkSerializable, IEquatable<NetworkSkillSyncData>
{
    public FixedString64Bytes SkillID;
    public int Level;
    public int CurrentXP;
    public int TotalXP;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SkillID);
        serializer.SerializeValue(ref Level);
        serializer.SerializeValue(ref CurrentXP);
        serializer.SerializeValue(ref TotalXP);
    }

    public bool Equals(NetworkSkillSyncData other)
    {
        return SkillID.Equals(other.SkillID)
            && Level == other.Level
            && CurrentXP == other.CurrentXP
            && TotalXP == other.TotalXP;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(SkillID, Level, CurrentXP, TotalXP);
    }
}
