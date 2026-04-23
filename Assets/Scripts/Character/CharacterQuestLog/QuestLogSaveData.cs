using System;
using System.Collections.Generic;

/// <summary>
/// Outer save DTO for CharacterQuestLog. Lists the character's claimed quests
/// (live on current map + dormant on other maps) plus the focused quest id.
/// </summary>
[Serializable]
public class QuestLogSaveData
{
    public List<QuestSnapshotEntry> activeQuests = new List<QuestSnapshotEntry>();
    public string focusedQuestId = string.Empty;
}
