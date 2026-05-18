using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using MWI.Interactables;
using MWI.Terrain;
using MWI.WorldSystem;

/// <summary>
/// Spawn tab of the dev-mode panel. Owns all dropdowns, multi-entry rows, the
/// Count field, and the Armed toggle. When armed, left-click on the Environment
/// layer spawns N configured NPCs at the cursor.
/// </summary>
public class DevSpawnModule : MonoBehaviour
{
    [Header("Core dropdowns")]
    [SerializeField] private TMP_Dropdown _raceDropdown;
    [SerializeField] private TMP_Dropdown _prefabDropdown;
    [SerializeField] private TMP_Dropdown _personalityDropdown;
    [SerializeField] private TMP_Dropdown _traitDropdown;

    [Header("Item dropdown (Item sub-tab)")]
    [Tooltip("Item list shown by the Item sub-tab. Selecting any item drops it (or N copies) at the click point. The Character sub-tab ignores this dropdown.")]
    [SerializeField] private TMP_Dropdown _itemDropdown;

    [Header("Harvestable dropdown (Harvestable sub-tab)")]
    [Tooltip("HarvestableSO list shown by the Harvestable sub-tab. Selecting any harvestable (crop, tree, ore vein, etc.) spawns a fully-mature, free-positioned instance at the click point — ready to pick / destroy immediately, no plowing or growth time required. The Character / Item sub-tabs ignore this dropdown.")]
    [SerializeField] private TMP_Dropdown _harvestableDropdown;

    [Header("Sub-tabs (Character / Item / Harvestable)")]
    [SerializeField] private Button _charSubTabButton;
    [SerializeField] private Button _itemSubTabButton;
    [SerializeField] private Button _harvestableSubTabButton;
    [SerializeField] private GameObject _charSubPanel;
    [SerializeField] private GameObject _itemSubPanel;
    [SerializeField] private GameObject _harvestableSubPanel;

    [Header("Combat styles list")]
    [SerializeField] private Transform _combatStylesContainer;
    [SerializeField] private Button _addCombatStyleButton;

    [Header("Skills list")]
    [SerializeField] private Transform _skillsContainer;
    [SerializeField] private Button _addSkillButton;

    [Header("Count & Armed")]
    [SerializeField] private TMP_InputField _countField;
    [SerializeField] private Toggle _armedToggle;

    [Header("Row prefab")]
    [Tooltip("Prefab with DevSpawnRow script. Shared between combat and skill lists.")]
    [SerializeField] private DevSpawnRow _rowPrefab;

    // --- Cached catalogs (Resources) ---
    private List<RaceSO> _races = new List<RaceSO>();
    private List<GameObject> _racePrefabs = new List<GameObject>();
    private List<CharacterPersonalitySO> _personalities = new List<CharacterPersonalitySO>();
    private List<CharacterBehavioralTraitsSO> _traits = new List<CharacterBehavioralTraitsSO>();
    private List<CombatStyleSO> _combatStyles = new List<CombatStyleSO>();
    private List<SkillSO> _skills = new List<SkillSO>();
    private List<ItemSO> _items = new List<ItemSO>();
    private List<HarvestableSO> _harvestables = new List<HarvestableSO>();

    private readonly List<DevSpawnRow> _combatRows = new List<DevSpawnRow>();
    private readonly List<DevSpawnRow> _skillRows = new List<DevSpawnRow>();

    private enum SpawnSubTab { Character, Item, Harvestable }
    private SpawnSubTab _activeSubTab = SpawnSubTab.Character;

    // ── Harvestable ghost-placement state (Harvestable sub-tab only) ──
    // When the user arms the Spawn toggle on the Harvestable sub-tab, we spawn a stripped
    // visual clone of the selected HarvestableSO's prefab and follow the cursor. The ghost
    // snaps to the nearest TerrainCellGrid cell of the MapController under the cursor —
    // matching CropPlacementManager's UX so a dev-spawned crop / tree / ore vein lines up
    // with the production crop placement grid. LMB confirms; ESC / RMB / disarm / sub-tab
    // change / dropdown change cancels and rebuilds.
    private GameObject _harvestableGhost;
    private HarvestableSO _harvestableGhostSO;
    private Vector3 _harvestableGhostSnappedPos;
    private bool _harvestableGhostIsOnGrid;
    private int _harvestableGhostCellX = -1;
    private int _harvestableGhostCellZ = -1;
    private MapController _harvestableGhostMap;
    private TerrainCellGrid _harvestableGhostGrid;
    private bool _warnedNoCameraGhost, _warnedRayMissGhost;

    private const int ENVIRONMENT_LAYER_MASK_FALLBACK = ~0; // used only if Environment layer missing
    private int _environmentLayerMask;

    private void Start()
    {
        LoadCatalogs();
        PopulateCoreDropdowns();
        WireListeners();
        SetupEnvironmentLayerMask();
    }

    private void OnDestroy()
    {
        UnwireListeners();
    }

    // ─── Catalog loading ─────────────────────────────────────────────

