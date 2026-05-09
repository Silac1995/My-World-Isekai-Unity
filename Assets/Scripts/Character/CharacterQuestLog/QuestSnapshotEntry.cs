using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Save-side + network-snapshot representation of a single Quest claimed by a Character.
/// Denormalized display + target data so the HUD can render when the live source is
/// unloaded (different map, hibernating building).
/// Implements INetworkSerializable so it can be pushed via ClientRpc to the owning client.
/// </summary>
[Serializable]
public class QuestSnapshotEntry : INetworkSerializable
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

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        // Null-safe string serialization via local temporaries (strings default to null in managed code,
        // but BufferSerializer requires non-null; coerce to string.Empty on write, restore on read if empty).
        SerializeString(serializer, ref questId);
        SerializeString(serializer, ref originWorldId);
        SerializeString(serializer, ref originMapId);
        SerializeString(serializer, ref issuerCharacterId);
        serializer.SerializeValue(ref questType);

        SerializeString(serializer, ref title);
        SerializeString(serializer, ref instructionLine);
        SerializeString(serializer, ref description);

        serializer.SerializeValue(ref totalProgress);
        serializer.SerializeValue(ref required);
        serializer.SerializeValue(ref maxContributors);
        serializer.SerializeValue(ref myContribution);

        serializer.SerializeValue(ref state);

        serializer.SerializeValue(ref targetPosition);
        serializer.SerializeValue(ref hasZoneBounds);
        serializer.SerializeValue(ref zoneCenter);
        serializer.SerializeValue(ref zoneSize);
        SerializeString(serializer, ref targetDisplayName);
    }

    private static void SerializeString<T>(BufferSerializer<T> serializer, ref string value) where T : IReaderWriter
    {
        if (serializer.IsWriter)
        {
            value ??= string.Empty;
        }
        serializer.SerializeValue(ref value);
    }
}
