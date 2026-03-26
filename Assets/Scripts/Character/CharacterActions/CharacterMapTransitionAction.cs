using UnityEngine;
using Unity.Collections;

public class CharacterMapTransitionAction : CharacterAction
{
    private readonly Character _character;
    private readonly MapTransitionDoor _door;
    private readonly string _targetMapId;
    private readonly Vector3 _targetPosition;
    private readonly float _fadeDuration;

    public CharacterMapTransitionAction(Character character, MapTransitionDoor door, string targetMapId, Vector3 targetPosition, float fadeDuration)
        : base(character, duration: fadeDuration)
    {
        _character = character;
        _door = door;
        _targetMapId = targetMapId;
        _targetPosition = targetPosition;
        _fadeDuration = fadeDuration;
    }

    public override void OnStart()
    {
        // Stop the character to prevent walking away during transition
        // NOTE: CharacterMovement may be on a child GameObject — use GetComponentInChildren.
        CharacterMovement movement = _character.GetComponentInChildren<CharacterMovement>();
        if (movement != null)
        {
            movement.Stop();
        }

        // Fade to black for the local player (hidden behind fade, NPCs/remote players skip)
        if (_character.IsOwner || _character.IsLocalPlayer)
        {
            ScreenFadeManager.Instance?.FadeOut(_fadeDuration * 0.5f);
        }
    }

    public override void OnApplyEffect()
    {
        Debug.Log($"<color=cyan>[MapTransition]</color> OnApplyEffect: IsOwner={_character.IsOwner}, IsLocalPlayer={_character.IsLocalPlayer}, IsServer={_character.IsServer}, targetMapId='{_targetMapId}', targetPos={_targetPosition}");

        // Prediction: Client warps instantly for seamless feel
        if (_character.IsOwner || _character.IsLocalPlayer)
        {
            // Only predict warp if we know the real position (repeat visits).
            // On first visit, _targetPosition is Vector3.zero — let the server
            // resolve the correct interior position and warp via WarpClientRpc.
            if (_targetPosition != Vector3.zero)
            {
                Debug.Log($"<color=cyan>[MapTransition]</color> Client predicting ForceWarp to {_targetPosition}");
                CharacterMovement movement = _character.GetComponentInChildren<CharacterMovement>();
                if (movement != null)
                {
                    movement.ForceWarp(_targetPosition);
                }
            }
            else
            {
                Debug.Log("<color=cyan>[MapTransition]</color> First visit — skipping client warp, waiting for server WarpClientRpc.");
            }

            // Send authoritative request cleanly via separated Tracker component
            if (_character.TryGetComponent(out CharacterMapTracker tracker))
            {
                Debug.Log($"<color=cyan>[MapTransition]</color> Sending RequestTransitionServerRpc('{_targetMapId}', {_targetPosition})");
                tracker.RequestTransitionServerRpc(_targetMapId, _targetPosition);
            }
            else
            {
                Debug.LogError($"[MapSystem] {_character.name} missing CharacterMapTracker for ServerRpc!");
            }

            // Fade back in after warp
            ScreenFadeManager.Instance?.FadeIn(_fadeDuration * 0.5f);
        }
        else if (_character.IsServer) // NPC logic, no prediction needed, just direct mutate
        {
            CharacterMovement movement = _character.GetComponentInChildren<CharacterMovement>();
            if (movement != null)
            {
                movement.ForceWarp(_targetPosition);
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
        CharacterMovement movement = _character.GetComponentInChildren<CharacterMovement>();
        if (movement != null)
        {
            movement.Resume();
        }

        // Cancel any active fade if the action was interrupted
        if (_character.IsOwner || _character.IsLocalPlayer)
        {
            ScreenFadeManager.Instance?.FadeIn(0.1f);
        }
    }
}
