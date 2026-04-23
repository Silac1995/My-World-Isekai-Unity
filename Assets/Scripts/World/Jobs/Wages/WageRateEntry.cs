using System;
using UnityEngine;
using MWI.WorldSystem;

[Serializable]
public class WageRateEntry
{
    public JobType JobType;
    [Tooltip("Coins per shift unit (piece-work jobs only). 0 for fixed-wage jobs.")]
    public int PieceRate;
    [Tooltip("Floor minimum wage per full shift (piece-work jobs only). Prorated by attendance. 0 for fixed-wage jobs.")]
    public int MinimumShiftWage;
    [Tooltip("Fixed wage per full shift (shop/vendor/barman/server/logistics manager). Prorated by attendance. 0 for piece-work jobs.")]
    public int FixedShiftWage;
}
