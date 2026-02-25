using UnityEngine;

/// <summary>
/// Meuble de type siège (chaise, tabouret, banc, trône...).
/// Un personnage peut s'y asseoir.
/// </summary>
public class ChairFurniture : Furniture
{
    [Header("Chair")]
    [SerializeField] private Transform _seatPoint;
    [SerializeField] private bool _faceForward = true;

    /// <summary>
    /// Position exacte où le personnage s'assoit.
    /// Si non défini, utilise le point d'interaction du meuble.
    /// </summary>
    public Vector3 SeatPosition => _seatPoint != null ? _seatPoint.position : GetInteractionPosition();

    /// <summary>
    /// Direction vers laquelle le personnage regarde une fois assis.
    /// </summary>
    public Vector3 SeatForward => _seatPoint != null ? _seatPoint.forward : transform.forward;
    public bool FaceForward => _faceForward;
}
