using UnityEngine;
using Unity.Collections;

public class CharacterMapTransitionAction : CharacterAction
{
    private readonly Character _character;
    private readonly MapTransitionDoor _door;
    private readonly string _targetMapId;
    private readonly Vector3 _targetPosition;

    public CharacterMapTransitionAction(Character character, MapTransitionDoor door, string targetMapId, Vector3 targetPosition, float fadeDuration) 
        : base(character, duration: fadeDuration)
    {
        _character = character;
        _door = door;
        _targetMapId = targetMapId;
        _targetPosition = targetPosition;
    }

    public override void OnStart()
    {
        // Stop the character to prevent walking away during transition
        if (_character.TryGetComponent(out CharacterMovement movement))
        {
            movement.Stop();
        }
    }

    public override void OnApplyEffect()
    {
        // Prediction: Client warps instantly for seamless feel
        if (_character.IsOwner || _character.IsLocalPlayer)
        {
            if (_character.TryGetComponent(out CharacterMovement movement))
            {
                movement.Warp(_targetPosition);
            }
            
            // Send authoritative request cleanly via separated Tracker component
            if (_character.TryGetComponent(out CharacterMapTracker tracker))
            {
                tracker.RequestTransitionServerRpc(_targetMapId, _targetPosition);
            }
            else 
            {
                Debug.LogError($"[MapSystem] {_character.name} missing CharacterMapTracker for ServerRpc!");
            }
        }
        else if (_character.IsServer) // NPC logic, no prediction needed, just direct mutate
        {
            if (_character.TryGetComponent(out CharacterMovement movement))
            {
                movement.Warp(_targetPosition);
            }
            
            if (_character.TryGetComponent(out CharacterMapTracker tracker))
            {
                tracker.SetCurrentMap(_targetMapId);
            }
        }
    }

    public override void OnCancel()
    {
        // Restore movement if interrupted (e.g., attacked)
        if (_character.TryGetComponent(out CharacterMovement movement))
        {
            movement.Resume();
        }
    }
}
