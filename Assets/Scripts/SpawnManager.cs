using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
    [SerializeField] private GameObject itemPrefab;

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
        GameObject go = Instantiate(itemPrefab, pos, Quaternion.identity);
        go.name = $"WorldItem_{data.ItemName}";

        // CreateInstance() génère le bon type (BagInstance, WeaponInstance, etc.)
        ItemInstance instance = data.CreateInstance();

        if (instance is EquipmentInstance equipment)
        {
            // On génère les couleurs aléatoires
            Color randomPrimary = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.5f, 1f);
            Color randomSecondary = Random.ColorHSV(0f, 1f, 0.3f, 0.8f, 0.3f, 0.8f);

            equipment.SetPrimaryColor(randomPrimary);

            if (equipment is WearableInstance wearable)
            {
                wearable.SetSecondaryColor(randomSecondary);
            }

            // --- APPLICATION VISUELLE SUR L'OBJET AU SOL ---
            // On récupère tous les renderers de l'objet qui vient d'apparaître
            SpriteRenderer[] sRenderers = go.GetComponentsInChildren<SpriteRenderer>();
            foreach (var sRenderer in sRenderers)
            {
                if (sRenderer.gameObject.name == "Color_Primary")
                    sRenderer.color = randomPrimary;
                else if (sRenderer.gameObject.name == "Color_Secondary")
                    sRenderer.color = randomSecondary;
                // On ne touche pas à Color_Main ni Line ici non plus !
            }

            // Si tu utilises quand même des MeshRenderers pour certains items 3D
            MeshRenderer visualRenderer = go.GetComponentInChildren<MeshRenderer>();
            if (visualRenderer != null)
            {
                visualRenderer.material.color = randomPrimary;
            }
        }

        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();
        if (worldItem != null)
        {
            worldItem.Initialize(instance);
            Debug.Log($"<color=green>[Spawn]</color> {instance.ItemSO.ItemName} créé avec succès.");
        }

        return instance;
    }

    public void SpawnCopyOfItem(ItemInstance existingInstance, Vector3 pos)
    {
        if (existingInstance == null) return;

        GameObject go = Instantiate(itemPrefab, pos, Quaternion.identity);
        go.name = $"WorldItem_{existingInstance.ItemSO.ItemName}_Copy";

        WorldItem worldItem = go.GetComponentInChildren<WorldItem>();
        if (worldItem != null)
        {
            worldItem.Initialize(existingInstance);

            // Pattern matching ici aussi pour éviter l'erreur d'assignation
            if (existingInstance is EquipmentInstance equipment)
            {
                if (equipment.HavePrimaryColor())
                {
                    SpriteRenderer visualRenderer = go.GetComponentInChildren<SpriteRenderer>();
                    if (visualRenderer != null)
                    {
                        visualRenderer.color = equipment.PrimaryColor;
                    }
                }
            }
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