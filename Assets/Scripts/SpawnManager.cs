using UnityEngine;
using System.Linq;
using System.Collections.Generic;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
    [SerializeField] private GameObject _defaultItemPrefab;
    [SerializeField] private RaceSO _defaultFallbackRace;

    private struct PendingDevConfig
    {
        public CharacterBehavioralTraitsSO Traits;
        public List<(CombatStyleSO style, int level)> CombatStyles;
        public List<(SkillSO skill, int level)> Skills;
    }

    private readonly Dictionary<ulong, PendingDevConfig> _pendingDevConfig = new Dictionary<ulong, PendingDevConfig>();

    public Vector3 DefaultSpawnPosition => spawnGameObject != null ? spawnGameObject.transform.position : Vector3.zero;
    public Quaternion DefaultSpawnRotation => spawnGameObject != null ? spawnGameObject.transform.rotation : Quaternion.identity;

    private CharacterPersonalitySO[] _availablePersonalities;
    private CharacterBehavioralTraitsSO[] _availableTraits;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"Un autre SpawnManager existe déjà. Destruction de {gameObject.name}.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Chargement des personnalités pour le spawn aléatoire
        _availablePersonalities = Resources.LoadAll<CharacterPersonalitySO>("Data/Personnality");
        Debug.Log($"<color=cyan>[SpawnManager]</color> {_availablePersonalities.Length} personnalités chargées.");

        // Chargement des traits comportementaux
        _availableTraits = Resources.LoadAll<CharacterBehavioralTraitsSO>("Data/Behavioural Traits");
        Debug.Log($"<color=cyan>[SpawnManager]</color> {_availableTraits.Length} profils comportementaux chargés.");

        if (spawnGameObject == null)
        {
            Debug.LogWarning("spawnGameObject n'est pas assigné dans l'inspecteur. Utilisation de la position fournie pour l'instanciation.", this);
        }
        else
        {
            Debug.Log($"Position de spawnGameObject : {spawnGameObject.transform.position}", this);
        }
    }

    // --- Logique de Spawn d'Items ---

    public ItemInstance SpawnItem(ItemSO data, Vector3 pos)
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"<color=red>[SpawnManager]</color> SpawnItem can only be called by the Server!");
            return null;
        }

        if (pos == Vector3.zero && spawnGameObject != null)
            pos = spawnGameObject.transform.position;

        // 1. On instancie TOUJOURS le prefab par défaut (la "coquille" WorldItem)
        if (_defaultItemPrefab == null)
        {
            Debug.LogError("[SpawnManager] Le _defaultItemPrefab n'est pas assigné !");
            return null;
        }

        GameObject worldItemGo = Instantiate(_defaultItemPrefab, pos, Quaternion.identity);

        // 2. On renomme l'objet pour la hiérarchie
        worldItemGo.name = $"WorldItem_{data.ItemName}";

        // 3. Création de la donnée (Instance)
        ItemInstance instance = data.CreateInstance();

        // 4. Gestion des couleurs aléatoires pour les équipements
        if (instance is EquipmentInstance equipment)
        {
            equipment.SetPrimaryColor(Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.5f, 1f));
            if (equipment is WearableInstance wearable)
            {
                wearable.SetSecondaryColor(Random.ColorHSV(0f, 1f, 0.3f, 0.8f, 0.3f, 0.8f));
            }
        }

        // 5. Liaison avec le script WorldItem
        if (worldItemGo.TryGetComponent(out WorldItem worldItemComponent))
        {
            worldItemComponent.Initialize(instance);

            if (worldItemGo.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
            {
                worldItemComponent.SetNetworkData(new NetworkItemData
                {
                    ItemId = new Unity.Collections.FixedString64Bytes(data.ItemId),
                    JsonData = new Unity.Collections.FixedString4096Bytes(JsonUtility.ToJson(instance))
                });

                netObj.Spawn(true);
            }
            else
            {
                Debug.LogWarning($"<color=orange>[SpawnManager]</color> _defaultItemPrefab missing NetworkObject component!");
            }

            // --- NOUVELLE LOGIQUE D'ÉJECTION ---
            if (worldItemGo.TryGetComponent(out Rigidbody rb))
            {
                // On calcule une direction aléatoire sur le plan horizontal (X, Z)
                float randomX = Random.Range(-1f, 1f);
                float randomZ = Random.Range(-1f, 1f);

                // On définit la force : une poussée vers le haut (Y) et un peu sur les côtés
                // Ajuste le 5f (hauteur) et le 2f (dispersion) selon tes besoins
                Vector3 ejectForce = new Vector3(randomX * 2f, 5f, randomZ * 2f);

                // On applique l'impulsion
                rb.AddForce(ejectForce, ForceMode.Impulse);

                // Optionnel : Ajoute un petit torque (rotation) pour que l'objet tourne sur lui-même en tombant
                rb.AddTorque(new Vector3(Random.Range(-10, 10), Random.Range(-10, 10), Random.Range(-10, 10)));
            }
            // ------------------------------------

            Debug.Log($"<color=green>[Spawn]</color> {instance.ItemSO.ItemName} éjecté !");
        }

        return instance;
    }

    public void SpawnCopyOfItem(ItemInstance existingInstance, Vector3 pos)
    {
        if (Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            Debug.LogError($"<color=red>[SpawnManager]</color> SpawnCopyOfItem can only be called by the Server!");
            return;
        }

        if (existingInstance == null) return;

        GameObject go = Instantiate(_defaultItemPrefab, pos, Quaternion.identity);
        go.name = $"WorldItem_{existingInstance.CustomizedName}_Copy";

        // On applique les propriétés sauvegardées (couleurs, library)
        existingInstance.InitializeWorldPrefab(go);

        if (go.TryGetComponent(out WorldItem worldItem) || go.GetComponentInChildren<WorldItem>() != null)
        {
            if (worldItem == null) worldItem = go.GetComponentInChildren<WorldItem>();
            worldItem.Initialize(existingInstance);

            if (go.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
            {
                netObj.Spawn(true);
            }

            worldItem.SetNetworkData(new NetworkItemData
            {
                ItemId = new Unity.Collections.FixedString64Bytes(existingInstance.ItemSO.ItemId),
                JsonData = new Unity.Collections.FixedString4096Bytes(JsonUtility.ToJson(existingInstance))
            });
        }
    }

    // --- Logique de Spawn de Personnages ---

    public Character SpawnCharacter(
        Vector3 pos,
        RaceSO race,
        GameObject visualPrefab,
        CharacterPersonalitySO personality = null,
        CharacterBehavioralTraitsSO traits = null,
        List<(CombatStyleSO style, int level)> combatStyles = null,
        List<(SkillSO skill, int level)> skills = null)
    {
        Vector3 spawnPos = pos == Vector3.zero && spawnGameObject != null ? spawnGameObject.transform.position : pos;

        GameObject characterPrefabObj = Instantiate(visualPrefab, spawnPos, Quaternion.identity);
        if (characterPrefabObj == null) return null;

        if (!characterPrefabObj.TryGetComponent(out Character character))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        // (Removed DestroyImmediate logic for NetworkRigidbody. Character.cs now disables it gracefully to preserve NetworkBehaviour indexing)

        // Si le réseau tourne, on doit Spawn() l'objet réseau
        if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsServer)
        {
            if (characterPrefabObj.TryGetComponent(out Unity.Netcode.NetworkObject netObj))
            {
                if (!netObj.IsSpawned)
                {
                    if (race != null)
                    {
                        character.NetworkRaceId.Value = new Unity.Collections.FixedString64Bytes(race.name);
                    }

                    // Pre-generate deterministic data on server so all clients get the same values
                    GenderType gender = character.CharacterBio != null && character.CharacterBio.IsMale ? GenderType.Male : GenderType.Female;
                    if (race != null && race.NameGenerator != null)
                        character.NetworkCharacterName.Value = new Unity.Collections.FixedString64Bytes(race.NameGenerator.GenerateName(gender));

                    character.NetworkVisualSeed.Value = Random.Range(int.MinValue, int.MaxValue);

                    if ((traits != null) || (combatStyles != null && combatStyles.Count > 0) || (skills != null && skills.Count > 0))
                    {
                        _pendingDevConfig[netObj.NetworkObjectId] = new PendingDevConfig
                        {
                            Traits = traits,
                            CombatStyles = combatStyles,
                            Skills = skills
                        };
                    }

                    // L'objet réseau va appeler InitializeSpawnedCharacter via OnNetworkSpawn.
                    try
                    {
                        netObj.Spawn(true);
                    }
                    catch (System.Exception ex)
                    {
                        // Prevent pending-config leak if the network spawn throws.
                        _pendingDevConfig.Remove(netObj.NetworkObjectId);
                        Debug.LogException(ex);
                        throw;
                    }

                    return character;
                }
            }
        }

        // Public SpawnManager.SpawnCharacter always spawns NPCs. The networked player-spawn path
        // runs through Character.OnNetworkSpawn → InitializeSpawnedCharacter(isPlayerObject: true)
        // and does not go through this method.
        if (!InitializeSpawnedCharacter(character, race, isPlayerObject: false, personality: personality))
        {
            Destroy(characterPrefabObj);
            return null;
        }
        ApplyDevExtras(character, traits, combatStyles, skills);

        return character;
    }

    public bool InitializeSpawnedCharacter(Character character, RaceSO race, bool isPlayerObject, bool isLocalOwner = false, CharacterPersonalitySO personality = null)
    {
        // Default Race fallback so that missing references don't break logic
        if (race == null)
        {
            race = _defaultFallbackRace;
            if (race == null) Debug.LogWarning("[SpawnManager] Fallback race is missing in inspector!");
        }

        // --- DRAIN PENDING DEV CONFIG UP-FRONT ---
        // Popping the entry now (instead of at the tail) ensures the dictionary
        // never leaks if an init step below returns false and short-circuits.
        PendingDevConfig? pendingDev = null;
        if (character.IsSpawned && character.NetworkObject != null
            && _pendingDevConfig.TryGetValue(character.NetworkObject.NetworkObjectId, out var popped))
        {
            _pendingDevConfig.Remove(character.NetworkObject.NetworkObjectId);
            pendingDev = popped;
        }

        // --- DETERMINISTIC SEED ---
        // Use the networked seed so all clients produce identical random values.
        // Offline (non-networked) spawns generate a fresh seed locally.
        int seed = (character.IsSpawned && character.NetworkVisualSeed.Value != 0)
            ? character.NetworkVisualSeed.Value
            : Random.Range(int.MinValue, int.MaxValue);
        System.Random rng = new System.Random(seed);

        // --- NETWORKED NAME ---
        // Apply the server-generated name before InitializeRace (which also generates names if empty)
        if (character.IsSpawned && !character.NetworkCharacterName.Value.IsEmpty)
        {
            character.CharacterName = character.NetworkCharacterName.Value.ToString();
        }

        if (!SetupInteractionDetector(character.gameObject, isPlayerObject)) return false;

        if (!InitializeCharacter(character.gameObject, race, null)) return false;

        // --- NAMING (only if not already set from network) ---
        if (string.IsNullOrEmpty(character.CharacterName) && race != null && race.NameGenerator != null)
        {
            GenderType charGender = character.CharacterBio != null && character.CharacterBio.IsMale ? GenderType.Male : GenderType.Female;
            character.CharacterName = race.NameGenerator.GenerateName(charGender);
        }

        // Update the GameObject's name in the Unity Hierarchy
        if (string.IsNullOrEmpty(character.CharacterName))
        {
            character.gameObject.name = race != null ? $"NPC_{race.RaceName}" : "Unknown_NPC";
        }
        else
        {
            character.gameObject.name = character.CharacterName;
        }

        ApplyRandomColor(character, rng);

        // Local ownership (UI & Camera) is now strictly handled by Character.SwitchToPlayer()

        float randomSize = Random.Range(0f, 200f);
        character.CharacterVisual.ResizeCharacter(randomSize);
        character.CharacterVisual.RequestAutoResize();
        character.CharacterVisual.ApplyPresetFromRace(race);

        // --- GESTION DE LA PERSONNALITÉ ---
        if (personality == null && _availablePersonalities != null && _availablePersonalities.Length > 0)
        {
            personality = _availablePersonalities[rng.Next(0, _availablePersonalities.Length)];
        }

        if (character.CharacterProfile != null && personality != null)
        {
            character.CharacterProfile.SetPersonality(personality);
            Debug.Log($"<color=cyan>[Spawn]</color> {character.CharacterName} a spawn avec la personnalité : {personality.PersonalityName}");
        }

        // --- GESTION DES TRAITS COMPORTEMENTAUX ---
        // Skip random trait assignment if a dev-mode trait override is queued for this character.
        // Note: in the offline path (non-networked), the caller's ApplyDevExtras runs AFTER this
        // method and will overwrite any random trait assignment done above. Two logs will appear
        // for offline dev-spawns. This is acceptable — offline dev-spawn is a dev-only flow.
        bool devTraitPending = pendingDev.HasValue && pendingDev.Value.Traits != null;
        if (!devTraitPending && _availableTraits != null && _availableTraits.Length > 0 && character.CharacterTraits != null)
        {
            CharacterBehavioralTraitsSO randomTrait = _availableTraits[rng.Next(0, _availableTraits.Length)];
            character.CharacterTraits.behavioralTraitsProfile = randomTrait;
            Debug.Log($"<color=cyan>[Spawn]</color> {character.CharacterName} a reçu le profil comportemental : {randomTrait.name}");
        }

        // --- APPLY DEV-MODE OVERRIDES ---
        // Takes priority over any random assignments done above.
        // The entry was popped from _pendingDevConfig at the top of this method so the
        // dictionary cannot leak if any earlier init step returned false and short-circuited.
        if (pendingDev.HasValue)
        {
            ApplyDevExtras(character, pendingDev.Value.Traits, pendingDev.Value.CombatStyles, pendingDev.Value.Skills);
        }

        return true;
    }

    private bool SetupInteractionDetector(GameObject obj, bool isPlayer)
    {
        Character character = obj.GetComponent<Character>();
        if (character == null) return false;

        if (isPlayer) character.SwitchToPlayer();
        else character.SwitchToNPC();

        return true;
    }

    private bool InitializeCharacter(GameObject obj, RaceSO race, GameObject visualPrefab)
    {
        Character character = obj.GetComponent<Character>();
        if (character == null)
        {
            Debug.LogError("SpawnManager: Composant Character introuvable sur l'objet !");
            return false;
        }

        Transform visual = obj.transform.Find("Visual");
        character.AssignVisualRoot(visual);

        // character.InitializeStats() n'est plus appelé ici, car les stats de base sont injectées via InitializeRace
        character.InitializeRace(race);
        character.InitializeAll();

        // On décompose pour trouver où ça bloque
        if (character.CharacterVisual != null &&
            character.CharacterVisual.BodyPartsController != null &&
            character.CharacterVisual.BodyPartsController.EyesController != null)
        {
            character.CharacterVisual.BodyPartsController.InitializeAllBodyParts();
        }
        else
        {
            // Ce log va te dire EXACTEMENT ce qui manque dans l'inspecteur
            Debug.LogError($"SpawnManager: Problème de hiérarchie sur {obj.name}. " +
                $"Visual: {character.CharacterVisual != null}, " +
                $"BodyParts: {character.CharacterVisual?.BodyPartsController != null}, " +
                $"Eyes: {character.CharacterVisual?.BodyPartsController?.EyesController != null}");
        }

        return true;
    }

    private bool ApplyRandomColor(Character character, System.Random rng)
    {
        CharacterVisual cv = character.GetComponentInChildren<CharacterVisual>();
        if (cv == null) return false;

        try
        {
            cv.BodyPartsController.HairController.SetGlobalHairColor(RandomColorHSV(rng));
            cv.ApplyColor(CharacterVisual.VisualPart.Hair, RandomColorHSV(rng));
            cv.BodyPartsController.EyesController.SetAllPupilsColor(RandomColorHSV(rng));
            cv.SkinColor = ColorUtils.HexToColor("E6CEBD");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Erreur lors de l'application de la couleur : {ex.Message}");
            return false;
        }

        return true;
    }

    private static Color RandomColorHSV(System.Random rng, float minS = 0f, float maxS = 1f, float minV = 0f, float maxV = 1f)
    {
        float h = (float)rng.NextDouble();
        float s = minS + (float)(rng.NextDouble() * (maxS - minS));
        float v = minV + (float)(rng.NextDouble() * (maxV - minV));
        return Color.HSVToRGB(h, s, v);
    }

    private void ApplyDevExtras(
        Character character,
        CharacterBehavioralTraitsSO traits,
        List<(CombatStyleSO style, int level)> combatStyles,
        List<(SkillSO skill, int level)> skills)
    {
        if (character == null) return;

        if (traits != null && character.CharacterTraits != null)
        {
            character.CharacterTraits.behavioralTraitsProfile = traits;
            Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} trait overridden to {traits.name}");
        }

        if (combatStyles != null && character.CharacterCombat != null)
        {
            foreach (var entry in combatStyles)
            {
                if (entry.style == null) continue;
                character.CharacterCombat.UnlockCombatStyle(entry.style, entry.level);
                Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} combat style {entry.style.StyleName} L{entry.level}");
            }
        }

        if (skills != null && character.CharacterSkills != null)
        {
            foreach (var entry in skills)
            {
                if (entry.skill == null) continue;
                character.CharacterSkills.AddSkill(entry.skill, entry.level);
                Debug.Log($"<color=cyan>[Spawn]</color> Dev-mode: {character.CharacterName} skill {entry.skill.SkillName} L{entry.level}");
            }
        }
    }
}
