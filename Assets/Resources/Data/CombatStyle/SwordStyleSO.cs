using UnityEngine;

[CreateAssetMenu(fileName = "SwordStyleSO", menuName = "Scriptable Objects/Animation/Sword Style")]
public class SwordStyleSO : CombatStyleSO
{
    // C'est la source de vérité pour ton code
    public override WeaponType WeaponType => WeaponType.Sword;

    [Header("Debug Info")]
    [SerializeField, TextArea] private string _info;

    // Cette méthode est appelée automatiquement par Unity quand tu 
    // crées l'objet ou que tu modifies une valeur dans l'inspecteur.
    private void OnValidate()
    {
        _info = "Weapon Type: " + WeaponType.ToString();
    }
}