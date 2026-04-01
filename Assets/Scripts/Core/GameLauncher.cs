// Assets/Scripts/Core/GameLauncher.cs
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using MWI.WorldSystem;

/// <summary>
/// Orchestrates the full game load sequence: fade out -> scene load -> world restore ->
/// wait for player spawn -> import profile + position -> spawn party NPCs -> fade in.
///
/// Singleton with DontDestroyOnLoad. Works WITH GameSessionManager's existing spawn flow
/// rather than replacing it. GameSessionManager still handles network start and player
/// object instantiation. GameLauncher hooks into the spawn callback to finish setup
/// (profile import, positioning, party NPC spawning) after the player object exists.
/// </summary>
public class GameLauncher : MonoBehaviour
{
    public static GameLauncher Instance { get; private set; }

    // ── Launch Parameters (set before calling Launch) ───────────────
    public string SelectedWorldGuid { get; set; }
    public string SelectedCharacterGuid { get; set; }
    public bool IsNewWorld { get; set; }

    /// <summary>
    /// True while a launch sequence is in progress (from Launch() to final FadeIn).
    /// Prevents double-launches and lets other systems know setup is pending.
    /// </summary>
    public bool IsLaunching { get; private set; }

    /// <summary>
    /// Indicates that GameLauncher has a pending character profile to import
    /// after the player object is spawned. Other systems can check this to
    /// defer initialization that depends on profile data.
    /// </summary>
    public static bool HasPendingProfile =>
        Instance != null && !string.IsNullOrEmpty(Instance.SelectedCharacterGuid);

    [Header("Scene")]
    [SerializeField] private string _gameSceneName = "GameScene";

    [Header("Timing")]
    [SerializeField] private float _fadeDuration = 0.5f;
    [SerializeField] private float _maxSpawnWaitSeconds = 15f;

    private const string LOG_TAG = "<color=magenta>[GameLauncher]</color>";

    private Coroutine _launchCoroutine;

