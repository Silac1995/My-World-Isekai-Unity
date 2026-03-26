using Unity.Netcode;
using UnityEngine;
using MWI.WorldSystem;

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

        // --- Door Lock / Broken Check ---
        DoorLock doorLock = GetComponent<DoorLock>();
        DoorHealth doorHealth = GetComponent<DoorHealth>();

        // Broken doors are always passable (lock bypassed)
        bool isBroken = doorHealth != null && doorHealth.IsSpawned && doorHealth.IsBroken.Value;

        if (!isBroken && doorLock != null && doorLock.IsSpawned && doorLock.IsLocked.Value)
        {
            // Check if interactor has a matching key
            KeyInstance key = interactor.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);
            if (key != null)
            {
                // Unlock the door (don't walk through yet)
                if (doorLock.IsSpawned)
                    doorLock.RequestUnlockServerRpc();
                return;
            }
            else
            {
                // No key — jiggle + feedback
                if (doorLock.IsSpawned)
                    doorLock.RequestJiggleServerRpc();
                return;
            }
        }

        string targetMapId = TargetMapId;
        Vector3 dest = TargetSpawnPoint != null ? TargetSpawnPoint.position : transform.position + TargetPositionOffset;

        // If this door has no TargetMapId (e.g. remote client where server-set fields didn't replicate),
        // resolve exit info from the parent MapController's replicated NetworkVariables.
        // NOTE: We cannot check MapController.IsInteriorOffset here — it's a plain bool that
        // doesn't replicate via NGO. Instead, we check if ExteriorMapId is non-empty (only set
        // on interior MapControllers by BuildingInteriorSpawner).
        if (string.IsNullOrEmpty(targetMapId))
        {
            // Try parent first (MapController on root, door is a child)
            var parentMap = GetComponentInParent<MapController>();
            // Fallback: MapController might be a sibling — search from the root
            if (parentMap == null)
            {
                parentMap = transform.root.GetComponentInChildren<MapController>();
            }

            if (parentMap != null)
            {
                string exteriorMap = parentMap.ExteriorMapId.Value.ToString();
                if (!string.IsNullOrEmpty(exteriorMap))
                {
                    targetMapId = exteriorMap;
                    dest = parentMap.ExteriorReturnPosition.Value;
                    Debug.Log($"<color=cyan>[MapTransitionDoor]</color> Resolved exit from MapController NetworkVars: targetMapId='{targetMapId}', dest={dest}");
                }
                else
                {
                    Debug.LogWarning($"<color=orange>[MapTransitionDoor]</color> Found MapController '{parentMap.MapId}' but ExteriorMapId is empty. NetworkVariable may not have replicated yet.");
                }
            }
            else
            {
                Debug.LogWarning($"<color=orange>[MapTransitionDoor]</color> No MapController found in hierarchy for exit door '{name}'.");
            }
        }

        // Guard: don't start a transition with no target — prevents empty ServerRpc spam
        if (string.IsNullOrEmpty(targetMapId))
        {
            Debug.LogWarning($"<color=orange>[MapTransitionDoor]</color> '{name}' has no TargetMapId after resolution. Aborting transition.");
            return;
        }

        Debug.Log($"<color=cyan>[MapTransitionDoor]</color> {GetType().Name} '{name}' Interact: TargetMapId='{targetMapId}', doorPos={transform.position}, TargetSpawnPoint={(TargetSpawnPoint != null ? TargetSpawnPoint.name : "null")}, TargetPositionOffset={TargetPositionOffset}, dest={dest}");

        var transitionAction = new CharacterMapTransitionAction(interactor, this, targetMapId, dest, FadeDuration);
        interactor.CharacterActions.ExecuteAction(transitionAction);
    }

    public override System.Collections.Generic.List<InteractionOption> GetHoldInteractionOptions(Character interactor)
    {
        var options = new System.Collections.Generic.List<InteractionOption>();

        DoorLock doorLock = GetComponent<DoorLock>();
        DoorHealth doorHealth = GetComponent<DoorHealth>();

        // Lock/Unlock options (requires matching key)
        if (doorLock != null && doorLock.IsSpawned)
        {
            bool isBroken = doorHealth != null && doorHealth.IsBroken.Value;
            KeyInstance key = interactor.CharacterEquipment?.FindKeyForLock(doorLock.LockId, doorLock.RequiredTier);

            if (key != null && !isBroken)
            {
                if (doorLock.IsLocked.Value)
                {
                    options.Add(new InteractionOption
                    {
                        Name = "Unlock",
                        Action = () => doorLock.RequestUnlockServerRpc()
                    });
                }
                else
                {
                    options.Add(new InteractionOption
                    {
                        Name = "Lock",
                        Action = () => doorLock.RequestLockServerRpc()
                    });
                }
            }
        }

        // Repair option (broken door)
        if (doorHealth != null && doorHealth.IsSpawned && doorHealth.IsBroken.Value)
        {
            options.Add(new InteractionOption
            {
                Name = "Repair",
                Action = () => doorHealth.RequestRepairServerRpc()
            });
        }

        return options.Count > 0 ? options : null;
    }
}
