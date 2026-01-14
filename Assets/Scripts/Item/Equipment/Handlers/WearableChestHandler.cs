using UnityEngine;
using UnityEngine.U2D.Animation;

public class WearableChestHandler : WearableHandlerBase
{
    [Header("Chest Parts")]
    [SerializeField] private GameObject _torso;
    [SerializeField] private GameObject _torsoBehind;
    [SerializeField] private GameObject _upperarmR, _upperarmL;
    [SerializeField] private GameObject _forearmR, _forearmL;
    [SerializeField] private GameObject _breastR, _breastL;

    protected override GameObject[] GetAllParts() => new[] {
        _torso, _torsoBehind, _upperarmR, _upperarmL,
        _forearmR, _forearmL, _breastR, _breastL
    };
}