    // ── Singleton Lifecycle ─────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (_launchCoroutine != null)
        {
            StopCoroutine(_launchCoroutine);
            _launchCoroutine = null;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    /// <summary>
    /// Begins the full launch sequence. Call after setting SelectedWorldGuid,
    /// SelectedCharacterGuid, and IsNewWorld.
    ///
    /// For solo/host: sets GameSessionManager flags, fades out, loads scene,
    /// waits for network + player spawn, imports profile, positions character,
    /// spawns party NPCs, then fades in.
    /// </summary>
    public void LaunchSolo()
    {
        if (IsLaunching)
        {
            Debug.LogWarning($"{LOG_TAG} Launch already in progress — ignoring duplicate call.");
            return;
        }

        Debug.Log($"{LOG_TAG} LaunchSolo requested. World={SelectedWorldGuid}, Character={SelectedCharacterGuid}, IsNew={IsNewWorld}");

        _launchCoroutine = StartCoroutine(LaunchSequence());
    }

    // ── Launch Coroutine ────────────────────────────────────────────

    private IEnumerator LaunchSequence()
    {
        IsLaunching = true;

        if (SaveManager.Instance != null) SaveManager.Instance.CurrentState = SaveManager.SaveLoadState.Loading;

        // ── Step 1: Fade to black with status overlay ───────────────
        if (ScreenFadeManager.Instance != null)
        {
            ScreenFadeManager.Instance.ShowOverlay(1.0f, "Loading...");
        }

        // ── Step 2: Configure GameSessionManager for host/solo ──────
        GameSessionManager.AutoStartNetwork = true;
        GameSessionManager.IsHost = true;

        // ── Step 3: Load the game scene ─────────────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Loading scene...");
        Debug.Log($"{LOG_TAG} Loading scene '{_gameSceneName}'...");
        AsyncOperation sceneLoad = SceneManager.LoadSceneAsync(_gameSceneName);
        if (sceneLoad == null)
        {
            Debug.LogError($"{LOG_TAG} Failed to start loading scene '{_gameSceneName}'.");
            yield return ReturnToMainMenuWithError($"Failed to load scene '{_gameSceneName}'.");
            yield break;
        }

        while (!sceneLoad.isDone)
        {
            yield return null;
        }

        Debug.Log($"{LOG_TAG} Scene '{_gameSceneName}' loaded.");

        // ── Step 4: Wait for network to start and player to spawn ───
        // On first load, GameSessionManager.Start() auto-starts the network.
        // On subsequent loads (DontDestroyOnLoad), Start() doesn't fire again,
        // so we explicitly trigger CheckAutoStart().
        if (GameSessionManager.Instance != null)
        {
            GameSessionManager.Instance.EnsureCallbacksRegistered();
            GameSessionManager.Instance.CheckAutoStart();
        }

        ScreenFadeManager.Instance?.UpdateStatus("Spawning player...");
        Character playerCharacter = null;
        yield return WaitForPlayerSpawn(result => playerCharacter = result);

        if (playerCharacter == null)
        {
            Debug.LogError($"{LOG_TAG} Player character never spawned — returning to main menu.");
            yield return ReturnToMainMenuWithError("Failed to spawn player character. The game could not start.");
            yield break;
        }

        Debug.Log($"{LOG_TAG} Player character found: {playerCharacter.gameObject.name}");

        // ── Step 5: Wait for ISaveable settling, then load world ─────
        // SaveManager.IsReady uses settling-based detection to know when
        // all ISaveable systems have finished registering.
        ScreenFadeManager.Instance?.UpdateStatus("Waiting for world systems...");
        float timeout = 10f;
        float elapsed = 0f;
        while (SaveManager.Instance != null && !SaveManager.Instance.IsReady && elapsed < timeout)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        if (SaveManager.Instance != null && !SaveManager.Instance.IsReady)
        {
            Debug.LogWarning($"{LOG_TAG} Timeout waiting for ISaveable registration — proceeding anyway.");
            ScreenFadeManager.Instance?.ShowWarning("Some world systems may not have loaded.");
        }

        ScreenFadeManager.Instance?.UpdateStatus("Restoring world data...");
        yield return LoadWorldData();

        // ── Step 5b: Spawn saved buildings on predefined maps ──────
        // Predefined maps never WakeUp(), so buildings from CommunityData
        // must be spawned explicitly after the world is loaded.
        if (!IsNewWorld)
        {
            ScreenFadeManager.Instance?.UpdateStatus("Spawning buildings...");
            foreach (var mc in Object.FindObjectsByType<MapController>(FindObjectsSortMode.None))
            {
                if (mc.IsPredefinedMap)
                    mc.SpawnSavedBuildings();
            }
        }

        // ── Step 5c: Spawn NPCs from pending snapshots ─────────────
        // MapController.OnNetworkSpawn fires before LoadWorldAsync populates PendingSnapshots,
        // so the snapshot consumption in OnNetworkSpawn finds nothing. We must spawn them here.
        if (!IsNewWorld && MapController.PendingSnapshots.Count > 0)
        {
            ScreenFadeManager.Instance?.UpdateStatus("Spawning NPCs...");
            foreach (var mc in Object.FindObjectsByType<MapController>(FindObjectsSortMode.None))
            {
                if (MapController.PendingSnapshots.TryGetValue(mc.MapId, out var snapshot))
                {
                    Debug.Log($"{LOG_TAG} Spawning {snapshot.HibernatedNPCs.Count} NPC(s) from snapshot for map '{mc.MapId}'.");
                    mc.SpawnNPCsFromPendingSnapshot();
                }
            }
        }

        // ── Step 6: Import character profile ────────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Loading character profile...");
        CharacterProfileSaveData profileData = null;
        yield return LoadAndImportProfile(playerCharacter, result => profileData = result);

        // ── Step 6b: Set GameObject name and initialize PlayerUI ────
        if (profileData != null)
        {
            playerCharacter.gameObject.name = profileData.characterName;
        }

        var playerUI = Object.FindFirstObjectByType<PlayerUI>(FindObjectsInactive.Include);
        if (playerUI != null)
        {
            playerUI.Initialize(playerCharacter.gameObject);
            Debug.Log($"{LOG_TAG} PlayerUI initialized with '{playerCharacter.CharacterName}'.");
        }

        // ── Step 7: Position the character ──────────────────────────
        ScreenFadeManager.Instance?.UpdateStatus("Positioning character...");
        PositionCharacter(playerCharacter, profileData);

        // ── Step 8: Spawn party NPC members ─────────────────────────
        if (profileData != null && profileData.partyMembers.Count > 0)
        {
            ScreenFadeManager.Instance?.UpdateStatus("Spawning party members...");
            yield return SpawnPartyMembers(playerCharacter, profileData);
        }

        // ── Step 9: Fade in ─────────────────────────────────────────
        // Small delay to let NavMesh, physics settle after positioning
        yield return new WaitForSecondsRealtime(0.2f);

        // ── Step 10: Enable debug tools, hide session buttons ─────
        var debugScript = Object.FindFirstObjectByType<DebugScript>(FindObjectsInactive.Include);
        if (debugScript != null)
            debugScript.gameObject.SetActive(true);

        var sessionManager = Object.FindFirstObjectByType<UI_SessionManager>(FindObjectsInactive.Include);
        if (sessionManager != null)
            sessionManager.HideSessionButtons();

        FadeInSafely();

        if (SaveManager.Instance != null) SaveManager.Instance.CurrentState = SaveManager.SaveLoadState.Idle;
        IsLaunching = false;
        _launchCoroutine = null;

        Debug.Log($"{LOG_TAG} Launch sequence complete.");
    }

    // ── Sub-steps ───────────────────────────────────────────────────

    /// <summary>
    /// Loads the world save data via SaveManager if this is an existing world.
    /// For new worlds, generates a fresh GUID and sets it on SaveManager.
    /// </summary>
    private IEnumerator LoadWorldData()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogWarning($"{LOG_TAG} SaveManager not found — skipping world data load.");
            yield break;
        }

