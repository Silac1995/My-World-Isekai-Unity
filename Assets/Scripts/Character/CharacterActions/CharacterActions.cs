using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class CharacterActions : CharacterSystem
{
    public Action<CharacterAction> OnActionStarted;
    public Action OnActionFinished;
    private float _actionStartTime; // Pour calculer la progression

    private CharacterAction _currentAction;
    private Coroutine _actionRoutine; // Référence pour éviter les accumulations

    public CharacterAction CurrentAction => _currentAction;

    public float GetActionProgress()
    {
        if (_currentAction == null) return 0f;
        // Continuous actions have no fixed Duration — they expose their own Progress
        // (e.g., CharacterAction_FinishConstruction returns Building.ConstructionProgress).
        if (_currentAction is CharacterAction_Continuous c) return Mathf.Clamp01(c.Progress);
        if (_currentAction.Duration <= 0) return 0f;
        float elapsed = Time.time - _actionStartTime;
        return Mathf.Clamp01(elapsed / _currentAction.Duration);
    }

    public bool ExecuteAction(CharacterAction action)
    {
        if (action == null || _currentAction != null) return false;
        if (!action.CanExecute()) return false;

        // Owner/Client -> Server Intent (Only if it's the real action, not a proxy)
        if (!IsServer && IsOwner && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Ask the server to broadcast visuals to everyone else
            NotifyActionStartedServerRpc(action.ShouldPlayGenericActionAnimation, action.Duration, action.ActionName);
        }

        if (IsServer && !(action is CharacterVisualProxyAction) && !action.IsReplicatedInternally)
        {
            // Server broadcasts the visual proxy to all clients.
            // Continuous actions have Duration=0 — broadcasting that would make the visual
            // proxy fire-and-finish in one frame, leaving the client's character with no
            // visible animation while the server-side action ticks. Use a long sentinel
            // (600s = 10 min) so the proxy stays as _currentAction on every peer until
            // either (a) the server-side action ends + ContinuousActionEndedClientRpc fires
            // a cleanup, or (b) the timer naturally expires (failsafe).
            float broadcastDuration = (action is CharacterAction_Continuous) ? 600f : action.Duration;
            BroadcastActionVisualsClientRpc(action.ShouldPlayGenericActionAnimation, broadcastDuration, action.ActionName);
        }

        _currentAction = action;
        _actionStartTime = Time.time;
        _currentAction.OnActionFinished += CleanupAction;

        OnActionStarted?.Invoke(_currentAction);

        // 1. On lance l'initialisation de l'action
        _currentAction.OnStart();

        // 2. GESTION DU FLUX (Instantané vs Temporisé vs Continu)
        if (action is CharacterAction_Continuous continuous)
        {
            if (!IsServer)
            {
                // Continuous actions are server-only. Clients should not start them locally —
                // the canonical path is a ServerRpc that has the server queue the action
                // (e.g. Building.RequestStartFinishConstructionRpc). If we let the client
                // start the coroutine, OnTick is server-gated → it never advances → the
                // action is stuck in _currentAction forever (zombie).
                CleanupAction(); // _currentAction was just assigned at line 44 — release it.
                return false;
            }
            _actionRoutine = StartCoroutine(ActionContinuousTickRoutine(continuous));
        }
        else if (action.Duration <= 0)
        {
            try
            {
                if (IsServer || action is CharacterVisualProxyAction)
                    action.OnApplyEffect();
                else
                    action.OnApplyEffect();

                action.Finish();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur Action Instantanée: {e.Message}");
                CleanupAction();
            }
        }
        else
        {
            _actionRoutine = StartCoroutine(ActionTimerRoutine(_currentAction));
        }

        return true;
    }

    [Rpc(SendTo.Server)]
    private void NotifyActionStartedServerRpc(bool shouldPlayGeneric, float duration, string actionName)
    {
        // Server relays the visual proxy to all clients (excluding the owner who already predicted it)
        BroadcastActionVisualsClientRpc(shouldPlayGeneric, duration, actionName);
    }

    [Rpc(SendTo.Server)]
    public void RequestCraftServerRpc(string itemId, Color primaryColor, Color secondaryColor, Vector3 stationPosition)
    {
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO itemSO = System.Array.Find(allItems, match => match.ItemId == itemId);
        if (itemSO == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find ItemSO with ID '{itemId}'");
            return;
        }

        CraftingStation station = FindCraftingStationNear(stationPosition);
        if (station == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find CraftingStation near {stationPosition}");
            return;
        }

        station.Craft(itemSO, _character, primaryColor, secondaryColor);
    }

    // ────────────────────── Equipment-window verb bridge (2026-05-19) ──────────────────────
    // UI_CharacterEquipment click verbs route through this single RPC. The server validates
    // the sender owns this character, then enqueues the matching CharacterAction via the
    // standard ExecuteAction path so rule #22 (player↔NPC parity) holds — an NPC AI calling
    // ExecuteAction(new CharacterAction_EquipWearable(...)) directly server-side goes through
    // the exact same code below the dispatch switch.

    /// <summary>VerbId values match <c>MWI.UI.Equipment.EquipmentVerbId</c> on the UI side.
    /// Kept as byte constants here so this file does not take a using-dependency on the UI assembly.</summary>
    public const byte EQUIP_VERB_EQUIP         = 0;
    public const byte EQUIP_VERB_UNEQUIP       = 1;
    public const byte EQUIP_VERB_CARRY_IN_HAND = 2;
    public const byte EQUIP_VERB_STASH_IN_BAG  = 3;
    public const byte EQUIP_VERB_USE           = 4;
    public const byte EQUIP_VERB_UNEQUIP_BAG   = 5;
    public const byte EQUIP_VERB_DROP          = 6;

    /// <summary>SourceKind values match <see cref="EquipmentSourceKind"/>.</summary>
    public const byte EQUIP_SRC_BAG     = 0;
    public const byte EQUIP_SRC_WORN    = 1;
    public const byte EQUIP_SRC_WEAPON  = 2;
    public const byte EQUIP_SRC_HANDS   = 3;

    /// <summary>
    /// Player UI bridge: client-side equipment-window click fires this RPC; server validates
    /// ownership + enqueues the matching CharacterAction. All five new actions plus the
    /// drop / unequip-bag verbs are dispatched through this one entry-point.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestEquipmentVerbServerRpc(
        byte verbId,
        byte sourceKind,
        int bagIndex,
        int layer,
        int slot,
        RpcParams rpcParams = default)
    {
        // Anti-cheat: sender must own this character.
        if (_character == null) return;
        if (rpcParams.Receive.SenderClientId != _character.OwnerClientId)
        {
            Debug.LogWarning($"<color=orange>[CharacterActions]</color> RequestEquipmentVerbServerRpc rejected — sender {rpcParams.Receive.SenderClientId} does not own {_character.CharacterName} (owner {_character.OwnerClientId}).");
            return;
        }

        EquipmentSourceRef source = BuildSourceRef(sourceKind, bagIndex, layer, slot);

        switch (verbId)
        {
            case EQUIP_VERB_EQUIP:
                ExecuteAction(new CharacterAction_EquipWearable(_character, source.BagIndex));
                break;

            case EQUIP_VERB_UNEQUIP:
                if (source.Kind != EquipmentSourceKind.WornSlot)
                {
                    Debug.LogWarning("[CharacterActions] Unequip verb requires WornSlot source.");
                    return;
                }
                ExecuteAction(new CharacterAction_UnequipWearable(_character, source.Layer, source.Slot));
                break;

            case EQUIP_VERB_CARRY_IN_HAND:
                ExecuteAction(new CharacterAction_CarryInHand(_character, source));
                break;

            case EQUIP_VERB_STASH_IN_BAG:
                ExecuteAction(new CharacterAction_StashInBag(_character, source));
                break;

            case EQUIP_VERB_USE:
            {
                // Route through the existing CharacterUseConsumableAction (1.5s duration +
                // Trigger_Consume animation + Character.UseConsumable on end which handles
                // ApplyEffect + removal from hands/inventory). Resolve the source to a
                // ConsumableInstance — the action wrapper doesn't care which slot it's in.
                ItemInstance instance = ResolveSourceInstance(source);
                if (!(instance is ConsumableInstance consumable))
                {
                    if (NPCDebug.VerboseActions)
                        Debug.LogWarning($"<color=orange>[CharacterActions]</color> Use verb: source {source} resolved to non-consumable {instance?.ItemSO?.ItemName ?? "null"}.");
                    return;
                }
                ExecuteAction(new CharacterUseConsumableAction(_character, consumable));
                break;
            }

            case EQUIP_VERB_UNEQUIP_BAG:
                // No CharacterAction wrapper today — direct call preserves existing UnequipBag
                // semantics (drops whole bag with contents to the world).
                _character.CharacterEquipment?.UnequipBag();
                break;

            case EQUIP_VERB_DROP:
                // The contextual item to drop depends on the source kind. We resolve the
                // instance server-side from the source and queue the existing CharacterDropItem
                // action which handles the WorldItem spawn.
                ItemInstance toDrop = ResolveSourceInstance(source);
                if (toDrop == null)
                {
                    if (NPCDebug.VerboseActions)
                        Debug.LogWarning($"<color=orange>[CharacterActions]</color> Drop verb: source {source} resolved to null.");
                    return;
                }
                ExecuteAction(new CharacterDropItem(_character, toDrop));
                break;

            default:
                Debug.LogWarning($"<color=orange>[CharacterActions]</color> RequestEquipmentVerbServerRpc unknown verbId {verbId}.");
                break;
        }
    }

    private static EquipmentSourceRef BuildSourceRef(byte kind, int bagIndex, int layer, int slot)
    {
        switch (kind)
        {
            case EQUIP_SRC_BAG:    return EquipmentSourceRef.Bag(bagIndex);
            case EQUIP_SRC_WORN:   return EquipmentSourceRef.Worn((WearableLayerEnum)layer, (WearableType)slot);
            case EQUIP_SRC_WEAPON: return EquipmentSourceRef.Weapon();
            case EQUIP_SRC_HANDS:  return EquipmentSourceRef.Hands();
            default:               return EquipmentSourceRef.Bag(-1); // invalid
        }
    }

    private ItemInstance ResolveSourceInstance(EquipmentSourceRef source)
    {
        var equip = _character.CharacterEquipment;
        if (equip == null) return null;

        switch (source.Kind)
        {
            case EquipmentSourceKind.BagSlot:
            {
                var inv = equip.GetInventory();
                if (inv == null || source.BagIndex < 0 || source.BagIndex >= inv.ItemSlots.Count) return null;
                var bagSlot = inv.ItemSlots[source.BagIndex];
                return bagSlot.IsEmpty() ? null : bagSlot.ItemInstance;
            }
            case EquipmentSourceKind.WornSlot:
            {
                EquipmentLayer targetLayer = source.Layer switch
                {
                    WearableLayerEnum.Underwear => equip.UnderwearLayer,
                    WearableLayerEnum.Clothing  => equip.ClothingLayer,
                    WearableLayerEnum.Armor     => equip.ArmorLayer,
                    _ => null,
                };
                return targetLayer?.GetInstance(source.Slot);
            }
            case EquipmentSourceKind.ActiveWeapon:
                return equip.CurrentWeapon;
            case EquipmentSourceKind.HandsCarry:
            {
                var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
                return hands?.CarriedItem;
            }
            default:
                return null;
        }
    }

    // ────────────────────── Generic RPCs ──────────────────────

    /// <summary>
    /// Sent by the server to the owning client when a non-wearable WorldItem is picked up.
    /// The client reconstructs the ItemInstance from network data and adds it to
    /// inventory/hands locally. Wearables go through CharacterEquipAction on the server
    /// instead (they sync via NetworkList).
    /// </summary>
    [Rpc(SendTo.Owner)]
    public void ReceiveItemPickupClientRpc(NetworkItemData itemData)
    {
        if (itemData.ItemId.IsEmpty) return;

        string id = itemData.ItemId.ToString();
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == id);
        if (so == null)
        {
            Debug.LogError($"<color=red>[CharacterActions]</color> Client could not find ItemSO '{id}' for pickup.");
            return;
        }

        ItemInstance instance = so.CreateInstance();
        JsonUtility.FromJsonOverwrite(itemData.JsonData.ToString(), instance);
        instance.ItemSO = so;

        if (_character.CharacterEquipment != null)
            _character.CharacterEquipment.PickUpItem(instance);

        Debug.Log($"<color=green>[CharacterActions]</color> Client received item pickup: {so.ItemName}");
    }

    /// <summary>
    /// Sent by the server to the owning client after a store-to-furniture action has
    /// successfully added the item to a <see cref="StorageFurniture"/> on the server
    /// (which replicates via <see cref="StorageFurnitureNetworkSync"/>'s NetworkList).
    /// The owner must now remove the item from its local source — either a bag slot
    /// (<paramref name="sourceSlotIndex"/> &gt;= 0) or the hands controller
    /// (<paramref name="sourceSlotIndex"/> == -1).
    ///
    /// Inverse of <see cref="ReceiveItemPickupClientRpc"/>: used when bag-inventory
    /// contents flow from a client-authoritative source to a server-authoritative
    /// destination. Sized as one int because bag-inventory contents aren't in
    /// CharacterEquipment._networkEquipment — the client is the source of truth for
    /// its own bag/hands and only the server knows whether the chest accepted the item.
    ///
    /// Host path: loopback ClientRpc, removes from the host's own (server-side) bag/hands.
    /// </summary>
    [Rpc(SendTo.Owner)]
    public void RemoveFromInventoryAfterStoreClientRpc(int sourceSlotIndex)
    {
        if (sourceSlotIndex < 0)
        {
            // Hands path.
            var hands = _character.CharacterVisual?.BodyPartsController?.HandsController;
            if (hands != null && hands.IsCarrying)
            {
                hands.DropCarriedItem(); // clears hands + destroys held visual; does not spawn a WorldItem
            }
            return;
        }

        // Bag slot path.
        var equip = _character.CharacterEquipment;
        if (equip == null || !equip.HaveInventory()) return;
        var inv = equip.GetInventory();
        if (inv == null) return;
        if (sourceSlotIndex >= inv.ItemSlots.Count) return;
        var slot = inv.ItemSlots[sourceSlotIndex];
        if (slot == null || slot.IsEmpty() || slot.ItemInstance == null) return;

        // Use Inventory.RemoveItem so OnInventoryChanged fires (refreshes the UI panel
        // bag side and any weapon visuals on the bag).
        inv.RemoveItem(slot.ItemInstance, _character);
    }

    /// <summary>
    /// Generic server-side despawn for any NetworkObject.
    /// Used by CharacterPickUpItem and other actions that need to remove
    /// a networked object from a client context.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestDespawnServerRpc(NetworkObjectReference targetRef)
    {
        if (!targetRef.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[CharacterActions] Server: Could not resolve NetworkObject for despawn.");
            return;
        }

        if (netObj.IsSpawned)
            netObj.Despawn(true);
    }

    /// <summary>
    /// Client requests the server to run the harvest effect: run Harvest() on the target,
    /// spawn the resulting WorldItem, and register a pickup task for NPC workers.
    /// The target Harvestable is resolved by world position (Harvestable is a scene-authored
    /// MonoBehaviour, so its position is identical on server and client).
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestHarvestServerRpc(Vector3 harvestablePosition)
    {
        Harvestable target = FindHarvestableNear(harvestablePosition);
        if (target == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find Harvestable near {harvestablePosition}");
            return;
        }

        ApplyHarvestOnServer(target);
    }

    /// <summary>
    /// Server-side (or offline) harvest execution. Runs the Harvest, spawns the WorldItem,
    /// and registers a pickup task for NPC workers. Called from:
    ///   - RequestHarvestServerRpc (networked client-owner path)
    ///   - CharacterHarvestAction.OnApplyEffect on the server (NPCs / host)
    ///   - CharacterHarvestAction.OnApplyEffect in offline mode (NetworkObject not spawned)
    /// Blocks only the networked-client case: IsSpawned && !IsServer.
    /// </summary>
    public ItemSO ApplyHarvestOnServer(Harvestable target)
    {
        if (IsSpawned && !IsServer) return null;
        if (target == null || !target.CanHarvest()) return null;
        if (_character == null) return null;

        var entries = target.Harvest(_character);
        if (entries == null || entries.Count == 0) return null;

        Vector3 baseSpawn = _character.transform.position + _character.transform.forward * 0.5f + Vector3.up * 0.3f;

        // Resolve the worker's harvesting workplace once. Harvest() may have already despawned
        // the target (one-shot crops in OnDepleted), but the WorldItem spawns still happen
        // at baseSpawn in world space and pickup tasks still need registering.
        //
        // Accept JobHarvester OR JobFarmer — both run the Harvest → PickupLooseItem →
        // DepositResources cycle and need PickupLooseItemTask registered for the planner's
        // looseItemExists query (JobFarmer mirrors JobHarvester's worldState query in
        // 73aac877). Without this widening, a Farmer harvests an apple tree, no pickup task
        // is registered, looseItemExists stays false on every replan, items stay orphaned
        // on the ground forever and Wanted Resources Apple/current never increments.
        CommercialBuilding harvesterWorkplace = null;
        if (_character.CharacterJob != null)
        {
            var workAssignment = _character.CharacterJob.ActiveJobs.FirstOrDefault(j => j.AssignedJob is JobHarvester || j.AssignedJob is JobFarmer);
            if (workAssignment != null) harvesterWorkplace = workAssignment.Workplace;
        }

        // Cache the workplace's accepted-items list ONCE (avoid the per-spawned-item alloc of
        // GetAcceptedItems). Used to gate PickupLooseItemTask registration: only register a
        // pickup task for items the workplace will actually accept. Without this filter, a
        // harvestable whose outputs include non-wanted items (e.g. apple tree drops wood AND
        // apple sapling on destruction; lumberyard only wants wood) registers a task for
        // EVERY drop. ClaimBestTask may then return the sapling task first; worker picks up
        // the sapling, hasAtLeastOneResource stays false (sapling not in acceptedItems), no
        // deposit goal forms, worker freezes. See wiki/gotchas/harvester-picks-wrong-loose-item.md.
        List<ItemSO> workplaceAccepted = null;
        if (harvesterWorkplace is HarvestingBuilding hbHarvest)
            workplaceAccepted = hbHarvest.GetAcceptedItems();

        ItemSO firstSpawned = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Item == null || entry.Count <= 0) continue;
            // Per-entry accept check — every spawned WorldItem in this entry shares the same ItemSO.
            bool workplaceWantsItem = workplaceAccepted == null || workplaceAccepted.Contains(entry.Item);
            for (int n = 0; n < entry.Count; n++)
            {
                Vector3 jitter = baseSpawn;
                Vector2 j2 = UnityEngine.Random.insideUnitCircle * 0.25f;
                jitter.x += j2.x;
                jitter.z += j2.y;
                WorldItem spawned = WorldItem.SpawnWorldItem(entry.Item, jitter);
                if (spawned != null)
                {
                    if (firstSpawned == null) firstSpawned = entry.Item;
                    if (harvesterWorkplace != null && workplaceWantsItem)
                        harvesterWorkplace.TaskManager?.RegisterTask(new PickupLooseItemTask(spawned));
                }
            }
        }

        return firstSpawned;
    }

    /// <summary>
    /// Server-side (or offline) destruction execution. Calls <see cref="Harvestable.DestroyForOutputs"/>
    /// (which spawns the destruction WorldItems and despawns the harvestable), then registers a
    /// <see cref="PickupLooseItemTask"/> on the worker's harvesting workplace for each spawned item.
    /// Mirrors <see cref="ApplyHarvestOnServer"/> — without the task-registration pass, the worker's
    /// planner sees <c>looseItemExists=false</c>, never enters the Pickup→Deposit chain, and the
    /// dropped wood/etc. stays orphaned on the ground.
    /// Called from:
    ///   - <see cref="RequestDestroyHarvestableServerRpc"/> (networked client-owner path)
    ///   - <see cref="CharacterAction_DestroyHarvestable.OnApplyEffect"/> (host / NPC / offline)
    /// Blocks only the networked-client case: IsSpawned &amp;&amp; !IsServer.
    /// </summary>
    public void ApplyDestroyOnServer(Harvestable target)
    {
        if (IsSpawned && !IsServer) return;
        if (target == null || _character == null) return;

        var spawned = target.DestroyForOutputs(_character);

        // Resolve the worker's harvesting workplace once. DestroyForOutputs has already despawned
        // the harvestable, but the spawned WorldItems are server-authoritative and still live —
        // we only need a TaskManager reference to register pickups against.
        // Accept JobHarvester OR JobFarmer — same widening as ApplyHarvestOnServer (above).
        CommercialBuilding harvesterWorkplace = null;
        if (_character.CharacterJob != null)
        {
            var workAssignment = _character.CharacterJob.ActiveJobs.FirstOrDefault(j => j.AssignedJob is JobHarvester || j.AssignedJob is JobFarmer);
            if (workAssignment != null) harvesterWorkplace = workAssignment.Workplace;
        }

        if (harvesterWorkplace != null && harvesterWorkplace.TaskManager != null && spawned != null)
        {
            // Same accepted-items gate as ApplyHarvestOnServer — only register pickup tasks
            // for items the workplace will actually accept. Apple tree drops wood AND a
            // sapling on destruction; lumberyard only wants wood. Without this filter, the
            // sapling task gets registered, ClaimBestTask may return it first, worker picks
            // it up, hasAtLeastOneResource stays false (sapling not in acceptedItems), no
            // deposit goal forms, worker freezes mid-zone.
            List<ItemSO> workplaceAccepted = null;
            if (harvesterWorkplace is HarvestingBuilding hbDestroy)
                workplaceAccepted = hbDestroy.GetAcceptedItems();

            for (int i = 0; i < spawned.Count; i++)
            {
                var wi = spawned[i];
                if (wi == null) continue;
                if (workplaceAccepted != null)
                {
                    var itemSO = wi.ItemInstance?.ItemSO;
                    if (itemSO == null || !workplaceAccepted.Contains(itemSO)) continue;
                }
                harvesterWorkplace.TaskManager.RegisterTask(new PickupLooseItemTask(wi));
            }
        }
    }

    private Harvestable FindHarvestableNear(Vector3 position, float maxDistance = 2.5f)
    {
        Harvestable[] all = UnityEngine.Object.FindObjectsByType<Harvestable>(FindObjectsSortMode.None);
        Harvestable best = null;
        float bestDist = maxDistance;
        foreach (var h in all)
        {
            float dist = Vector3.Distance(h.transform.position, position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = h;
            }
        }
        return best;
    }

    /// <summary>
    /// Client requests the server to run the destruction effect on a Harvestable
    /// (axe → tree drops wood, etc.). Mirrors RequestHarvestServerRpc — target is
    /// resolved by world position because Harvestable is a scene-authored MonoBehaviour
    /// whose transform is identical on server and client.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestDestroyHarvestableServerRpc(Vector3 harvestablePosition)
    {
        Harvestable target = FindHarvestableNear(harvestablePosition);
        if (target == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find Harvestable to destroy near {harvestablePosition}");
            return;
        }
        ApplyDestroyOnServer(target);
    }

    /// <summary>
    /// Client requests the server to spawn a dropped item in the world.
    /// Clients cannot spawn NetworkObjects — they send the item data to the server.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestItemDropServerRpc(string itemId, string jsonData, Vector3 ownerPosition)
    {
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        ItemSO so = System.Array.Find(allItems, match => match.ItemId == itemId);
        if (so == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find ItemSO '{itemId}' for drop.");
            return;
        }

        ItemInstance instance = so.CreateInstance();
        JsonUtility.FromJsonOverwrite(jsonData, instance);
        instance.ItemSO = so;

        Vector3 dropPos = ownerPosition + Vector3.up * 1.5f;
        Vector3 offset = new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0, UnityEngine.Random.Range(-0.3f, 0.3f));
        WorldItem.SpawnWorldItem(instance, dropPos + offset);

        Debug.Log($"<color=green>[CharacterActions]</color> Server spawned dropped item: {so.ItemName}");
    }

    // ────────────────────── Furniture Placement RPCs ──────────────────────

    [Rpc(SendTo.Server)]
    public void RequestFurniturePlaceServerRpc(string furnitureItemSOId, Vector3 visualPosition, Vector3 gridAnchor, Quaternion rotation)
    {
        // Resolve the FurnitureItemSO
        ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
        FurnitureItemSO furnitureItemSO = null;
        foreach (var item in allItems)
        {
            if (item is FurnitureItemSO fso && fso.ItemId == furnitureItemSOId)
            {
                furnitureItemSO = fso;
                break;
            }
        }

        if (furnitureItemSO == null || furnitureItemSO.InstalledFurniturePrefab == null)
        {
            Debug.LogWarning($"[CharacterActions] Server: Could not find FurnitureItemSO '{furnitureItemSOId}'");
            return;
        }

        Furniture placed = Instantiate(furnitureItemSO.InstalledFurniturePrefab, visualPosition, rotation);

        var netObj = placed.GetComponent<NetworkObject>();
        if (netObj != null)
            netObj.Spawn();

        // Register with room grid if inside a room
        Room room = FindRoomAtPosition(gridAnchor);
        if (room != null && room.FurnitureManager != null)
        {
            room.FurnitureManager.RegisterSpawnedFurniture(placed, gridAnchor);
        }

        Debug.Log($"<color=green>[CharacterActions]</color> Server placed {furnitureItemSO.name} at {visualPosition} (anchor: {gridAnchor}).");
    }

    [Rpc(SendTo.Server)]
    public void RequestFurniturePickUpServerRpc(NetworkObjectReference furnitureRef)
    {
        if (!furnitureRef.TryGet(out NetworkObject netObj))
        {
            Debug.LogWarning("[CharacterActions] Server: Could not resolve furniture NetworkObject for pickup.");
            return;
        }

        Furniture furniture = netObj.GetComponent<Furniture>();
        if (furniture == null) return;

        // Unregister from room grid
        Room parentRoom = furniture.GetComponentInParent<Room>();
        if (parentRoom != null && parentRoom.FurnitureManager != null)
        {
            parentRoom.FurnitureManager.UnregisterAndRemove(furniture);
        }

        // Despawn
        if (netObj.IsSpawned)
            netObj.Despawn(true);

        Debug.Log($"<color=green>[CharacterActions]</color> Server despawned furniture '{furniture.FurnitureName}'.");
    }

    /// <summary>
    /// Client → Server: enqueue CharacterAction_SleepOnFurniture for the local
    /// player Character. Server resolves the specific bed by parent
    /// NetworkObject + world position (beds have no NO of their own per the
    /// no-nested-NO rule; multi-bed buildings need the position to disambiguate).
    /// Validates server-side proximity (anti-cheat / race), sets PendingSkipHours
    /// only after ExecuteAction succeeds, then queues the action. The auto-trigger
    /// watcher in TimeSkipController will fire RequestSkip once all connected
    /// players are sleeping with PendingSkipHours > 0.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestSleepOnFurnitureServerRpc(NetworkObjectReference parentRef, Vector3 bedWorldPosition, int slotIndex, int desiredHours)
    {
        if (!parentRef.TryGet(out NetworkObject parentNetObj))
        {
            Debug.LogWarning("[CharacterActions] Server: RequestSleepOnFurniture: parentRef did not resolve to a NetworkObject.");
            return;
        }

        // Multi-bed disambiguation: pick the BedFurniture under the parent whose
        // transform.position is closest to the position the client clicked.
        BedFurniture bed = FindClosestBedUnder(parentNetObj, bedWorldPosition);
        if (bed == null)
        {
            Debug.LogWarning("[CharacterActions] Server: RequestSleepOnFurniture: no BedFurniture found under the resolved NetworkObject.");
            return;
        }

        if (slotIndex < 0 || slotIndex >= bed.SlotCount)
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestSleepOnFurniture: slotIndex {slotIndex} out of range for {bed.FurnitureName}.");
            return;
        }

        // Anti-cheat / race: require the player to actually be at the bed.
        // Mirrors the canonical proximity gate used by other action paths.
        var bedInteractable = bed.GetComponent<InteractableObject>();
        if (bedInteractable != null && !bedInteractable.IsCharacterInInteractionZone(_character))
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestSleepOnFurniture: {_character.CharacterName} not in interaction zone of {bed.FurnitureName}.");
            return;
        }

        // Atomicity: only commit PendingSkipHours after ExecuteAction succeeds, so
        // a rejection (e.g. _currentAction != null) doesn't leak a stale target value
        // that the auto-trigger watcher would later read.
        var action = new CharacterAction_SleepOnFurniture(_character, bed, slotIndex);
        if (!ExecuteAction(action))
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestSleepOnFurniture: ExecuteAction rejected for {_character.CharacterName} on {bed.FurnitureName}.");
            return;
        }

        if (desiredHours > 0)
        {
            _character.SetPendingSkipHours(desiredHours);
        }

        Debug.Log($"<color=green>[CharacterActions]</color> Server: RequestSleepOnFurniture: enqueued for {_character.CharacterName} on {bed.FurnitureName} slot {slotIndex} (desiredHours={desiredHours}).");
    }

    /// <summary>
    /// Client → Server: enqueue <see cref="CharacterAction_OccupyFurniture"/> for the local
    /// player Character on the resolved <see cref="OccupiableFurniture"/>. Validates proximity
    /// server-side via <see cref="InteractableObject.IsCharacterInInteractionZone"/> (anti-cheat /
    /// spawn-race) and rejects if the character is already occupying a different furniture.
    ///
    /// Resolution: <paramref name="furnitureRef"/> targets any NetworkBehaviour reachable from
    /// the furniture — the same GameObject for Cashier (its own NB) or the parent building's
    /// NB for Bed/Chair (no per-furniture NO per the no-nested-NO rule). The server resolves
    /// in three steps:
    /// <list type="number">
    ///   <item>The NB carries OccupiableFurniture directly → exact target (Cashier).</item>
    ///   <item>Otherwise, walk the NB's GameObject for an OccupiableFurniture sibling.</item>
    ///   <item>Otherwise, walk children (under the building) and pick the closest to
    ///   <paramref name="furnitureWorldPosition"/> — same disambiguation pattern as
    ///   <see cref="RequestSleepOnFurnitureServerRpc"/> with <see cref="FindClosestBedUnder"/>.</item>
    /// </list>
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestOccupyFurnitureServerRpc(NetworkBehaviourReference furnitureRef, Vector3 furnitureWorldPosition)
    {
        if (_character == null)
        {
            Debug.LogWarning("[CharacterActions] Server: RequestOccupyFurniture: _character is null.");
            return;
        }

        if (!furnitureRef.TryGet(out NetworkBehaviour nb))
        {
            Debug.LogWarning("[CharacterActions] Server: RequestOccupyFurniture: furnitureRef did not resolve to a NetworkBehaviour.");
            return;
        }

        // OccupiableFurniture extends Furniture : MonoBehaviour, NOT NetworkBehaviour — the
        // two are sibling branches off MonoBehaviour, so a direct `nb as OccupiableFurniture`
        // is rejected by the C# compiler (no reference-conversion path). Always resolve via
        // GetComponent on the same GameObject first; this is the Cashier-style path where
        // NetworkBehaviour (CashierNetSync) and OccupiableFurniture (Cashier) live on the
        // same GO.
        OccupiableFurniture target = nb.GetComponent<OccupiableFurniture>();
        if (target == null)
        {
            // Bed/Chair path — the NB is the parent building; walk children and pick the
            // closest by world position.
            target = FindClosestOccupiableUnder(nb, furnitureWorldPosition);
        }
        if (target == null)
        {
            Debug.LogWarning("[CharacterActions] Server: RequestOccupyFurniture: no OccupiableFurniture resolvable from reference + position.");
            return;
        }

        var interactable = target.GetComponent<InteractableObject>();
        if (interactable != null && !interactable.IsCharacterInInteractionZone(_character))
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: {_character.CharacterName} not in interaction zone of {target.FurnitureName}.");
            return;
        }

        if (_character.OccupyingFurniture != null && _character.OccupyingFurniture != target)
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: {_character.CharacterName} already occupying {_character.OccupyingFurniture.name}; rejected.");
            return;
        }

        if (!target.IsCharacterAllowedToOccupy(_character))
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: {_character.CharacterName} not authorized to occupy {target.FurnitureName} (role gate).");
            return;
        }

        var action = new CharacterAction_OccupyFurniture(_character, target);
        if (!ExecuteAction(action))
        {
            Debug.LogWarning($"[CharacterActions] Server: RequestOccupyFurniture: ExecuteAction rejected for {_character.CharacterName} on {target.FurnitureName}.");
            return;
        }

        Debug.Log($"<color=green>[CharacterActions]</color> Server: RequestOccupyFurniture: enqueued for {_character.CharacterName} on {target.FurnitureName}.");
    }

    private static OccupiableFurniture FindClosestOccupiableUnder(NetworkBehaviour parent, Vector3 worldPosition)
    {
        if (parent == null) return null;
        var occupiables = parent.GetComponentsInChildren<OccupiableFurniture>();
        if (occupiables == null || occupiables.Length == 0) return null;
        if (occupiables.Length == 1) return occupiables[0];

        OccupiableFurniture best = null;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < occupiables.Length; i++)
        {
            float distSq = (occupiables[i].transform.position - worldPosition).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = occupiables[i];
            }
        }
        return best;
    }

    /// <summary>
    /// Client → Server: leave whatever furniture this character is currently occupying.
    /// Server-side: if <see cref="Character.OccupyingFurniture"/> is non-null, clear the
    /// current action — <see cref="CharacterAction_OccupyFurniture.OnCancel"/> calls
    /// <see cref="OccupiableFurniture.Leave"/> on the target and releases the seat.
    /// Idempotent — no-op if nothing to leave.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void RequestLeaveOccupiedFurnitureServerRpc()
    {
        if (_character == null) return;
        if (_character.OccupyingFurniture == null) return;
        ClearCurrentAction();
        Debug.Log($"<color=green>[CharacterActions]</color> Server: RequestLeaveOccupiedFurniture: cleared current action for {_character.CharacterName}.");
    }

    private static BedFurniture FindClosestBedUnder(NetworkObject parent, Vector3 worldPosition)
    {
        var beds = parent.GetComponentsInChildren<BedFurniture>();
        if (beds == null || beds.Length == 0) return null;
        if (beds.Length == 1) return beds[0];

        BedFurniture best = null;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < beds.Length; i++)
        {
            float distSq = (beds[i].transform.position - worldPosition).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = beds[i];
            }
        }
        return best;
    }

    private static Room FindRoomAtPosition(Vector3 position)
    {
        Room[] allRooms = UnityEngine.Object.FindObjectsByType<Room>(FindObjectsSortMode.None);
        foreach (var room in allRooms)
        {
            if (room.IsPointInsideRoom(position)) return room;
        }
        return null;
    }

    private CraftingStation FindCraftingStationNear(Vector3 position, float maxDistance = 2f)
    {
        CraftingStation[] stations = UnityEngine.Object.FindObjectsOfType<CraftingStation>();
        CraftingStation best = null;
        float bestDist = maxDistance;
        foreach (var s in stations)
        {
            float dist = Vector3.Distance(s.transform.position, position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = s;
            }
        }
        return best;
    }

    [Rpc(SendTo.NotServer)]
    private void BroadcastActionVisualsClientRpc(bool shouldPlayGeneric, float duration, string actionName)
    {
        if (IsOwner && _currentAction != null) return; // Owner may have already predicted it

        ClearCurrentAction(); // Clear any visual desyncs

        var proxy = new CharacterVisualProxyAction(_character, duration, shouldPlayGeneric, actionName);
        ExecuteAction(proxy);
    }

    private IEnumerator ActionContinuousTickRoutine(CharacterAction_Continuous action)
    {
        if (action == null) yield break;
        // Continuous actions are server-authoritative (Rule #18). If the local peer is not
        // the server, bail immediately — the action will replicate back via NetworkVariable
        // changes. Defense in depth: ExecuteAction's continuous-branch entry now also
        // short-circuits clients before reaching this routine, so this is belt-and-suspenders.
        if (!IsServer) yield break;

        var wait = new WaitForSeconds(action.TickIntervalSeconds);

        while (true)
        {
            // External cancellation safeguard — if CleanupAction nulled out _currentAction
            // (combat, incapacitation, movement cancel), exit cleanly.
            if (_currentAction != action) yield break;

            bool finished;
            try
            {
                // Server-only: OnTick mutates NetworkVariables; clients see replicated state.
                finished = action.OnTick();
            }
            catch (Exception e)
            {
                Debug.LogError($"[CharacterActions] Erreur OnTick action continue '{action?.ActionName}' on '{_character?.CharacterName}':");
                Debug.LogException(e);
                CleanupAction();
                yield break;
            }

            if (finished)
            {
                try { action.Finish(); }
                catch (Exception e)
                {
                    Debug.LogError($"[CharacterActions] Erreur Finish action continue '{action?.ActionName}':");
                    Debug.LogException(e);
                    CleanupAction();
                }

                // Tell every client to clear their visual proxy. The server broadcast at
                // ExecuteAction time used a 600s sentinel duration so the proxy stayed
                // active for the full server-side duration; without this, clients keep
                // showing their character "doing the action" until the 600s timer expires.
                if (IsServer) CancelActionVisualsClientRpc();

                yield break;
            }

            yield return wait;
        }
    }

    private IEnumerator ActionTimerRoutine(CharacterAction action)
    {
        if (action == null) yield break;

        if (action.Duration > 0)
        {
            yield return new WaitForSeconds(action.Duration);
        }

        if (_currentAction != action) yield break;

        try
        {
            action.OnApplyEffect();
            action.Finish();
        }
        catch (Exception e)
        {
            // Log the full exception (including stack trace) — project rule #31. Logging only
            // e.Message hides which subscriber/iteration actually threw, which made a recurring
            // "Collection was modified" loop in the destroy-harvestable chain take much longer
            // to diagnose than necessary.
            Debug.LogError($"[CharacterActions] Erreur durant l'exécution de l'action '{action?.ActionName}' on '{_character?.CharacterName}':");
            Debug.LogException(e);
            CleanupAction();
        }
    }

    private void CleanupAction()
    {
        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
        }

        _currentAction = null;
        _actionRoutine = null;

        OnActionFinished?.Invoke();
    }

    public void ClearCurrentAction()
    {
        if (IsServer) CancelActionVisualsClientRpc();
        ClearCurrentActionLocally();
    }

    [Rpc(SendTo.NotServer)]
    private void CancelActionVisualsClientRpc()
    {
        ClearCurrentActionLocally();
    }

    private void ClearCurrentActionLocally()
    {
        if (_actionRoutine != null)
        {
            StopCoroutine(_actionRoutine);
            _actionRoutine = null;
        }

        if (_currentAction != null)
        {
            _currentAction.OnActionFinished -= CleanupAction;
            _currentAction.OnCancel(); 

            var animHandler = _character.CharacterVisual?.CharacterAnimator;
            if (animHandler != null)
            {
                animHandler.ResetActionTriggers();
            }

            OnActionFinished?.Invoke();
        }

        _currentAction = null;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        StopAllCoroutines();
        _currentAction = null;
        _actionRoutine = null;
    }

    protected override void HandleIncapacitated(Character character)
    {
        ClearCurrentActionLocally();
    }

    protected override void HandleCombatStateChanged(bool inCombat)
    {
        if (inCombat) ClearCurrentActionLocally();
    }
}

public class CharacterVisualProxyAction : CharacterAction
{
    private bool _shouldPlayGeneric;
    private string _proxyActionName;
    public override bool ShouldPlayGenericActionAnimation => _shouldPlayGeneric;
    public override string ActionName => _proxyActionName;

    public CharacterVisualProxyAction(Character character, float duration, bool shouldPlayGeneric, string actionName) : base(character, duration)
    {
        _shouldPlayGeneric = shouldPlayGeneric;
        _proxyActionName = actionName;
    }

    public override void OnStart() { }
    
    public override void OnApplyEffect() 
    { 
        // Visual proxy does not mutate any game state
    } 
}