    private void LoadCatalogs()
    {
        _races.Clear();
        if (GameSessionManager.Instance != null && GameSessionManager.Instance.AvailableRaces != null)
        {
            _races.AddRange(GameSessionManager.Instance.AvailableRaces);
        }

        _personalities.Clear();
        _personalities.AddRange(Resources.LoadAll<CharacterPersonalitySO>("Data/Personnality"));

        _traits.Clear();
        _traits.AddRange(Resources.LoadAll<CharacterBehavioralTraitsSO>("Data/Behavioural Traits"));

        _combatStyles.Clear();
        _combatStyles.AddRange(Resources.LoadAll<CombatStyleSO>("Data/CombatStyle"));

        _skills.Clear();
        _skills.AddRange(Resources.LoadAll<SkillSO>("Data/Skills"));

        // Items — alphabetised by display name for usability in the override dropdown.
        _items.Clear();
        _items.AddRange(Resources.LoadAll<ItemSO>("Data/Item"));
        _items.Sort((a, b) =>
        {
            string an = (a != null && a.ItemName != null) ? a.ItemName : string.Empty;
            string bn = (b != null && b.ItemName != null) ? b.ItemName : string.Empty;
            return string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
        });

        // Harvestables — alphabetised by display name. Resources.LoadAll picks up every
        // HarvestableSO subclass (HarvestableSO base, CropSO, TreeHarvestableSO) recursively
        // under Assets/Resources/Data/ so a single dropdown lists crops, trees, ore veins, etc.
        _harvestables.Clear();
        _harvestables.AddRange(Resources.LoadAll<HarvestableSO>("Data"));
        // Drop entries without a spawn prefab — they can't be instantiated.
        _harvestables.RemoveAll(h => h == null || h.HarvestablePrefab == null);
        _harvestables.Sort((a, b) =>
        {
            string an = !string.IsNullOrEmpty(a.DisplayName) ? a.DisplayName : a.name;
            string bn = !string.IsNullOrEmpty(b.DisplayName) ? b.DisplayName : b.name;
            return string.Compare(an, bn, System.StringComparison.OrdinalIgnoreCase);
        });

        Debug.Log($"<color=cyan>[DevSpawn]</color> Catalogs loaded — races:{_races.Count} personalities:{_personalities.Count} traits:{_traits.Count} combat:{_combatStyles.Count} skills:{_skills.Count} items:{_items.Count} harvestables:{_harvestables.Count}");
    }

    private void PopulateCoreDropdowns()
    {
        if (_raceDropdown != null)
        {
            _raceDropdown.ClearOptions();
            var names = new List<string>();
            foreach (var r in _races) names.Add(r.raceName);
            _raceDropdown.AddOptions(names);
            _raceDropdown.value = 0;
            _raceDropdown.RefreshShownValue();
            RefreshPrefabDropdown();
        }

        if (_personalityDropdown != null)
        {
            var names = new List<string> { "Random" };
            foreach (var p in _personalities) names.Add(p.PersonalityName);
            _personalityDropdown.ClearOptions();
            _personalityDropdown.AddOptions(names);
            _personalityDropdown.value = 0;
            _personalityDropdown.RefreshShownValue();
        }

        if (_traitDropdown != null)
        {
            var names = new List<string> { "Random" };
            foreach (var t in _traits) names.Add(t.name);
            _traitDropdown.ClearOptions();
            _traitDropdown.AddOptions(names);
            _traitDropdown.value = 0;
            _traitDropdown.RefreshShownValue();
        }

        if (_itemDropdown != null)
        {
            // Sub-tab determines mode now — the dropdown is just a list of items (no sentinel).
            var names = new List<string>();
            foreach (var it in _items)
            {
                if (it == null) continue;
                names.Add(string.IsNullOrEmpty(it.ItemName) ? it.name : it.ItemName);
            }
            _itemDropdown.ClearOptions();
            _itemDropdown.AddOptions(names);
            _itemDropdown.value = 0;
            _itemDropdown.RefreshShownValue();
        }

        if (_harvestableDropdown != null)
        {
            var names = new List<string>();
            foreach (var h in _harvestables)
            {
                if (h == null) continue;
                names.Add(!string.IsNullOrEmpty(h.DisplayName) ? h.DisplayName : h.name);
            }
            _harvestableDropdown.ClearOptions();
            _harvestableDropdown.AddOptions(names);
            _harvestableDropdown.value = 0;
            _harvestableDropdown.RefreshShownValue();
        }
    }

    private void RefreshPrefabDropdown()
    {
        if (_prefabDropdown == null) return;

        _racePrefabs.Clear();
        var names = new List<string>();

        if (_raceDropdown != null && _races.Count > 0)
        {
            var race = _races[Mathf.Clamp(_raceDropdown.value, 0, _races.Count - 1)];
            foreach (var prefab in race.character_prefabs)
            {
                if (prefab != null)
                {
                    _racePrefabs.Add(prefab);
                    names.Add(prefab.name);
                }
            }
        }

        _prefabDropdown.ClearOptions();
        _prefabDropdown.AddOptions(names);
        _prefabDropdown.value = 0;
        _prefabDropdown.RefreshShownValue();
    }

    // ─── Sub-tab navigation ───────────────────────────────────────────

    private void ShowCharacterSubTab()
    {
        _activeSubTab = SpawnSubTab.Character;
        if (_charSubPanel != null) _charSubPanel.SetActive(true);
        if (_itemSubPanel != null) _itemSubPanel.SetActive(false);
        if (_harvestableSubPanel != null) _harvestableSubPanel.SetActive(false);
        ClearHarvestableGhost();
        UpdateSubTabVisuals();
    }

    private void ShowItemSubTab()
    {
        _activeSubTab = SpawnSubTab.Item;
        if (_charSubPanel != null) _charSubPanel.SetActive(false);
        if (_itemSubPanel != null) _itemSubPanel.SetActive(true);
        if (_harvestableSubPanel != null) _harvestableSubPanel.SetActive(false);
        ClearHarvestableGhost();
        UpdateSubTabVisuals();
    }

