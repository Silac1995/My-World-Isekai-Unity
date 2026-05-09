using UnityEngine;

[CreateAssetMenu(fileName = "SwordStyleSO", menuName = "Scriptable Objects/Combat Style/Sword Style")]
public class SwordStyleSO : MeleeCombatStyleSO
{
    public override WeaponType WeaponType => WeaponType.Sword;

    [Header("Debug Info")]
    [SerializeField, TextArea] private string _info;

    private void OnValidate()
    {
        _info = "Weapon Type: " + WeaponType.ToString();
    }
}