        if (IsNewWorld)
        {
            // New world: generate GUID if not already set
            if (string.IsNullOrEmpty(SelectedWorldGuid))
            {
                SelectedWorldGuid = System.Guid.NewGuid().ToString("N");
            }

            SaveManager.Instance.CurrentWorldGuid = SelectedWorldGuid;
            SaveManager.Instance.CurrentWorldName = "New World";
            Debug.Log($"{LOG_TAG} New world created with GUID: {SelectedWorldGuid}");
        }
        else if (!string.IsNullOrEmpty(SelectedWorldGuid))
        {
            Debug.Log($"{LOG_TAG} Loading world data for GUID: {SelectedWorldGuid}...");

            var loadTask = SaveManager.Instance.LoadWorldAsync(SelectedWorldGuid);

            // Wait for the async task to complete (bridge async -> coroutine)
            while (!loadTask.IsCompleted)
            {
                yield return null;
            }

            if (loadTask.IsFaulted)
            {
                Debug.LogError($"{LOG_TAG} World load failed: {loadTask.Exception?.InnerException?.Message}");
            }
            else
            {
                Debug.Log($"{LOG_TAG} World data loaded successfully.");
            }
        }
    }

    /// <summary>
    /// Waits for the local player's NetworkObject to be spawned by GameSessionManager.
    /// Uses NetworkManager.LocalClient.PlayerObject to detect the spawn.
    /// </summary>
    private IEnumerator WaitForPlayerSpawn(System.Action<Character> onResult)
    {
        Debug.Log($"{LOG_TAG} Waiting for player object spawn...");

        float elapsed = 0f;

        while (elapsed < _maxSpawnWaitSeconds)
        {
            // NetworkManager might not exist yet right after scene load
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.IsListening &&
                NetworkManager.Singleton.LocalClient != null &&
                NetworkManager.Singleton.LocalClient.PlayerObject != null)
            {
                var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
                if (playerObj.TryGetComponent(out Character character))
                {
                    onResult?.Invoke(character);
                    yield break;
                }
                else
                {
                    Debug.LogError($"{LOG_TAG} Player object exists but has no Character component!");
                    onResult?.Invoke(null);
                    yield break;
                }
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogError($"{LOG_TAG} Timed out after {_maxSpawnWaitSeconds}s waiting for player spawn.");
        onResult?.Invoke(null);
    }

    /// <summary>
    /// Reads the character profile from disk and imports it into the spawned player.
    /// If no profile GUID is set or the file doesn't exist, the character keeps its
    /// default spawned state.
    /// </summary>
    private IEnumerator LoadAndImportProfile(Character character, System.Action<CharacterProfileSaveData> onResult)
    {
        if (string.IsNullOrEmpty(SelectedCharacterGuid))
        {
            Debug.Log($"{LOG_TAG} No character GUID selected — player keeps default state.");
            onResult?.Invoke(null);
            yield break;
        }

        if (!SaveFileHandler.ProfileExists(SelectedCharacterGuid))
        {
            Debug.Log($"{LOG_TAG} No profile file found for '{SelectedCharacterGuid}' — player keeps default state.");
            onResult?.Invoke(null);
            yield break;
        }

        Debug.Log($"{LOG_TAG} Loading character profile '{SelectedCharacterGuid}' from disk...");

        var readTask = SaveFileHandler.ReadProfileAsync(SelectedCharacterGuid);
        while (!readTask.IsCompleted)
        {
            yield return null;
        }

        CharacterProfileSaveData profileData = readTask.Result;

        if (profileData == null)
        {
            Debug.LogWarning($"{LOG_TAG} Profile read returned null for '{SelectedCharacterGuid}'.");
            onResult?.Invoke(null);
            yield break;
        }

        // Import the profile into the character via CharacterDataCoordinator
        var coordinator = character.GetComponent<CharacterDataCoordinator>();
        if (coordinator != null)
        {
            coordinator.ImportProfile(profileData);
            Debug.Log($"{LOG_TAG} Profile imported: '{profileData.characterName}' ({profileData.characterGuid})");
        }
        else
        {
            Debug.LogError($"{LOG_TAG} Player character has no CharacterDataCoordinator — cannot import profile.");
        }

        onResult?.Invoke(profileData);
    }

    /// <summary>
    /// Positions the character at their last known world position (from WorldAssociation),
    /// or falls back to SpawnManager.DefaultSpawnPosition.
    /// </summary>
    private void PositionCharacter(Character character, CharacterProfileSaveData profileData)
    {
        Vector3 targetPosition = GetSpawnPosition(profileData);

        // Use CharacterMovement.Warp for proper NavMesh + Rigidbody handling
        var movement = character.CharacterMovement;
        if (movement != null)
        {
            movement.Warp(targetPosition);
            Debug.Log($"{LOG_TAG} Warped player to {targetPosition}.");
        }
        else
        {
            // Fallback: directly set transform
            character.transform.position = targetPosition;
            Debug.LogWarning($"{LOG_TAG} No CharacterMovement found — set transform.position directly to {targetPosition}.");
        }
    }

    /// <summary>
    /// Resolves the spawn position from the profile's WorldAssociation for the current world,
    /// falling back to SpawnManager's default position.
    /// </summary>
    private Vector3 GetSpawnPosition(CharacterProfileSaveData profileData)
    {
        // Try to find saved position in WorldAssociation
        if (profileData != null && !string.IsNullOrEmpty(SelectedWorldGuid))
        {
            var association = profileData.worldAssociations?.Find(w => w.worldGuid == SelectedWorldGuid);
            if (association != null)
            {
                Vector3 savedPos = new Vector3(association.positionX, association.positionY, association.positionZ);

                // Sanity check: don't use zero/origin positions that likely mean "never saved"
                if (savedPos.sqrMagnitude > 0.01f)
                {
                    Debug.Log($"{LOG_TAG} Using saved WorldAssociation position: {savedPos}");
                    return savedPos;
                }
            }
        }

        // Fallback: SpawnManager default
        if (SpawnManager.Instance != null)
        {
            Vector3 defaultPos = SpawnManager.Instance.DefaultSpawnPosition;
            Debug.Log($"{LOG_TAG} Using SpawnManager default position: {defaultPos}");
            return defaultPos;
        }

        Debug.LogWarning($"{LOG_TAG} No SpawnManager found — using Vector3.zero as fallback spawn position.");
        return Vector3.zero;
    }

    /// <summary>
    /// Spawns party NPC members from the saved profile data.
    /// Each NPC is spawned via SpawnManager, then has its profile imported via CharacterDataCoordinator.
    /// NPCs are positioned near the player character.
    /// </summary>
    private IEnumerator SpawnPartyMembers(Character leader, CharacterProfileSaveData leaderProfile)
    {
        if (SpawnManager.Instance == null)
        {
            Debug.LogWarning($"{LOG_TAG} SpawnManager not found — cannot spawn party NPCs.");
            yield break;
        }

        Debug.Log($"{LOG_TAG} Spawning {leaderProfile.partyMembers.Count} party NPC member(s)...");

        // Determine if we're loading into the same world these NPCs came from.
        // If so, they may already exist from the NPC snapshot — don't duplicate.
        string currentWorldGuid = SaveManager.Instance?.CurrentWorldGuid;
        bool isSoloSession = true; // Solo/host mode — duplicates not allowed in same world

        // Step 1: Create party for the leader
        CharacterParty leaderParty = null;
        leader.TryGet(out leaderParty);
        if (leaderParty != null && !leaderParty.IsInParty)
        {
            leaderParty.CreateParty();
            Debug.Log($"{LOG_TAG} Created party for leader '{leader.CharacterName}'.");
        }

        Vector3 leaderPos = leader.transform.position;
        int memberIndex = 0;

        foreach (var memberProfile in leaderProfile.partyMembers)
        {
            // Check if this NPC already exists in the world (from NPC snapshot).
            // In solo/host mode loading the SAME world, the NPC was already spawned
            // by SpawnNPCsFromPendingSnapshot — don't spawn a duplicate.
            Character existingNPC = null;
            if (isSoloSession && !string.IsNullOrEmpty(memberProfile.characterGuid))
            {
                existingNPC = Character.FindByUUID(memberProfile.characterGuid);
            }

            Character npcCharacter;

            if (existingNPC != null)
            {
                // NPC already exists in this world — reconnect, don't spawn
                npcCharacter = existingNPC;
                Debug.Log($"{LOG_TAG} Party NPC '{memberProfile.characterName}' already in world — reconnecting.");
            }
            else
            {
                // NPC doesn't exist — spawn fresh from profile
                float angle = (360f / leaderProfile.partyMembers.Count) * memberIndex;
                float radius = 1.5f;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    0f,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );
                Vector3 spawnPos = leaderPos + offset;

                // Get the NPC prefab — use the default character prefab
                GameObject npcPrefab = NetworkManager.Singleton?.NetworkConfig?.PlayerPrefab;
                if (npcPrefab == null)
                {
                    Debug.LogError($"{LOG_TAG} No NPC prefab available for '{memberProfile.characterName}' — skipping.");
                    memberIndex++;
                    continue;
                }

                GameObject npcGO = Instantiate(npcPrefab, spawnPos, Quaternion.identity);
                var netObj = npcGO.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn(true);
                }

                npcCharacter = npcGO.GetComponent<Character>();
                if (npcCharacter == null)
                {
                    Debug.LogError($"{LOG_TAG} Spawned NPC has no Character component — skipping.");
                    memberIndex++;
                    continue;
                }

                // Wait a frame for NetworkObject to initialize
                yield return null;

                // Import profile
                var npcCoordinator = npcCharacter.GetComponent<CharacterDataCoordinator>();
                if (npcCoordinator != null)
                {
                    npcCoordinator.ImportProfile(memberProfile);
                }

                npcCharacter.gameObject.name = memberProfile.characterName;
                Debug.Log($"{LOG_TAG} Party NPC '{memberProfile.characterName}' spawned and profile imported.");
            }

            // Re-form party — join the leader's party
            if (leaderParty != null && leaderParty.IsInParty)
            {
                npcCharacter.TryGet(out CharacterParty npcParty);
                if (npcParty != null && !npcParty.IsInParty)
                {
                    npcParty.JoinParty(leaderParty.PartyData.PartyId);
                    Debug.Log($"{LOG_TAG} Party NPC '{npcCharacter.CharacterName}' joined leader's party.");
                }
            }

            memberIndex++;
        }

        Debug.Log($"{LOG_TAG} Finished spawning {memberIndex} party member(s).");
    }

    // ── Utilities ───────────────────────────────────────────────────

    /// <summary>
    /// Safely triggers fade-in even if ScreenFadeManager is missing.
    /// </summary>
    /// <summary>
    /// Shows error on overlay, shuts down network, resets state, and returns to main menu.
    /// Called when a critical failure occurs during the launch sequence.
    /// </summary>
    private IEnumerator ReturnToMainMenuWithError(string errorMessage)
    {
        Debug.LogError($"{LOG_TAG} Critical launch error: {errorMessage}");

        // Show error on overlay
        ScreenFadeManager.Instance?.ShowOverlay(1.0f, "Error");
        ScreenFadeManager.Instance?.ShowWarning(errorMessage);
        yield return new WaitForSecondsRealtime(3f); // Let player read the error

        // Shutdown network and wait for it to complete
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            // Wait for shutdown to complete
            while (NetworkManager.Singleton != null && NetworkManager.Singleton.ShutdownInProgress)
                yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
        }

        // Reset GameSessionManager callbacks (shutdown clears them)
        if (GameSessionManager.Instance != null)
            GameSessionManager.Instance.ResetCallbacks();

        // Reset all state
        ClearLaunchParameters();

        // Clear overlay before scene load
        ScreenFadeManager.Instance?.HideOverlay(0f);

        // Return to main menu
        SceneManager.LoadScene("MainMenuScene");
    }

    private void FadeInSafely()
    {
        if (ScreenFadeManager.Instance != null)
        {
            ScreenFadeManager.Instance.HideOverlay(_fadeDuration);
        }
    }

    /// <summary>
    /// Resets the launch parameters. Call this when returning to main menu
    /// or when the launch is no longer relevant.
    /// </summary>
    public void ClearLaunchParameters()
    {
        SelectedWorldGuid = null;
        SelectedCharacterGuid = null;
        IsNewWorld = false;
        IsLaunching = false;

        // Reset SaveManager state for fresh session
        if (SaveManager.Instance != null)
            SaveManager.Instance.ResetForNewSession();
    }
}
