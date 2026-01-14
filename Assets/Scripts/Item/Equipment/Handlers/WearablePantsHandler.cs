using UnityEngine;

public class WearablePantsHandler : WearableHandlerBase
{
    [Header("Pants Parts")]
    [SerializeField] private GameObject _hips;
    [SerializeField] private GameObject _buttR, _buttL;
    [SerializeField] private GameObject _thighR, _thighL;
    [SerializeField] private GameObject _shinR, _shinL;

    protected override GameObject[] GetAllParts() => new[] {
        _hips, _buttR, _buttL, _thighR, _thighL, _shinR, _shinL
    };
}