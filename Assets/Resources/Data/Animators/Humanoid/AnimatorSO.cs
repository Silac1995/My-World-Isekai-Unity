using UnityEngine;

[CreateAssetMenu(fileName = "AnimatorSO", menuName = "Scriptable Objects/Animation/Animation Set")]
public class MasteryAnimationSetSO : ScriptableObject
{
    public AnimatorOverrideController overrideController;
    // Contient les clips spécifiques : Idle_Sword, Run_Sword, Attack_Sword...
}