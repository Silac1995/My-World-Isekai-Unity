using UnityEngine;

[CreateAssetMenu(fileName = "SwordMasterySet", menuName = "Scriptable Objects/Mastery/Animation Set")]
public class MasteryAnimationSetSO : ScriptableObject
{
    public AnimatorOverrideController overrideController;
    // Contient les clips spécifiques : Idle_Sword, Run_Sword, Attack_Sword...
}