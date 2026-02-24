using UnityEngine;

/// <summary>
/// Bloc horaire dans le schedule d'un personnage.
/// Définit une activité à effectuer entre startHour et endHour.
/// </summary>
[System.Serializable]
public class ScheduleEntry
{
    [Range(0, 23)] public int startHour;
    [Range(0, 23)] public int endHour;
    public ScheduleActivity activity;
    public int priority = 0; // Plus haut = prioritaire en cas de chevauchement

    public ScheduleEntry() { }

    public ScheduleEntry(int start, int end, ScheduleActivity act, int prio = 0)
    {
        startHour = start;
        endHour = end;
        activity = act;
        priority = prio;
    }

    /// <summary>
    /// Vérifie si ce créneau est actif à une heure donnée.
    /// Gère le cas où le créneau passe minuit (ex: 22h → 6h).
    /// </summary>
    public bool IsActiveAtHour(int hour)
    {
        if (startHour <= endHour)
        {
            // Créneau normal : 8h → 17h
            return hour >= startHour && hour < endHour;
        }
        else
        {
            // Créneau passant minuit : 22h → 6h
            return hour >= startHour || hour < endHour;
        }
    }
}
