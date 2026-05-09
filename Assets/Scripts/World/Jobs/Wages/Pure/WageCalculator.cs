using UnityEngine;

namespace MWI.Jobs.Wages
{
    /// <summary>
    /// Pure-logic wage math. No Unity dependencies outside Mathf.Clamp01.
    /// Kept deliberately small so it can be unit-tested in an EditMode assembly.
    /// </summary>
    public static class WageCalculator
    {
        /// <summary>
        /// Piece-work wage: (shiftRatio * minimumShiftWage) + (pieceRate * shiftUnits).
        /// Minimum component is attendance-prorated. Piece bonus is not.
        /// </summary>
        public static int ComputePieceWorkWage(
            float hoursWorked, float scheduledShiftHours,
            int minimumShiftWage, int pieceRate, int shiftUnits)
        {
            float ratio = ComputeShiftRatio(hoursWorked, scheduledShiftHours);
            return Mathf.RoundToInt(ratio * minimumShiftWage) + (pieceRate * shiftUnits);
        }

        /// <summary>
        /// Fixed-wage wage: shiftRatio * fixedShiftWage. Fully attendance-prorated.
        /// </summary>
        public static int ComputeFixedWage(
            float hoursWorked, float scheduledShiftHours, int fixedShiftWage)
        {
            float ratio = ComputeShiftRatio(hoursWorked, scheduledShiftHours);
            return Mathf.RoundToInt(ratio * fixedShiftWage);
        }

        /// <summary>
        /// Clamped attendance ratio. Caps at 1.0 — no overtime bonus.
        /// Safe if scheduledShiftHours &lt;= 0 (returns 0).
        /// </summary>
        public static float ComputeShiftRatio(float hoursWorked, float scheduledShiftHours)
        {
            if (scheduledShiftHours <= 0f) return 0f;
            return Mathf.Clamp01(hoursWorked / scheduledShiftHours);
        }
    }
}
