using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Instant action: the interactor attempts to tame a target CharacterAnimal.
/// Per rule 22, the effect lives here — the UI/NPC AI only queues the action.
/// Runs server-authoritative: if executed on a non-server caller, dispatches
/// through CharacterAnimal.RequestTameServerRpc (Task 8).
/// </summary>
public class CharacterTameAction : CharacterAction
{
    private readonly Character _target;

    public override bool ShouldPlayGenericActionAnimation => false;
    public override string ActionName => "Tame";

    public CharacterTameAction(Character interactor, Character target)
        : base(interactor, duration: 0f)
    {
        _target = target;
    }

    public override bool CanExecute()
    {
        if (_target == null)
        {
            Debug.LogWarning($"[CharacterTameAction] {character?.CharacterName} — null target.");
            return false;
        }

        if (!_target.TryGet<CharacterAnimal>(out var animal))
        {
            Debug.LogWarning($"[CharacterTameAction] Target '{_target.CharacterName}' has no CharacterAnimal.");
            return false;
        }

        if (!animal.IsTameable || animal.IsTamed)
        {
            Debug.Log($"[CharacterTameAction] Target '{_target.CharacterName}' is not currently tameable.");
            return false;
        }

        if (_target == character)
        {
            Debug.LogWarning($"[CharacterTameAction] {character.CharacterName} cannot tame themselves.");
            return false;
        }

        return true;
    }

    public override void OnStart()
    {
        Debug.Log($"<color=cyan>[Tame]</color> {character.CharacterName} attempts to tame {_target.CharacterName}.");
    }

    public override void OnApplyEffect()
    {
        if (_target == null)
        {
            Debug.LogWarning($"[CharacterTameAction] Target vanished before effect on {character?.CharacterName}.");
            return;
        }

        if (!_target.TryGet<CharacterAnimal>(out var animal))
        {
            Debug.LogWarning($"[CharacterTameAction] Target '{_target.CharacterName}' lost its CharacterAnimal component mid-action.");
            return;
        }

        // Route to server. If we're already on the server, call directly;
        // otherwise go through the ServerRpc.
        NetworkObject interactorNetObj = character != null ? character.NetworkObject : null;
        if (interactorNetObj == null || !interactorNetObj.IsSpawned)
        {
            Debug.LogError($"[CharacterTameAction] Interactor has no spawned NetworkObject — cannot route tame.");
            return;
        }

        if (animal.IsServer)
        {
            animal.TryTameOnServer(new NetworkObjectReference(interactorNetObj));
        }
        else
        {
            animal.RequestTameServerRpc(new NetworkObjectReference(interactorNetObj));
        }
    }
}