    private void ShowHarvestableSubTab()
    {
        _activeSubTab = SpawnSubTab.Harvestable;
        if (_charSubPanel != null) _charSubPanel.SetActive(false);
        if (_itemSubPanel != null) _itemSubPanel.SetActive(false);
        if (_harvestableSubPanel != null) _harvestableSubPanel.SetActive(true);
        // If the user already had Armed on, the ghost goes live the moment they switch in.
        if (_armedToggle != null && _armedToggle.isOn) EnsureHarvestableGhost();
        UpdateSubTabVisuals();
    }

    /// <summary>
    /// Disables the active sub-tab button so it reads as "selected" while the inactive ones
    /// stay clickable. Cheap visual cue without an extra style asset or selection sprite.
    /// </summary>
    private void UpdateSubTabVisuals()
    {
        if (_charSubTabButton != null) _charSubTabButton.interactable = (_activeSubTab != SpawnSubTab.Character);
        if (_itemSubTabButton != null) _itemSubTabButton.interactable = (_activeSubTab != SpawnSubTab.Item);
        if (_harvestableSubTabButton != null) _harvestableSubTabButton.interactable = (_activeSubTab != SpawnSubTab.Harvestable);
    }

    // ─── Listener wiring ─────────────────────────────────────────────

    private void WireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.AddListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.AddListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.AddListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.AddListener(HandleArmedChanged);

