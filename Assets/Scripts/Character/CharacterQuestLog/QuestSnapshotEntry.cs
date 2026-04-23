using System;
using UnityEngine;

/// <summary>
/// Save-side + network-snapshot representation of a single Quest claimed by a Character.
/// Denormalized display + target data so the HUD can render when the live source is
/// unloaded (different map, hibernating building).
/// </summary>
[Serializable]
public class QuestSnapshotEntry
{
    public string questId;
    public string originWorldId;
    public string originMapId;
    public string issuerCharacterId;
    public int questType;          // QuestType enum as int

    public string title;
    public string instructionLine;
    public string description;

    public int totalProgress;
    public int required;
    public int maxContributors;
    public int myContribution;

    public int state;              // QuestState enum as int

    public Vector3 targetPosition;
    public bool hasZoneBounds;
    public Vector3 zoneCenter;
    public Vector3 zoneSize;
    public string targetDisplayName;
}
