using System;

namespace MWI.Jobs.Wages
{
    /// <summary>
    /// Pure-logic deficit-bounded credit for harvester deposits.
    /// Credit = clamp(depositQty, 0, deficitBefore).
    /// Excess deposits do not earn bonus pay.
    /// </summary>
    public static class HarvesterCreditCalculator
    {
        public static int GetCreditedAmount(int depositQty, int deficitBefore)
        {
            if (depositQty <= 0) return 0;
            if (deficitBefore <= 0) return 0;
            return Math.Min(depositQty, deficitBefore);
        }
    }
}
