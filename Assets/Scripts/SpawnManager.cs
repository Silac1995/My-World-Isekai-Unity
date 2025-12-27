using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
    [SerializeField] private GameObject itemPrefab;
    private const string INTERACTION_PROMPT_PATH = "UI/InteractionPrompt";



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


    // Dans SpawnManager.cs
    public ItemInstance SpawnItem(ItemSO data, Vector3 pos)
    {
        GameObject go = Instantiate(itemPrefab, pos, Quaternion.identity);
        go.name = $"WorldItem_{data.ItemName}";

        // 1. Création de l'instance de donnée
        ItemInstance instance = data switch
        {
            EquipmentSO e => new EquipmentInstance(e),
            _ => new ItemInstance(data)
        };

        // 2. Logique de couleur personnalisée (si c'est un équipement)
        if (instance is EquipmentInstance equipment)
        {
            // On génère une couleur aléatoire
            Color randomColor = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.5f, 1f);
            equipment.SetCustomizedColor(randomColor);

            // On applique cette couleur au visuel de l'objet au sol
            // On cherche le renderer dans les enfants (là où se trouve le mesh)
            MeshRenderer visualRenderer = go.GetComponentInChildren<MeshRenderer>();
            if (visualRenderer != null)
            {
                // Utilisation de .material (crée une instance unique pour cet objet)
                visualRenderer.material.color = randomColor;
            }
        }

        // 3. Liaison avec le composant WorldItem
        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();
        if (worldItem != null)
        {
            worldItem.Initialize(instance);
            Debug.Log($"<color=green>[Spawn]</color> Setup réussi pour {instance.ItemSO.ItemName} avec couleur appliquée.");
        }
        else
        {
            Debug.LogError($"<color=red>[Spawn Error]</color> WorldItem introuvable sur le prefab {go.name} !");
        }

        return instance;
    }

    /// <summary>
    /// Spawne un item dans le monde à partir d'une instance existante (conserve les couleurs, durabilité, etc.)
    /// </summary>
    public void SpawnCopyOfItem(ItemInstance existingInstance, Vector3 pos)
    {
        if (existingInstance == null)
        {
            Debug.LogError("[SpawnManager] Impossible de copier une instance nulle.");
            return;
        }

        // 1. Instanciation du prefab de base
        GameObject go = Instantiate(itemPrefab, pos, Quaternion.identity);
        go.name = $"WorldItem_{existingInstance.ItemSO.ItemName}_Copy";

        // 2. Liaison avec le composant WorldItem
        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();
        if (worldItem != null)
        {
            // On initialise avec l'instance EXACTE (pas une nouvelle)
            worldItem.Initialize(existingInstance);

            // 3. Application visuelle des propriétés de l'instance
            // Si c'est un équipement et qu'il a une couleur personnalisée, on l'applique au mesh/sprite
            if (existingInstance is EquipmentInstance equipment && equipment.HaveCustomizedColor())
            {
                // Note: J'utilise MeshRenderer car c'est ce que tu as utilisé dans SpawnItem
                MeshRenderer visualRenderer = go.GetComponentInChildren<MeshRenderer>();
                if (visualRenderer != null)
                {
                    visualRenderer.material.color = equipment.CustomizedColor;
                }
            }

            Debug.Log($"<color=cyan>[Spawn Copy]</color> Copie de {existingInstance.ItemSO.ItemName} créée avec succès.");
        }
        else
        {
            Debug.LogError($"[Spawn Error] WorldItem introuvable sur le prefab pour la copie de {go.name}");
        }
    }


    public Character SpawnCharacter(Vector3 pos, float health, float mana, float str, float agi, RaceSO race, GameObject visualPrefab, bool isPlayer)
    {

        // Utiliser spawnGameObject.transform.position si pos est (0, 0, 0) et spawnGameObject est assigné
        Vector3 spawnPos = pos == Vector3.zero && spawnGameObject != null ? spawnGameObject.transform.position : pos;

        GameObject characterPrefabObj = Instantiate(visualPrefab, spawnPos, Quaternion.identity);
        if (characterPrefabObj == null)
        {
            Debug.LogError("Échec de l'instanciation du characterPrefab.", this);
            return null;
        }

        // Vérifier que le composant Character est présent
        if (!characterPrefabObj.TryGetComponent(out Character character))
        {
            Debug.LogError("Le characterPrefab n'a pas de composant Character.", this);
            Destroy(characterPrefabObj);
            return null;
        }

        // Vérifier la position après instantiation
        Debug.Log($"Position après instantiation : {characterPrefabObj.transform.position}", this);

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

        if (!ApplyRandomColor(character))
        {
            Destroy(characterPrefabObj);
            return null;
        }

        // Vérifier la position finale
        Debug.Log($"Personnage instancié à la position finale : {characterPrefabObj.transform.position}", this);
        // Si le personnage est un joueur, la caméra le suit
        if (isPlayer)
        {
            CameraFollow cameraFollow = Camera.main?.GetComponent<CameraFollow>();
            if (cameraFollow != null)
            {
                cameraFollow.SetGameObject(characterPrefabObj);
                Debug.Log("Caméra attachée au joueur.", this);
            }
            else
            {
                Debug.LogWarning("CameraFollow non trouvé sur la caméra principale.", this);
            }
        }

        character.CharacterVisual.RandomizeBreastSprites(4);
        character.CharacterVisual.RandomizeHairSprites(2);
        character.CharacterVisual.RandomizeCharacterSize();

        return character;
    }

    private bool SetupInteractionDetector(GameObject obj, bool isPlayer)
    {
        if (obj == null)
        {
            Debug.LogError("L'objet passé à SetupInteractionDetector est null.", this);
            return false;
        }

        Character character = obj.GetComponent<Character>();
        if (character == null)
        {
            Debug.LogError("Le composant Character est manquant.", this);
            return false;
        }

        if (isPlayer)
        {
            character.SwitchToPlayer();
        }
        else
        {
            character.SwitchToNPC();
        }

        return true;
    }


    private bool InitializeCharacter(GameObject obj, RaceSO race, GameObject visualPrefab, float health, float mana, float str, float agi)
    {
        Character character = obj.GetComponent<Character>();
        Transform visual = obj.transform.Find("Visual");

        character.AssignVisualRoot(visual);
        Debug.Log(character.VisualRoot, this);
        try
        {
            character.InitializeStats(health, mana, str, agi);
            character.InitializeRace(race);
            Debug.Log($"Position après InitializeStats : {obj.transform.position}", this);
            character.InitializeAll();
            Debug.Log($"Position après InitializeAll : {obj.transform.position}", this);
            character.InitializeSpriteRenderers();
            Debug.Log($"Position après InitializeSpriteRenderers : {obj.transform.position}", this);
            character.CharacterVisual.BodyPartsController.EyesController.Initialize();

        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Erreur lors de l'initialisation du personnage : {ex.Message}", this);
            //return false;
        }

        return true;
    }

    private bool ApplyRandomColor(Character character)
    {
        if (character == null)
        {
            Debug.LogError("Le personnage passé à ApplyRandomColor est null.", this);
            return false;
        }

        CharacterVisual cv = character.GetComponentInChildren<CharacterVisual>();
        if (cv == null)
        {
            Debug.LogError("CharacterVisual est manquant sur le personnage ou ses enfants.", this);
            return false;
        }

        try
        {
            cv.HairColor = Random.ColorHSV();
            //cv.LeftEyeColor = Random.ColorHSV();
            //cv.RightEyeColor = Random.ColorHSV();
            cv.BodyPartsController.EyesController.SetAllPupilsColor(Random.ColorHSV());
            cv.SkinColor = ColorUtils.HexToColor("E6CEBD");

            Debug.Log($"Position après ApplyRandomColor : {character.transform.position}", this);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Erreur lors de l'application de la couleur : {ex.Message}", this);
            return false;
        }

        return true;
    }
}