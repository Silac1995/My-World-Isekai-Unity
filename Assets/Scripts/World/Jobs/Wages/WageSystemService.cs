using MWI.Economy;
using MWI.Jobs.Wages;
using MWI.WorldSystem;
using UnityEngine;

/// <summary>
/// Singleton orchestrator for wage payment. Owns the active IWagePayer and the
/// WageRatesSO defaults asset. Computes shift wages and forwards to the payer.
/// </summary>
public class WageSystemService : MonoBehaviour
{
    public static WageSystemService Instance { get; private set; }

    [SerializeField] private WageRatesSO _defaultRates;
    [Tooltip("If true, use MintedWagePayer (v1 default). Future: set false and inject a different payer in Awake/Inspector.")]
    [SerializeField] private bool _useMintedPayer = true;

    private IWagePayer _payer;

    public WageRatesSO DefaultRates => _defaultRates;
    public IWagePayer Payer => _payer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError($"[WageSystemService] Duplicate instance detected on '{name}'. Destroying.");
            Destroy(this);
            return;
        }
        Instance = this;

        if (_useMintedPayer)
        {
            _payer = new MintedWagePayer();
        }
        // else: leave _payer null — caller must set via SetPayer() before any wage payment.
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Optional manual payer injection (for tests, future BuildingTreasuryWagePayer, etc.).
    /// </summary>
    public void SetPayer(IWagePayer payer)
    {
        _payer = payer;
    }

    /// <summary>
    /// Copies WageRatesSO defaults into a freshly-created JobAssignment.
    /// Called at hire time (Task 18). Safe to call when _defaultRates is null
    /// or no entry exists — assignment fields stay at zero.
    /// </summary>
    public void SeedAssignmentDefaults(JobAssignment assignment)
    {
        if (assignment == null) { Debug.LogError("[WageSystemService] SeedAssignmentDefaults: null assignment."); return; }
        if (_defaultRates == null) { Debug.LogWarning("[WageSystemService] SeedAssignmentDefaults: no WageRatesSO assigned — assignment will have zero wage."); return; }
        var jobType = assignment.AssignedJob != null ? assignment.AssignedJob.Type : JobType.None;
        var entry = _defaultRates.GetDefaults(jobType);
        if (entry == null)
        {
            Debug.LogWarning($"[WageSystemService] No WageRateEntry for {jobType} — assignment will have zero wage.");
            return;
        }
        assignment.PieceRate = entry.PieceRate;
        assignment.MinimumShiftWage = entry.MinimumShiftWage;
        assignment.FixedShiftWage = entry.FixedShiftWage;
        // Currency stays at CurrencyId.Default — future Kingdom resolves from building ownership.
        assignment.Currency = CurrencyId.Default;
    }

    /// <summary>
    /// Computes the wage for a finalized shift and pays the worker.
    /// Returns the wage amount paid (zero if no payment was due).
    /// </summary>
    public int ComputeAndPayShiftWage(
        Character worker, JobAssignment assignment, ShiftSummary summary,
        float scheduledShiftHours, float hoursWorked, CurrencyId paymentCurrency)
    {
        if (worker == null || assignment == null || summary == null)
        {
            Debug.LogError("[WageSystemService] ComputeAndPayShiftWage: null arg (worker/assignment/summary).");
            return 0;
        }
        if (_payer == null)
        {
            Debug.LogError("[WageSystemService] ComputeAndPayShiftWage: no IWagePayer configured. Wage discarded.");
            return 0;
        }

        int wage = 0;
        if (IsPieceWorkJob(assignment.AssignedJob != null ? assignment.AssignedJob.Type : JobType.None))
        {
            wage = WageCalculator.ComputePieceWorkWage(
                hoursWorked, scheduledShiftHours,
                assignment.MinimumShiftWage, assignment.PieceRate, summary.ShiftUnits);
        }
        else
        {
            wage = WageCalculator.ComputeFixedWage(
                hoursWorked, scheduledShiftHours, assignment.FixedShiftWage);
        }

        if (wage > 0)
        {
            string source = $"shift:{(assignment.AssignedJob != null ? assignment.AssignedJob.Type.ToString() : "Unknown")}@{(assignment.Workplace != null ? assignment.Workplace.name : "<no-place>")}";
            _payer.PayWages(worker, paymentCurrency, wage, source);
        }
        return wage;
    }

    private static bool IsPieceWorkJob(JobType jobType)
    {
        // Piece-work: Harvester family + Crafter + Transporter + Blacksmith.
        switch (jobType)
        {
            case JobType.Woodcutter:
            case JobType.Miner:
            case JobType.Forager:
            case JobType.Farmer:
            case JobType.Crafter:
            case JobType.Transporter:
            case JobType.Blacksmith:
            case JobType.BlacksmithApprentice:
                return true;
            default:
                // Vendor, Server, Barman, LogisticsManager, None — fixed-wage or non-paying.
                return false;
        }
    }
}
