using UnityEngine;
using System.Linq;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
    [SerializeField] private GameObject _defaultItemPrefab;

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
        if (existingInstance == null) return;

        // On récupère le prefab depuis le SO de l'instance existante
        GameObject prefabToSpawn = existingInstance.ItemSO.ItemPrefab != null
                                    ? existingInstance.ItemSO.ItemPrefab
                                    : _defaultItemPrefab;

        GameObject go = Instantiate(prefabToSpawn, pos, Quaternion.identity);
        go.name = $"WorldItem_{existingInstance.CustomizedName}_Copy";

        // On applique les propriétés sauvegardées (couleurs, library)
        existingInstance.InitializeWorldPrefab(go);

        if (go.TryGetComponent(out WorldItem worldItem) || go.GetComponentInChildren<WorldItem>() != null)
        {
            if (worldItem == null) worldItem = go.GetComponentInChildren<WorldItem>();
            worldItem.Initialize(existingInstance);
        }
    }

    // --- Logique de Spawn de Personnages ---

    public Character SpawnCharacter(Vector3 pos, RaceSO race, GameObject visualPrefab, bool isPlayer, CharacterPersonalitySO personality = null)
    {
        Vector3 spawnPos = pos == Vector3.zero && spawnGameObject != null ? spawnGameObject.transform.position : pos;

        GameObject characterPrefabObj = Instantiate(visualPrefab, spawnPos, Quaternion.identity);
        if (characterPrefabObj == null) return null;

        if (!characterPrefabObj.TryGetComponent(out Character character))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        if (!SetupInteractionDetector(characterPrefabObj, isPlayer))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        if (!InitializeCharacter(characterPrefabObj, race, visualPrefab))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        // --- RANDOM NAMING ---
        if (race != null && race.NameGenerator != null)
        {
            GenderType charGender = character.CharacterBio != null && character.CharacterBio.IsMale ? GenderType.Male : GenderType.Female;
            character.CharacterName = race.NameGenerator.GenerateName(charGender);
        }
        
        // Update the GameObject's name in the Unity Hierarchy
        if (string.IsNullOrEmpty(character.CharacterName))
        {
            characterPrefabObj.name = race != null ? $"NPC_{race.RaceName}" : "Unknown_NPC";
        }
        else
        {
            characterPrefabObj.name = character.CharacterName;
        }

        ApplyRandomColor(character);

        if (isPlayer)
        {
            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null) cameraFollow.SetGameObject(characterPrefabObj);

            // Lien avec l'UI du joueur
            PlayerUI playerUI = UnityEngine.Object.FindAnyObjectByType<PlayerUI>(FindObjectsInactive.Include);
            if (playerUI != null)
            {
                playerUI.Initialize(characterPrefabObj);
            }
        }

        float randomSize = Random.Range(0f, 200f);
        character.CharacterVisual.ResizeCharacter(randomSize);
        character.CharacterVisual.RequestAutoResize();
        character.CharacterVisual.ApplyPresetFromRace(race);

        // --- GESTION DE LA PERSONNALITÉ ---
        if (personality == null && _availablePersonalities != null && _availablePersonalities.Length > 0)
        {
            personality = _availablePersonalities[Random.Range(0, _availablePersonalities.Length)];
        }

        if (character.CharacterProfile != null && personality != null)
        {
            character.CharacterProfile.SetPersonality(personality);
            Debug.Log($"<color=cyan>[Spawn]</color> {character.CharacterName} a spawn avec la personnalité : {personality.PersonalityName}");
        }

        // --- GESTION DES TRAITS COMPORTEMENTAUX ---
        if (_availableTraits != null && _availableTraits.Length > 0 && character.CharacterTraits != null)
        {
            CharacterBehavioralTraitsSO randomTrait = _availableTraits[Random.Range(0, _availableTraits.Length)];
            character.CharacterTraits.behavioralTraitsProfile = randomTrait;
            Debug.Log($"<color=cyan>[Spawn]</color> {character.CharacterName} a reçu le profil comportemental : {randomTrait.name}");
        }

        // --- DEBUG : ASSIGNATION AUTOMATIQUE DE RÉSIDENCE ---
        if (BuildingManager.Instance != null && BuildingManager.Instance.allBuildings != null)
        {
            int totalBuildings = BuildingManager.Instance.allBuildings.Count;
            if (totalBuildings == 0)
            {
                Debug.LogWarning($"<color=orange>[SpawnManager]</color> Aucun bâtiment n'est enregistré dans le BuildingManager ! {character.CharacterName} reste sans maison.");
            }
            else
            {
                bool buildingFound = false;
                foreach (var b in BuildingManager.Instance.allBuildings)
                {
                    if (b is ResidentialBuilding resBuilding)
                    {
                        if (resBuilding.Owners.Count == 0)
                        {
                            if (character.CharacterLocations != null)
                            {
                                character.CharacterLocations.ReceiveOwnership(resBuilding);
                                Debug.Log($"<color=green>[SpawnManager]</color> {character.CharacterName} est maintenant propriétaire de {resBuilding.BuildingName}.");
                                buildingFound = true;
                                break;
                            }
                            else
                            {
                                Debug.LogWarning($"<color=orange>[SpawnManager]</color> {character.CharacterName} n'a pas de composant CharacterLocations !");
                            }
                        }
                        else
                        {
                            Debug.Log($"[SpawnManager] Bâtiment {resBuilding.BuildingName} ignoré car il a déjà {resBuilding.Owners.Count} propriétaire(s).");
                        }
                    }
                    else
                    {
                        Debug.Log($"[SpawnManager] Bâtiment {b.BuildingName} ignoré car c'est un {b.BuildingType} (non résidentiel).");
                    }
                }

                if (!buildingFound)
                {
                    Debug.LogWarning($"<color=orange>[SpawnManager]</color> Aucun bâtiment résidentiel libre trouvé parmi les {totalBuildings} bâtiments enregistrés.");
                }
            }
        }
        else
        {
            Debug.LogError("[SpawnManager] BuildingManager.Instance est null ou la liste des bâtiments est manquante.");
        }

        // --- LOGIQUE D'EMPLOI AUTOMATIQUE ---
        if (!isPlayer && character.CharacterJob != null && BuildingManager.Instance != null && BuildingManager.Instance.allBuildings.Count > 0)
        {
            // 1. Tente d'abord de devenir propriétaire d'un bâtiment commercial libre
            CommercialBuilding unownedCommercial = BuildingManager.Instance.FindUnownedCommercialBuilding();
            if (unownedCommercial != null)
            {
                // Le SetOwner s'occupera de lui donner le JobLogisticsManager prioritairement
                character.CharacterJob.BecomeOwner(unownedCommercial);
                Debug.Log($"<color=green>[SpawnManager]</color> {character.CharacterName} a pris possession du bâtiment commercial libre : {unownedCommercial.BuildingName}.");
            }
        }

        return character;
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

    private bool ApplyRandomColor(Character character)
    {
        CharacterVisual cv = character.GetComponentInChildren<CharacterVisual>();
        if (cv == null) return false;

        try
        {
            cv.BodyPartsController.HairController.SetGlobalHairColor(Random.ColorHSV());
            cv.ApplyColor(CharacterVisual.VisualPart.Hair, Random.ColorHSV());
            cv.BodyPartsController.EyesController.SetAllPupilsColor(Random.ColorHSV());
            cv.SkinColor = ColorUtils.HexToColor("E6CEBD");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Erreur lors de l'application de la couleur : {ex.Message}");
            return false;
        }

        return true;
    }
}