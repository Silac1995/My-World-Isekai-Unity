using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

    private readonly List<DevSpawnRow> _combatRows = new List<DevSpawnRow>();
    private readonly List<DevSpawnRow> _skillRows = new List<DevSpawnRow>();

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

        Debug.Log($"<color=cyan>[DevSpawn]</color> Catalogs loaded — races:{_races.Count} personalities:{_personalities.Count} traits:{_traits.Count} combat:{_combatStyles.Count} skills:{_skills.Count}");
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

    // ─── Listener wiring ─────────────────────────────────────────────

    private void WireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.AddListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.AddListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.AddListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.AddListener(HandleArmedChanged);

        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            DevModeManager.Instance.OnClickConsumerChanged += HandleClickConsumerChanged;
        }
    }

    private void UnwireListeners()
    {
        if (_raceDropdown != null) _raceDropdown.onValueChanged.RemoveListener(HandleRaceChanged);
        if (_addCombatStyleButton != null) _addCombatStyleButton.onClick.RemoveListener(AddCombatRow);
        if (_addSkillButton != null) _addSkillButton.onClick.RemoveListener(AddSkillRow);
        if (_armedToggle != null) _armedToggle.onValueChanged.RemoveListener(HandleArmedChanged);

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
    }

    private void Update()
    {
        if (DevModeManager.Instance == null || !DevModeManager.Instance.IsEnabled) return;

        // Global shortcut: Space + Right-Click spawns at cursor, any tab, any armed state.
        HandleShortcut();

        // Armed click-loop (legacy path — kept for discoverability via the toggle).
        if (_armedToggle == null || !_armedToggle.isOn) return;
        if (DevModeManager.Instance.ActiveClickConsumer != this) return;

        // Escape disarms the toggle in armed mode.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _armedToggle.isOn = false;
            return;
        }

        // If Space is held, the Spawn shortcut handles the click; if Ctrl is held the Select
        // shortcut owns the click — either way, don't let the armed Spawn loop double-fire.
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

    // ─── Shortcut ─────────────────────────────────────────────────────

    /// <summary>
    /// Space held + Left-Click anywhere on the environment spawns at the cursor using the panel's
    /// current configuration (race / personality / combat styles / count). Skipped when a text input
    /// field has focus so typing spaces in the Count field doesn't trigger a spawn.
    /// </summary>
    private void HandleShortcut()
    {
        if (IsTextInputFocused()) return;
        if (!Input.GetKey(KeyCode.Space)) return;
        // If Ctrl is also held, the Select shortcut owns the click (mutually exclusive).
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) return;
        if (!Input.GetMouseButtonDown(0)) return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (TryRaycastEnvironment(out Vector3 hitPoint))
        {
            Debug.Log("<color=cyan>[DevSpawn]</color> Space+LMB shortcut — spawning at cursor.");
            SpawnAt(hitPoint);
        }
    }

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

    private static bool IsTextInputFocused()
    {
        if (EventSystem.current == null) return false;
        var sel = EventSystem.current.currentSelectedGameObject;
        if (sel == null) return false;
        return sel.GetComponent<TMP_InputField>() != null
            || sel.GetComponent<UnityEngine.UI.InputField>() != null;
    }

    private void SpawnAt(Vector3 anchor)
    {
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
