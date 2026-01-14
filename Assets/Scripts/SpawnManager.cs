using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
    [SerializeField] private GameObject _defaultItemPrefab;

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

    public Character SpawnCharacter(Vector3 pos, float health, float mana, float str, float agi, RaceSO race, GameObject visualPrefab, bool isPlayer)
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

        if (!InitializeCharacter(characterPrefabObj, race, visualPrefab, health, mana, str, agi))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        ApplyRandomColor(character);

        if (isPlayer)
        {
            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null) cameraFollow.SetGameObject(characterPrefabObj);
        }

        float randomSize = Random.Range(0f, 200f);
        character.CharacterVisual.ResizeCharacter(randomSize);
        character.CharacterVisual.RequestAutoResize();
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

    private bool InitializeCharacter(GameObject obj, RaceSO race, GameObject visualPrefab, float health, float mana, float str, float agi)
    {
        Character character = obj.GetComponent<Character>();
        if (character == null)
        {
            Debug.LogError("SpawnManager: Composant Character introuvable sur l'objet !");
            return false;
        }

        Transform visual = obj.transform.Find("Visual");
        character.AssignVisualRoot(visual);

        character.InitializeStats(health, mana, str, agi);
        character.InitializeRace(race);
        character.InitializeAll();

        // On décompose pour trouver où ça bloque
        if (character.CharacterVisual != null &&
            character.CharacterVisual.BodyPartsController != null &&
            character.CharacterVisual.BodyPartsController.EyesController != null)
        {
            character.CharacterVisual.BodyPartsController.EyesController.Initialize();
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