using Unity.Netcode;
using UnityEngine;

public class MapTransitionDoor : InteractableObject
{
    [Header("Transition Settings")]
    public string TargetMapId;
    public Transform TargetSpawnPoint; // Alternatively use a direct coordinate if preferred
    public Vector3 TargetPositionOffset; 
    public float FadeDuration = 0.5f;

    public override void Interact(Character interactor)
    {
        if (interactor == null || !interactor.IsAlive()) return;

        // Ensure we don't start the action if we are already transitioning
        if (interactor.CharacterActions.CurrentAction is CharacterMapTransitionAction)
        {
            return;
        }

        Vector3 dest = TargetSpawnPoint != null ? TargetSpawnPoint.position : transform.position + TargetPositionOffset;

        Debug.Log($"<color=cyan>[MapTransitionDoor]</color> {GetType().Name} '{name}' Interact: TargetMapId='{TargetMapId}', doorPos={transform.position}, TargetSpawnPoint={(TargetSpawnPoint != null ? TargetSpawnPoint.name : "null")}, TargetPositionOffset={TargetPositionOffset}, dest={dest}");

        var transitionAction = new CharacterMapTransitionAction(interactor, this, TargetMapId, dest, FadeDuration);
        interactor.CharacterActions.ExecuteAction(transitionAction);
    }
}
