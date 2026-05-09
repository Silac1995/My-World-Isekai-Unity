using UnityEngine;

/// <summary>
/// One slot on a <see cref="BedFurniture"/>. The Anchor transform defines where the
/// occupying Character is snapped (position + rotation) when sleeping. Occupant /
/// ReservedBy are runtime-only (not serialized) — set internally by <see cref="BedFurniture"/>.
/// </summary>
[System.Serializable]
public class BedSlot
{
    [Tooltip("Authored child transform. Sets the position + rotation the sleeping character is snapped to.")]
    [SerializeField] private Transform _anchor;

    public Transform Anchor => _anchor;
    public Character Occupant { get; internal set; }
    public Character ReservedBy { get; internal set; }
    public bool IsFree => Occupant == null && ReservedBy == null;
}
