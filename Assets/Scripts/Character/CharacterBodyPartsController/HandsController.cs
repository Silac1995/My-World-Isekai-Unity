using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class HandsController : MonoBehaviour, ICharacterSaveData<HandsSaveData>
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private List<CharacterHand> _hands = new List<CharacterHand>();
    [SerializeField] private SpriteLibraryAsset _spriteLibraryAssetsHands;
    [SerializeField] private Character _character;

    [Header("Settings")]
    [SerializeField] private string _spriteLibraryCategory = "01_human";
    [SerializeField] private bool _debugMode = true;

    // --- Carry System ---
    private ItemInstance _carriedItem;
    private GameObject _carriedVisual;

    // Held by Deserialize when hands aren't initialized yet; consumed by Initialize.
    private ItemInstance _pendingRestoreItem;

    public List<CharacterHand> Hands => _hands;

    /// <summary>Item actuellement porté dans les mains (null si rien)</summary>
    public ItemInstance CarriedItem => _carriedItem;

    /// <summary>Le personnage porte-t-il un item ?</summary>
    public bool IsCarrying => _carriedItem != null;

    /// <summary>
    /// Clears the currently carried item without spawning a WorldItem (unlike CharacterDropItem).
    /// Used by consume-from-hand flow (food, potion). The item is destroyed, not dropped.
    /// </summary>
    public void ClearCarriedItem()
    {
        _carriedItem = null;
        if (_carriedVisual != null)
        {
            Destroy(_carriedVisual);
            _carriedVisual = null;
        }
    }

    public void Initialize()
    {
        RetrieveHandObjects();

        // Auto-find Character si non assigné
        if (_character == null)
            _character = GetComponentInParent<Character>();

        // If Deserialize ran before the visual hierarchy was ready, finish the
        // carry-restore now that the hand bones exist.
        if (_pendingRestoreItem != null)
        {
            ItemInstance toRestore = _pendingRestoreItem;
            _pendingRestoreItem = null;
            ApplyRestoredCarry(toRestore);
        }
    }

    private void RetrieveHandObjects()
    {
        _hands.Clear();

        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;

        if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Recherche dans : {spritesContainer.name}");

        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        if (allRenderers.Length == 0 && _debugMode)
            Debug.LogWarning($"<color=red>[HandsController]</color> Aucun SpriteRenderer trouvé dans {spritesContainer.name} !");

        Dictionary<string, GameObject> thumbs = new Dictionary<string, GameObject>();
        Dictionary<string, GameObject> fingers = new Dictionary<string, GameObject>();

        foreach (var renderer in allRenderers)
        {
            GameObject part = renderer.gameObject;
            string lowerName = part.name.ToLower();

            string side = "";
            if (lowerName.Contains("_l")) side = "L";
            else if (lowerName.Contains("_r")) side = "R";

            if (string.IsNullOrEmpty(side)) continue;

            if (lowerName.Contains("skin_thumb"))
            {
                thumbs[side] = part;
                if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Trouvé Thumb côté {side} : {part.name}");
            }
            else if (lowerName.Contains("skin_fingers"))
            {
                fingers[side] = part;
                if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Trouvé Fingers côté {side} : {part.name}");
            }
        }

        string[] sides = { "L", "R" };
        foreach (string s in sides)
        {
            if (thumbs.ContainsKey(s) || fingers.ContainsKey(s))
            {
                CharacterHand newHand = new CharacterHand(
                    _bodyPartsController,
                    thumbs.ContainsKey(s) ? thumbs[s] : null,
                    fingers.ContainsKey(s) ? fingers[s] : null,
                    _spriteLibraryCategory,
                    s
                );

                _hands.Add(newHand);

                if (_debugMode)
                    Debug.Log($"<color=green>[HandsController]</color> Main <b>{s}</b> assemblée. " +
                              $"(Thumb: {thumbs.ContainsKey(s)}, Fingers: {fingers.ContainsKey(s)})");
            }
            else if (_debugMode)
            {
                Debug.LogWarning($"<color=yellow>[HandsController]</color> Côté <b>{s}</b> ignoré (aucune partie trouvée).");
            }
        }

        if (_hands.Count == 0 && _debugMode)
            Debug.LogWarning($"<color=red>[HandsController]</color> Scan terminé : 0 mains trouvées.");
    }

    public void SetHandsColor(Color color)
    {
        foreach (var hand in _hands)
        {
            hand.SetColor(color);
        }
    }

    public void SetHandsCategory(string categoryName)
    {
        _spriteLibraryCategory = categoryName;

        if (_hands.Count == 0)
        {
            Debug.LogWarning("<color=orange>[HandsController]</color> Liste vide, tentative de scan forcé.");
            RetrieveHandObjects();
        }

        foreach (var hand in _hands)
        {
            hand.SetCategory(categoryName);
        }
    }

    // --- Gestion des poses par côté ---

    /// <summary>
    /// Récupère la main du côté demandé ("L" ou "R"). Retourne null si non trouvée.
    /// </summary>
    public CharacterHand GetHand(string side)
    {
        foreach (var hand in _hands)
        {
            if (hand.Side == side) return hand;
        }

        if (_debugMode) Debug.LogWarning($"<color=orange>[HandsController]</color> Aucune main trouvée pour le côté {side}.");
        return null;
    }

    // --- Main gauche ---
    public void SetLeftHandNormal() => GetHand("L")?.SetPose("normal");
    public void SetLeftHandFist() => GetHand("L")?.SetPose("fist");

    // --- Main droite ---
    public void SetRightHandNormal() => GetHand("R")?.SetPose("normal");
    public void SetRightHandFist() => GetHand("R")?.SetPose("fist");

    // --- Les deux mains ---
    [ContextMenu("Set All Hands Normal")]
    public void SetAllHandsNormal()
    {
        foreach (var hand in _hands) hand.SetPose("normal");
    }

    [ContextMenu("Set All Hands Fist")]
    public void SetAllHandsFist()
    {
        foreach (var hand in _hands) hand.SetPose("fist");
    }

    // ================================================================
    // === CARRY SYSTEM ===
    // ================================================================

    /// <summary>
    /// Vérifie si les mains sont libres (pas de carry, pas d'arme équipée).
    /// </summary>
    public bool AreHandsFree()
    {
        if (IsCarrying) return false;

        if (_character != null && _character.CharacterEquipment != null)
        {
            if (_character.CharacterEquipment.HasWeaponInHands)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Prend un item dans les mains. Instancie le WorldItem visuel sur la main droite.
    /// Retourne true si l'item a été pris avec succès.
    /// </summary>
    public bool CarryItem(ItemInstance item)
    {
        if (item == null) return false;

        if (!AreHandsFree())
        {
            Debug.Log($"<color=orange>[Carry]</color> Les mains ne sont pas libres !");
            return false;
        }

        _carriedItem = item;
        AttachVisualToHand(item);

        Debug.Log($"<color=green>[Carry]</color> {_character?.CharacterName} porte {item.ItemSO.ItemName}.");
        return true;
    }

    /// <summary>
    /// Prend un item (depuis un ItemSO en créant une instance) dans les mains.
    /// </summary>
    public bool CarryItem(ItemSO itemSO)
    {
        if (itemSO == null) return false;
        ItemInstance instance = itemSO.CreateInstance();
        return CarryItem(instance);
    }

    /// <summary>
    /// Lâche l'item porté. Détruit le visuel des mains.
    /// Retourne l'ItemInstance lâchée (null si rien n'était porté).
    /// </summary>
    public ItemInstance DropCarriedItem()
    {
        if (_carriedItem == null) return null;

        ItemInstance dropped = _carriedItem;
        _carriedItem = null;

        if (_carriedVisual != null)
        {
            Destroy(_carriedVisual);
            _carriedVisual = null;
        }

        GetHand("R")?.SetPose("normal");

        Debug.Log($"<color=cyan>[Carry]</color> {_character?.CharacterName} a lâché {dropped.ItemSO.ItemName}.");
        return dropped;
    }

    /// <summary>
    /// Instancie le prefab WorldItem de l'item et le colle sur la main droite.
    /// </summary>
    private void AttachVisualToHand(ItemInstance item)
    {
        if (_carriedVisual != null)
        {
            Destroy(_carriedVisual);
            _carriedVisual = null;
        }

        GameObject prefab = item.ItemSO.WorldItemPrefab;
        if (prefab == null) return;

        CharacterHand rightHand = GetHand("R");
        Transform anchor = rightHand?.FingersObject?.transform;
        
        // Chercher le vrai bone de la main via le composant SpriteSkin
        if (anchor != null)
        {
            if (anchor.TryGetComponent(out UnityEngine.U2D.Animation.SpriteSkin skin))
            {
                if (skin.boneTransforms != null && skin.boneTransforms.Length > 0)
                {
                    // Utiliser le premier bone lié à ce sprite (généralement bone_fingers_R ou de la main)
                    anchor = skin.boneTransforms[0];
                }
            }
            else
            {
                // Fallback (très peu probable si tout est bien configuré avec 2D Animation)
                Transform bone = anchor.Find("bone_Fingers_R");
                if (bone != null) anchor = bone;
            }
        }

        if (anchor == null)
        {
            anchor = _character != null ? _character.transform : transform;
        }

        _carriedVisual = Instantiate(prefab, anchor);
        _carriedVisual.name = $"Carried_{item.ItemSO.ItemName}";
        _carriedVisual.transform.localPosition = Vector3.zero;
        _carriedVisual.transform.localRotation = Quaternion.identity;
        _carriedVisual.transform.localScale = Vector3.one * 0.5f;

        if (_carriedVisual.TryGetComponent(out WorldItem worldItem))
        {
            worldItem.Initialize(item);
            worldItem.IsBeingCarried = true; // Empêche les autres harvesters de le voler

            // --- GESTION DU SORTING GROUP ---
            if (worldItem.SortingGroup != null)
            {
                // Trouver l'ordre de tri de la main (via SortingGroup ou SpriteRenderer)
                int handSortingOrder = 0;
                string handSortingLayer = "Default";

                if (rightHand?.FingersObject != null)
                {
                    if (rightHand.FingersObject.TryGetComponent(out UnityEngine.Rendering.SortingGroup hg))
                    {
                        handSortingOrder = hg.sortingOrder;
                        handSortingLayer = hg.sortingLayerName;
                    }
                    else if (rightHand.FingersRenderer != null)
                    {
                        handSortingOrder = rightHand.FingersRenderer.sortingOrder;
                        handSortingLayer = rightHand.FingersRenderer.sortingLayerName;
                    }
                }

                // Appliquer n-1 pour qu'il soit derrière les doigts
                worldItem.SortingGroup.sortingLayerName = handSortingLayer;
                worldItem.SortingGroup.sortingOrder = handSortingOrder - 1;
            }
        }

        // Désactiver physique et colliders
        if (_carriedVisual.TryGetComponent(out Rigidbody rb))
        {
            rb.isKinematic = true;
        }
        foreach (var col in _carriedVisual.GetComponentsInChildren<Collider>())
        {
            col.enabled = false;
        }

        // CRITICAL: strip the visual clone's NetworkObject + NetworkBehaviours.
        //
        // The WorldItem prefab carries a NetworkObject (used by real dropped instances).
        // For the carried visual we never call Spawn() — but the clone is parented under
        // the player's hand bone, which lives under the player's NetworkObject. NGO's
        // SceneEventData.SortParentedNetworkObjects walks every spawned root NO's
        // GetComponentsInChildren<NetworkObject>() during initial-sync to a late joiner,
        // and Serialize NREs on this never-spawned NO at NetworkObject.cs:3172 because
        // NetworkManagerOwner is null (never went through SpawnInternal). Symptom:
        // host picks up a watering can → client cannot join until the host drops it.
        //
        // Order: NetworkBehaviours first, then NetworkObject (the latter has DisallowMultipleComponent
        // editor metadata but DestroyImmediate at runtime is unconstrained). After this strip,
        // _carriedVisual is a pure visual GameObject with no networking surface.
        //
        // See memory: feedback_no_networkobject_in_visual_clone.md
        StripNetworkComponents(_carriedVisual);

        rightHand?.SetPose("fist");
    }

    /// <summary>
    /// Removes every NetworkBehaviour (including the Initialize-completed WorldItem) and
    /// the NetworkObject from a visual-only clone. Used by AttachVisualToHand so the
    /// carried-visual WorldItem prefab clone never poisons NGO's scene-sync child walk.
    /// </summary>
    private static void StripNetworkComponents(GameObject root)
    {
        if (root == null) return;
        var behaviours = root.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null) DestroyImmediate(behaviours[i]);
        }
        var netObjects = root.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true);
        for (int i = 0; i < netObjects.Length; i++)
        {
            if (netObjects[i] != null) DestroyImmediate(netObjects[i]);
        }
    }

    // ================================================================
    // === ICharacterSaveData IMPLEMENTATION ===
    // ================================================================

    public string SaveKey => "HandsController";

    // Runs after CharacterEquipment (priority 30) so the weapon slot is restored
    // first. AreHandsFree() then reflects the post-equip state correctly.
    public int LoadPriority => 35;

    public HandsSaveData Serialize()
    {
        var data = new HandsSaveData();

        if (_carriedItem != null && _carriedItem.ItemSO != null)
        {
            data.carriedItemId = _carriedItem.ItemSO.ItemId;
            data.carriedItemJson = JsonUtility.ToJson(_carriedItem);
        }

        return data;
    }

    public void Deserialize(HandsSaveData data)
    {
        // Always start from a clean slate so a save with empty hands clears any
        // pre-load carry state (e.g. a default item placed during prefab spawn).
        ClearCarriedItem();
        _pendingRestoreItem = null;

        if (data == null || string.IsNullOrEmpty(data.carriedItemId))
            return;

        ItemInstance restored;
        try
        {
            ItemSO[] allItems = Resources.LoadAll<ItemSO>("Data/Item");
            ItemSO so = System.Array.Find(allItems, match => match.ItemId == data.carriedItemId);
            if (so == null)
            {
                Debug.LogWarning($"<color=orange>[HandsController.Deserialize]</color> ItemSO not found for id '{data.carriedItemId}'. Carried item will be lost.");
                return;
            }

            restored = so.CreateInstance();
            if (!string.IsNullOrEmpty(data.carriedItemJson))
            {
                JsonUtility.FromJsonOverwrite(data.carriedItemJson, restored);
                restored.ItemSO = so;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"<color=red>[HandsController.Deserialize]</color> Failed to rebuild carried item '{data.carriedItemId}': {ex.Message}");
            return;
        }

        // If hands haven't been scanned yet, defer the visual attach until Initialize().
        if (_hands == null || _hands.Count == 0)
        {
            _pendingRestoreItem = restored;
            if (_debugMode)
                Debug.Log($"<color=cyan>[HandsController.Deserialize]</color> Hands not yet initialized — deferring carry restore for {restored.ItemSO.ItemName}.");
            return;
        }

        ApplyRestoredCarry(restored);
    }

    /// <summary>
    /// Restore a saved carry item, bypassing the AreHandsFree() weapon check
    /// (the saved state is the source of truth at this point).
    /// </summary>
    private void ApplyRestoredCarry(ItemInstance item)
    {
        if (item == null) return;

        _carriedItem = item;
        AttachVisualToHand(item);

        if (_debugMode)
            Debug.Log($"<color=green>[HandsController.Deserialize]</color> Restored carry: {_character?.CharacterName} is holding {item.ItemSO.ItemName}.");
    }

    // Non-generic bridge (explicit interface impl)
    string ICharacterSaveData.SerializeToJson() => CharacterSaveDataHelper.SerializeToJson(this);
    void ICharacterSaveData.DeserializeFromJson(string json) => CharacterSaveDataHelper.DeserializeFromJson(this, json);
}
