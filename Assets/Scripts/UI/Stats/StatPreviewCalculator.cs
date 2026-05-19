using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure math that drives the "+1 STR -> PhysPwr 14.4 -> 15.6" preview line on each
/// secondary-stat tooltip in the Character Stats window.
///
/// Per Phase 0 recon §5d the calculator does NOT hard-code per-stat multipliers,
/// because race data overrides them at runtime via CharacterStats.ApplyRaceStats.
/// Callers build a <see cref="ScalingTable"/> from the live CharacterTertiaryStats
/// instances (Multiplier / BaseOffset / LinkedStat / MinValue getters added in
/// Phase 1A) and pass it in.
/// </summary>
public static class StatPreviewCalculator
{
    /// <summary>Snapshot of the six secondary stats — the values that drive every
    /// tertiary computation. Immutable, struct-by-value.</summary>
    public readonly struct Snapshot
    {
        public readonly float Strength;
        public readonly float Agility;
        public readonly float Dexterity;
        public readonly float Intelligence;
        public readonly float Endurance;
        public readonly float Charisma;

        public Snapshot(float strength, float agility, float dexterity,
                        float intelligence, float endurance, float charisma)
        {
            Strength = strength;
            Agility = agility;
            Dexterity = dexterity;
            Intelligence = intelligence;
            Endurance = endurance;
            Charisma = charisma;
        }

        /// <summary>Return a copy with +1 on the bumped secondary. Other stats unchanged.</summary>
        public Snapshot With(StatType bumped)
        {
            switch (bumped)
            {
                case StatType.Strength:     return new Snapshot(Strength + 1, Agility, Dexterity, Intelligence, Endurance, Charisma);
                case StatType.Agility:      return new Snapshot(Strength, Agility + 1, Dexterity, Intelligence, Endurance, Charisma);
                case StatType.Dexterity:    return new Snapshot(Strength, Agility, Dexterity + 1, Intelligence, Endurance, Charisma);
                case StatType.Intelligence: return new Snapshot(Strength, Agility, Dexterity, Intelligence + 1, Endurance, Charisma);
                case StatType.Endurance:    return new Snapshot(Strength, Agility, Dexterity, Intelligence, Endurance + 1, Charisma);
                case StatType.Charisma:     return new Snapshot(Strength, Agility, Dexterity, Intelligence, Endurance, Charisma + 1);
                default:                    return this;
            }
        }

        public float Get(StatType linked)
        {
            switch (linked)
            {
                case StatType.Strength:     return Strength;
                case StatType.Agility:      return Agility;
                case StatType.Dexterity:    return Dexterity;
                case StatType.Intelligence: return Intelligence;
                case StatType.Endurance:    return Endurance;
                case StatType.Charisma:     return Charisma;
                default:                    return 0f;
            }
        }
    }

    /// <summary>Per-tertiary scaling triple: which secondary drives it, by how much,
    /// what base offset to add, and the minimum clamp.</summary>
    public readonly struct ScalingEntry
    {
        public readonly StatType LinkedSecondary;
        public readonly float Multiplier;
        public readonly float BaseOffset;
        public readonly float MinValue;

        public ScalingEntry(StatType linked, float multiplier, float baseOffset, float minValue)
        {
            LinkedSecondary = linked;
            Multiplier = multiplier;
            BaseOffset = baseOffset;
            MinValue = minValue;
        }
    }

    /// <summary>Mutable dictionary of (derived stat) -> (linked, multiplier, baseOffset, minValue).
    /// Built per-character from the live CharacterTertiaryStats getters and passed to PreviewPlusOne.</summary>
    public sealed class ScalingTable
    {
        private readonly Dictionary<StatType, ScalingEntry> _entries = new Dictionary<StatType, ScalingEntry>();

        public void Set(StatType derived, StatType linked, float multiplier, float baseOffset, float minValue)
            => _entries[derived] = new ScalingEntry(linked, multiplier, baseOffset, minValue);

        public bool TryGet(StatType derived, out ScalingEntry entry)
            => _entries.TryGetValue(derived, out entry);

        public IEnumerable<KeyValuePair<StatType, ScalingEntry>> All => _entries;
    }

    /// <summary>One tertiary's before/after pair for a +1 hypothetical on a secondary.</summary>
    public readonly struct PreviewLine
    {
        public readonly StatType DerivedStat;
        public readonly float Before;
        public readonly float After;

        public PreviewLine(StatType derivedStat, float before, float after)
        {
            DerivedStat = derivedStat;
            Before = before;
            After = after;
        }
    }

    /// <summary>For each tertiary in <paramref name="scaling"/>, compute its current value
    /// from <paramref name="before"/> and its hypothetical value if the player put +1 on
    /// <paramref name="bumped"/>.</summary>
    public static IEnumerable<PreviewLine> PreviewPlusOne(Snapshot before, StatType bumped, ScalingTable scaling)
    {
        var after = before.With(bumped);
        foreach (var kv in scaling.All)
        {
            float b = Compute(kv.Value, before);
            float a = Compute(kv.Value, after);
            yield return new PreviewLine(kv.Key, b, a);
        }
    }

    /// <summary>Mirror of CharacterTertiaryStats.UpdateFromLinkedStat:
    /// value = max(minValue, baseOffset + linkedValue * multiplier).</summary>
    public static float Compute(ScalingEntry entry, Snapshot s)
    {
        float linkedValue = s.Get(entry.LinkedSecondary);
        return Mathf.Max(entry.MinValue, entry.BaseOffset + linkedValue * entry.Multiplier);
    }
}
