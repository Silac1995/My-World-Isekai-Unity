using UnityEngine;

/// <summary>
/// Seat-type furniture (chair, stool, bench, throne...).
/// A character can sit on it. Single-occupant — inherits the base
/// <see cref="OccupiableFurniture.Use"/> / <see cref="OccupiableFurniture.Release"/>
/// directly (no per-slot list like <see cref="BedFurniture"/>).
/// </summary>
public class ChairFurniture : OccupiableFurniture
{
    [Header("Chair")]
    [SerializeField] private Transform _seatPoint;
    [SerializeField] private bool _faceForward = true;

    /// <summary>
    /// Exact position where the character sits.
    /// If not defined, uses the furniture's interaction point.
    /// </summary>
    public Vector3 SeatPosition => _seatPoint != null ? _seatPoint.position : GetInteractionPosition();

    /// <summary>
    /// Direction the character faces once seated.
    /// </summary>
    public Vector3 SeatForward => _seatPoint != null ? _seatPoint.forward : transform.forward;
    public bool FaceForward => _faceForward;
}
