using UnityEngine;
using MWI.WorldSystem;

namespace MWI.Time
{
    public static class MacroSimulator
    {
        /// <summary>
        /// Fast-forwards the hibernated data based on the elapsed time.
        /// </summary>
        public static void SimulateCatchUp(MapSaveData savedData, int currentDay, float currentTime01)
        {
            if (savedData == null || savedData.HibernatedNPCs == null) return;

            // Absolute time formula: (Full Days) + (Fraction of Current Day)
            double currentAbsoluteTime = currentDay + currentTime01;
            double daysPassed = currentAbsoluteTime - savedData.LastHibernationTime;

            if (daysPassed <= 0) return;

            float hoursPassed = (float)(daysPassed * 24.0);

            Debug.Log($"<color=orange>[MacroSim]</color> Fast-forwarding Map '{savedData.MapId}' by {hoursPassed:F2} in-game hours.");

            int currentHour = Mathf.FloorToInt(currentTime01 * 24f);

            foreach (var npc in savedData.HibernatedNPCs)
            {
                SimulateNPCCatchUp(npc, savedData.MapId, currentHour, hoursPassed);
            }
        }

        private static void SimulateNPCCatchUp(HibernatedNPCData npcData, string currentMapId, int currentHour, float hoursPassed)
        {
            // Tier 1: Snap Position based on Anchors
            if (!npcData.HasSchedule) return;

            string targetMapId = null;
            Vector3 targetPosition = npcData.Position; // Default to wherever they were

            // Determine Target Anchor based on hour.
            // Simplified routine: Sleep -> Work -> FreeTime -> (wrapper) Sleep
            if (IsHourInRange(currentHour, npcData.SleepHourStarts, npcData.WorkHourStarts))
            {
                targetMapId = npcData.HomeMapId;
                targetPosition = npcData.HomePosition;
            }
            else if (IsHourInRange(currentHour, npcData.WorkHourStarts, npcData.FreeTimeStarts))
            {
                targetMapId = npcData.WorkMapId;
                targetPosition = npcData.WorkPosition;
            }
            else // Free Time zone
            {
                targetMapId = npcData.FreeTimeMapId;
                targetPosition = npcData.FreeTimePosition;
            }

            // Cross-Map Check (Option 1: Lazy Routing)
            // If they belong here, snap them. If they belong somewhere else, do nothing and let live AI route them across maps.
            if (string.IsNullOrEmpty(targetMapId) || targetMapId == currentMapId)
            {
                // Only snap if we have valid coordinates (not default empty Vector3)
                // This prevents snapping to (0,0,0) if an anchor wasn't fully set
                if (targetPosition != Vector3.zero) 
                {
                    npcData.Position = targetPosition;
                }
            }
            else
            {
                // Edge case handling for Option 1: NPC structurally belongs in City B right now, but Map A is waking up.
                // We leave them exactly where they went to sleep in Map A. When they spawn, their live GOAP will issue a pathing order to Map B via doors.
            }
        }

        private static bool IsHourInRange(int currentHour, int startHour, int endHour)
        {
            if (startHour == endHour) return false;
            
            if (startHour < endHour)
            {
                // Normal daytime shift (e.g. 8 to 18)
                return currentHour >= startHour && currentHour < endHour;
            }
            else
            {
                // Overnight shift (e.g. 22 to 8)
                return currentHour >= startHour || currentHour < endHour; 
            }
        }
    }
}