        if (_charSubTabButton != null) _charSubTabButton.onClick.AddListener(ShowCharacterSubTab);
        if (_itemSubTabButton != null) _itemSubTabButton.onClick.AddListener(ShowItemSubTab);
        if (_harvestableSubTabButton != null) _harvestableSubTabButton.onClick.AddListener(ShowHarvestableSubTab);
        if (_harvestableDropdown != null) _harvestableDropdown.onValueChanged.AddListener(HandleHarvestableDropdownChanged);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
        }

        // Default to character sub-tab on startup.
        ShowCharacterSubTab();
    }

    private void UnwireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.RemoveListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.RemoveListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.RemoveListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.RemoveListener(HandleArmedChanged);

        if (_charSubTabButton != null) _charSubTabButton.onClick.RemoveListener(ShowCharacterSubTab);
        if (_itemSubTabButton != null) _itemSubTabButton.onClick.RemoveListener(ShowItemSubTab);
        if (_harvestableSubTabButton != null) _harvestableSubTabButton.onClick.RemoveListener(ShowHarvestableSubTab);
        if (_harvestableDropdown != null) _harvestableDropdown.onValueChanged.RemoveListener(HandleHarvestableDropdownChanged);
        // Defensive: destroy any orphaned ghost on destroy.
        ClearHarvestableGhost();

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged -= HandleClickConsumerChanged;
        }

        foreach (var row in _combatRows) if (row != null) row.OnRemoveClicked -= HandleCombatRowRemove;
        foreach (var row in _skillRows) if (row != null) row.OnRemoveClicked -= HandleSkillRowRemove;
    }

    private void HandleRaceChanged(int _) => RefreshPrefabDropdown();

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (!isEnabled && _armedToggle != null && _armedToggle.isOn)
        {
            _armedToggle.isOn = false;
        }
        // Closing dev mode always nukes the ghost regardless of state — defensive.
        if (!isEnabled) ClearHarvestableGhost();
    }

    private void HandleHarvestableDropdownChanged(int _)
    {
        // Selection change while ghost is active → rebuild ghost from the new SO.
        if (_harvestableGhost != null) EnsureHarvestableGhost();
    }

    private void HandleClickConsumerChanged()
    {
        if (DevModeManager.Instance == null) return;
        if (DevModeManager.Instance.ActiveClickConsumer == this) return;
        // Another module claimed the click stream — disarm our toggle.
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    // ─── Row management ───────────────────────────────────────────────

    private void AddCombatRow()
    {
        if (_rowPrefab == null || _combatStylesContainer == null) return;
        var row = Instantiate(_rowPrefab, _combatStylesContainer);
        var names = new List<string>();
        foreach (var s in _combatStyles) names.Add(s.StyleName);
        row.Populate(names, defaultLevel: 1);
        row.OnRemoveClicked += HandleCombatRowRemove;
        _combatRows.Add(row);
    }

    private void HandleCombatRowRemove(DevSpawnRow row)
    {
        _combatRows.Remove(row);
        row.OnRemoveClicked -= HandleCombatRowRemove;
        Destroy(row.gameObject);
    }

    private void AddSkillRow()
    {
        if (_rowPrefab == null || _skillsContainer == null) return;
        var row = Instantiate(_rowPrefab, _skillsContainer);
        var names = new List<string>();
        foreach (var s in _skills) names.Add(s.SkillName);
        row.Populate(names, defaultLevel: 1);
        row.OnRemoveClicked += HandleSkillRowRemove;
        _skillRows.Add(row);
    }

    private void HandleSkillRowRemove(DevSpawnRow row)
    {
        _skillRows.Remove(row);
        row.OnRemoveClicked -= HandleSkillRowRemove;
        Destroy(row.gameObject);
    }

    // ─── Click-to-spawn ──────────────────────────────────────────────

    private void SetupEnvironmentLayerMask()
    {
        int envLayer = LayerMask.NameToLayer("Environment");
        if (envLayer < 0)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> 'Environment' layer not found — falling back to all layers.");
            _environmentLayerMask = ENVIRONMENT_LAYER_MASK_FALLBACK;
        }
        else
        {
            _environmentLayerMask = 1 << envLayer;
        }
    }

    private void HandleArmedChanged(bool armed)
    {
        Debug.Log($"<color=cyan>[DevSpawn]</color> Armed: {armed}");
        if (DevModeManager.Instance == null) return;
        if (armed) DevModeManager.Instance.SetClickConsumer(this);
        else DevModeManager.Instance.ClearClickConsumer(this);

        // Harvestable sub-tab is the only one that uses a follow-cursor ghost. Spawn on
        // arm, destroy on disarm. The other sub-tabs are click-and-spawn directly.
        if (armed && _activeSubTab == SpawnSubTab.Harvestable) EnsureHarvestableGhost();
        else ClearHarvestableGhost();
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;

        // Armed click-loop (legacy path — kept for discoverability via the toggle).
        // Global shortcuts (Ctrl+Click / Space+Click / ESC) are handled by DevModeManager so
        // they keep working regardless of which tab's content is currently active.
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;

        // Escape disarms the toggle in armed mode (also handled globally by DevModeManager).
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _armedToggle.isOn = false;
            return;
        }

        // Harvestable sub-tab: drive the ghost-placement loop (mirrors CropPlacementManager
        // UX). LMB confirms a single grid-snapped spawn at the ghost position; RMB clears
        // the ghost (also re-armed on dropdown change). Space+LMB shortcut still hits the
        // scatter path below via DevModeManager.
        if (_activeSubTab == SpawnSubTab.Harvestable && _harvestableGhost != null)
        {
            UpdateHarvestableGhostPosition();

            if (Input.GetMouseButtonDown(1))
            {
                // RMB while ghost is up = cancel (drop ghost + disarm).
                _armedToggle.isOn = false;
                return;
            }

            // If Space or Ctrl is held, DevModeManager handles the click — don't double-fire here.
            if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

            if (Input.GetMouseButtonDown(0))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                ConfirmHarvestableGhostSpawn();
            }
            return;
        }

        // If Space or Ctrl is held, DevModeManager handles the click — don't double-fire here.
        if (Input.GetKey(KeyCode.Space)) return;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;

        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        if (TryRaycastEnvironment(out Vector3 hitPoint))
        {
            SpawnAt(hitPoint);
        }
    }

    // ─── Shortcut API (invoked by DevModeManager) ─────────────────────

    /// <summary>
    /// Raycasts the environment and spawns using the panel's current configuration. Returns true
    /// on successful spawn. Public so <see cref="DevModeManager"/> can invoke it as a global shortcut.
    /// </summary>
    public bool TrySpawnAtCursor()
    {
        if (TryRaycastEnvironment(out Vector3 hitPoint))
        {
            SpawnAt(hitPoint);
            return true;
        }
        return false;
    }

    /// <summary>Disarm the armed toggle if on (invoked by DevModeManager's ESC shortcut).</summary>
    public void DisarmToggle()
    {
        if (_armedToggle != null && _armedToggle.isOn) _armedToggle.isOn = false;
    }

    /// <summary>True iff the armed Spawn toggle is currently on.</summary>
    public bool IsArmed => _armedToggle != null && _armedToggle.isOn;

    private bool TryRaycastEnvironment(out Vector3 hitPoint)
    {
        hitPoint = default;
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Camera.main is null — cannot spawn.");
            return false;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _environmentLayerMask))
        {
            return false;
        }

        hitPoint = hit.point;
        return true;
    }

    private void SpawnAt(Vector3 anchor)
    {
        // Sub-tab determines spawn type: Item / Harvestable sub-tabs route to dedicated
        // SpawnXBatch helpers using their currently-selected dropdown entry; Character
        // sub-tab falls through to the NPC spawn path below.
        if (_activeSubTab == SpawnSubTab.Item)
        {
            if (_itemDropdown != null && _items != null
                && _itemDropdown.value >= 0 && _itemDropdown.value < _items.Count
                && _items[_itemDropdown.value] != null)
            {
                SpawnItemBatch(anchor, _items[_itemDropdown.value]);
            }
            else
            {
                Debug.LogWarning("<color=orange>[DevSpawn]</color> Item sub-tab active but no valid item selected.");
            }
            return;
        }

        if (_activeSubTab == SpawnSubTab.Harvestable)
        {
            if (_harvestableDropdown != null && _harvestables != null
                && _harvestableDropdown.value >= 0 && _harvestableDropdown.value < _harvestables.Count
                && _harvestables[_harvestableDropdown.value] != null)
            {
                SpawnHarvestableBatch(anchor, _harvestables[_harvestableDropdown.value]);
            }
            else
            {
                Debug.LogWarning("<color=orange>[DevSpawn]</color> Harvestable sub-tab active but no valid harvestable selected.");
            }
            return;
        }

        if (_races.Count == 0 || _racePrefabs.Count == 0)
        {
            Debug.LogError("<color=red>[DevSpawn]</color> No race or prefab available.");
            return;
        }

        RaceSO race = _races[Mathf.Clamp(_raceDropdown.value, 0, _races.Count - 1)];
        GameObject prefab = _racePrefabs[Mathf.Clamp(_prefabDropdown.value, 0, _racePrefabs.Count - 1)];

        int n = 1;
        if (_countField != null && int.TryParse(_countField.text, out int parsed)) n = Mathf.Max(1, parsed);

        float radius = 4f * Mathf.Sqrt(n);

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = anchor;
            if (n > 1)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                pos += new Vector3(offset.x, 0f, offset.y);
            }

            CharacterPersonalitySO personality = ResolvePersonality();
            CharacterBehavioralTraitsSO trait = ResolveTrait();
            List<(CombatStyleSO, int)> combatList = BuildCombatList();
            List<(SkillSO, int)> skillList = BuildSkillList();

            var character = SpawnManager.Instance.SpawnCharacter(
                pos: pos,
                race: race,
                visualPrefab: prefab,
                personality: personality,
                traits: trait,
                combatStyles: combatList,
                skills: skillList
            );

            if (character == null)
            {
                Debug.LogError($"<color=red>[DevSpawn]</color> Spawn {i} returned null.");
            }
        }

        Debug.Log($"<color=green>[DevSpawn]</color> Spawned {n} NPC(s) near {anchor} (radius {radius:F2}u).");
    }

    /// <summary>
    /// Drops N copies of <paramref name="item"/> around <paramref name="anchor"/> using the same
    /// scatter formula as character spawning (radius = 4 * sqrt(N)). Server-only — bails with a
    /// clear log if invoked on a client. SpawnManager.SpawnItem also enforces this internally.
    /// </summary>
    private void SpawnItemBatch(Vector3 anchor, ItemSO item)
    {
        if (item == null)
        {
            Debug.LogError("<color=red>[DevSpawn]</color> SpawnItemBatch called with null item.");
            return;
        }

        if (SpawnManager.Instance == null)
        {
            Debug.LogError("<color=red>[DevSpawn]</color> SpawnManager.Instance is null — cannot spawn item.");
            return;
        }

        // Clearer error than SpawnManager's internal check — surfaces the dev-mode origin.
        if (Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Item spawn requested on a client — host-only operation, ignoring.");
            return;
        }

        int n = 1;
        if (_countField != null && int.TryParse(_countField.text, out int parsed)) n = Mathf.Max(1, parsed);

        float radius = 4f * Mathf.Sqrt(n);
        int spawned = 0;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = anchor;
            if (n > 1)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                pos += new Vector3(offset.x, 0f, offset.y);
            }

            try
            {
                var instance = SpawnManager.Instance.SpawnItem(item, pos);
                if (instance != null) spawned++;
                else Debug.LogWarning($"<color=orange>[DevSpawn]</color> SpawnItem {i} returned null for '{item.ItemName}'.");
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        Debug.Log($"<color=green>[DevSpawn]</color> Spawned {spawned}/{n} '{item.ItemName}' near {anchor} (radius {radius:F2}u).");
    }

    /// <summary>
    /// Spawns N copies of the harvestable described by <paramref name="so"/> around
    /// <paramref name="anchor"/> using the same scatter formula as character / item spawning
    /// (radius = 4 * sqrt(N)). Each scattered position is snapped to its containing
    /// <see cref="MWI.Terrain.TerrainCellGrid"/> cell so the spawn aligns with the world
    /// grid (matching <see cref="MWI.Farming.CropPlacementManager"/>'s placement convention).
    /// When no MapController / grid is found at the scattered position the instance falls
    /// back to the raw scattered XZ (still free-positioned). Either way the instance is
    /// started fully mature and free-positioned (cellX = -1) so no plowing or
    /// FarmGrowthSystem tick is required.
    ///
    /// Server-only — bails with a clear log if invoked on a client. Mirrors
    /// <see cref="MWI.Farming.FarmGrowthSystem.SpawnHarvestableAt"/> minus the cell-coupling
    /// + FarmGrowthSystem.RegisterHarvestable call.
    /// </summary>
    private void SpawnHarvestableBatch(Vector3 anchor, HarvestableSO so)
    {
        if (!ValidateHarvestableSpawn(so)) return;

        int n = 1;
        if (_countField != null && int.TryParse(_countField.text, out int parsed)) n = Mathf.Max(1, parsed);

        float radius = 4f * Mathf.Sqrt(n);
        int spawned = 0;

        for (int i = 0; i < n; i++)
        {
            Vector3 pos = anchor;
            if (n > 1)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                pos += new Vector3(offset.x, 0f, offset.y);
            }

            // Grid-snap: per-instance lookup means each scattered copy lands on the cell
            // it physically falls on, not all on the anchor's cell. Falls back to the raw
            // XZ when there's no MapController / grid at that point — preserves dev-mode's
            // "spawn anywhere" guarantee for off-grid terrain. Map output is consumed by
            // TryInstantiateHarvestable to re-parent the spawn under the map's hierarchy.
            Vector3 snapped = SnapPositionToGrid(pos, out MapController scatterMap);

            if (TryInstantiateHarvestable(so, snapped, scatterMap, out _)) spawned++;
        }

        string label = !string.IsNullOrEmpty(so.DisplayName) ? so.DisplayName : so.name;
        Debug.Log($"<color=green>[DevSpawn]</color> Spawned {spawned}/{n} '{label}' near {anchor} (radius {radius:F2}u, grid-snapped).");
    }

    /// <summary>
    /// Confirms the ghost-placement spawn. Single instance, grid-snapped (the ghost is already
    /// at the snapped pos), respects the same fall-back-to-free-positioned rule as the scatter
    /// path. After a successful spawn the toggle stays armed and a fresh ghost is rebuilt so
    /// the dev can drop multiple copies without re-arming.
    /// </summary>
    private void ConfirmHarvestableGhostSpawn()
    {
        var so = _harvestableGhostSO;
        if (so == null)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Ghost confirm with no SO selected.");
            return;
        }
        if (!ValidateHarvestableSpawn(so)) return;

        Vector3 pos = _harvestableGhostSnappedPos;
        // Use the map cached on the ghost — already resolved during UpdateHarvestableGhostPosition.
        // Falls back to a fresh GetMapAtPosition lookup if the cache is null (defensive — should
        // never happen since UpdateHarvestableGhostPosition always runs before this confirm fires).
        MapController parentMap = _harvestableGhostMap != null
            ? _harvestableGhostMap
            : MapController.GetMapAtPosition(pos);
        if (!TryInstantiateHarvestable(so, pos, parentMap, out var spawnedH)) return;

        string label = !string.IsNullOrEmpty(so.DisplayName) ? so.DisplayName : so.name;
        string cellInfo = _harvestableGhostIsOnGrid
            ? $"cell=({_harvestableGhostCellX},{_harvestableGhostCellZ}) on '{(parentMap != null ? parentMap.name : "?")}'"
            : (parentMap != null ? $"off-grid on '{parentMap.name}' (free-positioned)" : "off-grid + off-map (scene root)");
        Debug.Log($"<color=green>[DevSpawn]</color> Spawned '{label}' at {pos} ({cellInfo}).");

        // Keep the dev armed for chain-spawning — rebuild the ghost so it follows the cursor
        // for the next placement. Matches "place multiple seeds" UX from CropPlacementManager
        // minus the "consume held seed" gate (dev god mode).
        EnsureHarvestableGhost();
    }

    /// <summary>Shared validation block for harvestable spawn paths (scatter + ghost-confirm).
    /// Returns false (with a log) when the SO is null, has no prefab, or we're on a client.</summary>
    private bool ValidateHarvestableSpawn(HarvestableSO so)
    {
        if (so == null)
        {
            Debug.LogError("<color=red>[DevSpawn]</color> Harvestable spawn called with null SO.");
            return false;
        }
        if (so.HarvestablePrefab == null)
        {
            Debug.LogError($"<color=red>[DevSpawn]</color> HarvestableSO '{so.name}' has no HarvestablePrefab — cannot spawn.");
            return false;
        }
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning("<color=orange>[DevSpawn]</color> Harvestable spawn requested on a client — host-only operation, ignoring.");
            return false;
        }
        return true;
    }

    /// <summary>Server-only. Instantiates one harvestable instance at the given world position,
    /// applies <see cref="Harvestable.InitializeAtStage"/> with the mature stage sentinel +
    /// free-positioning, spawns its NetworkObject when present so clients see it, and re-parents
    /// it under <paramref name="parentMap"/>'s NetworkObject for hierarchy organisation +
    /// hibernation correctness (mirrors <see cref="MWI.Farming.FarmGrowthSystem.SpawnHarvestableAt"/>'s
    /// final step). Pass null <paramref name="parentMap"/> to leave the spawn at scene root.
    /// Returns false when the prefab is missing the Harvestable component or instantiation throws.</summary>
    private bool TryInstantiateHarvestable(HarvestableSO so, Vector3 pos, MapController parentMap, out Harvestable harvestable)
    {
        harvestable = null;
        try
        {
            var go = Instantiate(so.HarvestablePrefab, pos, Quaternion.identity);
            harvestable = go != null ? go.GetComponent<Harvestable>() : null;
            if (harvestable == null)
            {
                Debug.LogError($"<color=red>[DevSpawn]</color> HarvestablePrefab on '{so.name}' has no Harvestable component.");
                if (go != null) Destroy(go);
                return false;
            }

            // Initialize BEFORE Spawn so the replicated NetworkVariables (CropIdNet,
            // CurrentStage, IsDepleted) land in the initial spawn payload and clients
            // can resolve the SO on the first OnNetSyncChanged tick. Mirrors the pre-spawn
            // pattern in FarmGrowthSystem.SpawnHarvestableAt. int.MaxValue gets clamped to
            // crop.DaysToMature internally so crop-aware SOs spawn fully mature.
            harvestable.InitializeAtStage(so, startStage: int.MaxValue, startDepleted: false,
                                          map: null, cellX: -1, cellZ: -1, grid: null);

            if (go.TryGetComponent<NetworkObject>(out var netObj) && !netObj.IsSpawned)
            {
                netObj.Spawn(true);
                // Re-parent under the MapController's NetworkObject so the spawn lives in
                // the same hierarchy as production crop spawns + scene-authored Tree.prefab
                // children — keeps the editor Hierarchy view tidy AND, critically, keeps
                // the harvestable inside the map's hibernation scope. Without this the
                // spawn sits at scene root, ignored by MapController.Hibernate and never
                // serialised when the map sleeps. `worldPositionStays: true` keeps the
                // visual position fixed across the re-parent. Mirrors the final block of
                // FarmGrowthSystem.SpawnHarvestableAt verbatim.
                if (parentMap != null && parentMap.TryGetComponent<NetworkObject>(out var mapNetObj))
                {
                    if (!netObj.TrySetParent(mapNetObj, worldPositionStays: true))
                        Debug.LogWarning($"<color=orange>[DevSpawn]</color> TrySetParent failed for '{so.name}' under map '{parentMap.name}' — falling back to scene root.");
                }
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    // ──────────────────── Harvestable ghost-placement ──────────────────────
    //
    // Canonical placement-aware sub-tab pattern (see .agent/skills/dev-mode/SKILL.md §7.1).
    // Any new dev-spawn sub-tab that drops a *world entity that exists on the TerrainCellGrid*
    // (Building, Furniture-in-world, NPC-in-formation, etc.) MUST mirror this shape verbatim:
    //   1. Ghost state fields (instance + SO + snapped pos + on-grid + cell coords + map/grid refs + warn-once flags)
    //   2. Lifecycle hooks (HandleArmedChanged, Show*SubTab, HandleXxxDropdownChanged, HandleDevModeChanged, OnDestroy)
    //   3. EnsureXxxGhost builder (Instantiate → DisableGhostInterference → TintGhost neutral → UpdateXxxGhostPosition once)
    //   4. UpdateXxxGhostPosition (raycast → MapController.GetMapAtPosition → grid.WorldToGrid → grid.GridToWorld + preserve hit.y → tint green/yellow)
    //   5. SnapPositionToGrid + EnsureGridInitialized (defensive bootstrap from BoxCollider bounds)
    //   6. DisableGhostInterference (extended per entity type to disable the entity's specific scripts)
    //   7. ConfirmXxxGhostSpawn (LMB → spawn at _xxxGhostSnappedPos via shared TryInstantiateXxx → EnsureXxxGhost re-arm for chain-spawn)
    //   8. TryInstantiateXxx MUST re-parent under MapController.NetworkObject via TrySetParent
    //      (worldPositionStays:true) — without this the spawn lives at scene root, outside the
    //      map's hibernation scope, and never serialises when the map sleeps.
    // The Space+LMB scatter path MUST also grid-snap per-instance (see SpawnHarvestableBatch).
    // Click-and-spawn-at-raw-hit-point is only acceptable for non-spatial drops (Item / Character).

    /// <summary>
    /// Snaps <paramref name="worldPos"/> to its containing <see cref="TerrainCellGrid"/> cell
    /// and outputs the resolved <see cref="MapController"/> so the spawn path can re-parent
    /// under it. Resolves the map at the point, bootstraps the grid if needed (mirrors
    /// <see cref="MWI.Farming.CropPlacementManager.EnsureGridInitialized"/>), and returns the
    /// grid-anchored world position. Falls back to <paramref name="worldPos"/> unchanged
    /// (with <paramref name="map"/> = null) when no map / grid is available or the point is
    /// outside grid bounds — dev-mode "spawn anywhere" semantics preserved.
    /// </summary>
    private Vector3 SnapPositionToGrid(Vector3 worldPos, out MapController map)
    {
        map = MapController.GetMapAtPosition(worldPos);
        if (map == null) return worldPos;
        EnsureGridInitialized(map);
        var grid = map.GetComponent<TerrainCellGrid>();
        if (grid == null) return worldPos;
        if (!grid.WorldToGrid(worldPos, out int x, out int z)) return worldPos;
        var snapped = grid.GridToWorld(x, z);
        // Preserve Y from the original raycast hit point so the harvestable lands on the
        // ground surface rather than at the cell's nominal Y (which is grid-plane Y).
        snapped.y = worldPos.y;
        return snapped;
    }

    /// <summary>Defensive bootstrap — copy of CropPlacementManager.EnsureGridInitialized so dev
    /// mode doesn't depend on the live farming system having visited this map first. The
    /// TerrainCellGrid has an Initialize(Bounds) but no caller in the existing terrain pipeline
    /// invokes it for a live (non-hibernated) map.</summary>
    private static void EnsureGridInitialized(MapController map)
    {
        var grid = map.GetComponent<TerrainCellGrid>();
        if (grid == null) return;
        if (grid.Width > 0 && grid.Depth > 0) return;
        var box = map.GetComponent<BoxCollider>();
        if (box == null)
        {
            Debug.LogError($"<color=red>[DevSpawn]</color> {map.name} has no BoxCollider — cannot bootstrap TerrainCellGrid.");
            return;
        }
        grid.Initialize(box.bounds);
        Debug.Log($"<color=cyan>[DevSpawn]</color> Bootstrapped TerrainCellGrid on {map.name} from BoxCollider bounds (Width={grid.Width}, Depth={grid.Depth}).");
    }

    /// <summary>(Re)creates the ghost instance from the currently-selected HarvestableSO.
    /// No-op if the sub-tab isn't Harvestable, dev mode is off, or the toggle is not armed —
    /// the caller is expected to gate. Stripping mirrors
    /// <see cref="MWI.Farming.CropPlacementManager.DisableGhostInterference"/>: disable
    /// NetworkObject, kinematic Rigidbody, disable colliders + NavMeshObstacles, move to
    /// Ignore Raycast layer.</summary>
    private void EnsureHarvestableGhost()
    {
        ClearHarvestableGhost();

        if (_harvestableDropdown == null || _harvestables == null || _harvestables.Count == 0) return;
        int idx = _harvestableDropdown.value;
        if (idx < 0 || idx >= _harvestables.Count) return;
        var so = _harvestables[idx];
        if (so == null || so.HarvestablePrefab == null) return;

        _harvestableGhostSO = so;
        _harvestableGhost = Instantiate(so.HarvestablePrefab);
        _harvestableGhost.name = "DevHarvestableGhost_" + (string.IsNullOrEmpty(so.Id) ? so.name : so.Id);
        DisableGhostInterference(_harvestableGhost);
        TintGhost(_harvestableGhost, 1f, 1f, 1f, 0.7f); // neutral until UpdateGhostPosition runs

        _warnedNoCameraGhost = _warnedRayMissGhost = false;

        // Run one frame of positioning so the ghost is at the cursor immediately (no flicker
        // at world origin on the first frame after Instantiate).
        UpdateHarvestableGhostPosition();
    }

    private void ClearHarvestableGhost()
    {
        if (_harvestableGhost != null)
        {
            Destroy(_harvestableGhost);
            _harvestableGhost = null;
        }
        _harvestableGhostSO = null;
        _harvestableGhostMap = null;
        _harvestableGhostGrid = null;
        _harvestableGhostIsOnGrid = false;
        _harvestableGhostCellX = _harvestableGhostCellZ = -1;
    }

    /// <summary>Raycast cursor → snap to grid cell → move ghost. Visual tint: green on grid,
    /// yellow off-grid (still spawnable in dev mode).</summary>
    private void UpdateHarvestableGhostPosition()
    {
        if (_harvestableGhost == null) return;
        if (Camera.main == null)
        {
            if (!_warnedNoCameraGhost)
            {
                Debug.LogWarning("<color=orange>[DevSpawn]</color> Camera.main is null — ghost cannot follow cursor.");
                _warnedNoCameraGhost = true;
            }
            return;
        }

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _environmentLayerMask))
        {
            if (!_warnedRayMissGhost)
            {
                Debug.LogWarning("<color=orange>[DevSpawn]</color> Ghost raycast missed Environment layer — leaving ghost at last position.");
                _warnedRayMissGhost = true;
            }
            return;
        }
        // Reset warn flag once we have a valid hit again.
        _warnedRayMissGhost = false;

        // Default: ghost sits at hit point (off-grid, yellow tint).
        Vector3 finalPos = hit.point;
        bool onGrid = false;
        int cellX = -1, cellZ = -1;
        MapController map = MapController.GetMapAtPosition(hit.point);
        TerrainCellGrid grid = null;
        if (map != null)
        {
            EnsureGridInitialized(map);
            grid = map.GetComponent<TerrainCellGrid>();
            if (grid != null && grid.WorldToGrid(hit.point, out cellX, out cellZ))
            {
                var snapped = grid.GridToWorld(cellX, cellZ);
                snapped.y = hit.point.y;
                finalPos = snapped;
                onGrid = true;
            }
        }

        _harvestableGhost.transform.position = finalPos;
        _harvestableGhostSnappedPos = finalPos;
        _harvestableGhostIsOnGrid = onGrid;
        _harvestableGhostMap = map;
        _harvestableGhostGrid = grid;
        _harvestableGhostCellX = cellX;
        _harvestableGhostCellZ = cellZ;

        // Green when on grid, yellow when off (still spawnable in dev god mode).
        if (onGrid) TintGhost(_harvestableGhost, 0.6f, 1f, 0.6f, 0.75f);
        else TintGhost(_harvestableGhost, 1f, 1f, 0.4f, 0.7f);
    }

    /// <summary>Strip everything that would interfere with a passive cursor-follower: network
    /// identity (clients shouldn't see the ghost), colliders (don't block raycast), rigidbodies
    /// (don't fall), NavMeshObstacles (don't carve the navmesh as the cursor sweeps), and put
    /// the whole tree on the Ignore Raycast layer. Also disables the <see cref="Harvestable"/>
    /// component itself so its Awake-bound Update polling doesn't run on the ghost.</summary>
    private static void DisableGhostInterference(GameObject ghost)
    {
        if (ghost.TryGetComponent(out NetworkObject netObj)) netObj.enabled = false;
        if (ghost.TryGetComponent(out Rigidbody rb)) rb.isKinematic = true;
        foreach (var col in ghost.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var obs in ghost.GetComponentsInChildren<NavMeshObstacle>(true)) obs.enabled = false;
        // Disable the Harvestable script + its NetSync sibling so they don't poll NetVars
        // on an unspawned NetworkObject (would NPE on the NetSync.CurrentStage read path).
        foreach (var h in ghost.GetComponentsInChildren<Harvestable>(true)) h.enabled = false;
        foreach (var ns in ghost.GetComponentsInChildren<HarvestableNetSync>(true)) ns.enabled = false;
        int ignoreLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreLayer >= 0) SetLayerRecursive(ghost, ignoreLayer);
    }

    private static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
    }

    /// <summary>Tints every SpriteRenderer + every MeshRenderer's instanced material with the
    /// given color. Sprite path is the common case for harvestable prefabs (2D-sprites-in-3D-
    /// environment per project rule 17); mesh fallback handles future 3D harvestables.</summary>
    private static void TintGhost(GameObject ghost, float r, float g, float b, float a)
    {
        Color c = new Color(r, g, b, a);
        foreach (var sr in ghost.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = c;
        foreach (var mr in ghost.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr.material != null) mr.material.color = c;
        }
    }

    private CharacterPersonalitySO ResolvePersonality()
    {
        if (_personalityDropdown == null || _personalities.Count == 0) return null;
        int v = _personalityDropdown.value;
        if (v == 0) return null; // Random — SpawnManager handles it
        int idx = v - 1;
        return (idx >= 0 && idx < _personalities.Count) ? _personalities[idx] : null;
    }

    private CharacterBehavioralTraitsSO ResolveTrait()
    {
        if (_traitDropdown == null || _traits.Count == 0) return null;
        int v = _traitDropdown.value;
        if (v == 0) return null;
        int idx = v - 1;
        return (idx >= 0 && idx < _traits.Count) ? _traits[idx] : null;
    }

    private List<(CombatStyleSO, int)> BuildCombatList()
    {
        if (_combatRows.Count == 0) return null;
        var list = new List<(CombatStyleSO, int)>();
        foreach (var row in _combatRows)
        {
            if (row == null) continue;
            int i = row.SelectedIndex;
            if (i < 0 || i >= _combatStyles.Count) continue;
            list.Add((_combatStyles[i], row.Level));
        }
        return list.Count > 0 ? list : null;
    }

    private List<(SkillSO, int)> BuildSkillList()
    {
        if (_skillRows.Count == 0) return null;
        var list = new List<(SkillSO, int)>();
        foreach (var row in _skillRows)
        {
            if (row == null) continue;
            int i = row.SelectedIndex;
            if (i < 0 || i >= _skills.Count) continue;
            list.Add((_skills[i], row.Level));
        }
        return list.Count > 0 ? list : null;
    }
}
