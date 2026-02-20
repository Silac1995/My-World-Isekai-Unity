using UnityEngine;

[CreateAssetMenu(fileName = "BarehandsStyleSO", menuName = "Scriptable Objects/Combat Style/Barehands Style")]
public class BarehandsStyleSO : CombatStyleSO
{
    public override WeaponType WeaponType => WeaponType.Barehands;

    [Header("Debug Info")]
    [SerializeField, TextArea] private string _info;

    private void OnValidate()
    {
        _info = "Weapon Type: " + WeaponType.ToString();
    }
}
