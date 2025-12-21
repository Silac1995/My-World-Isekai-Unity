using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    [SerializeField] private GameObject spawnGameObject;
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

    public Character SpawnCharacter(Vector3 pos, float health, float mana, float str, float agi, RaceSO race, GameObject visualPrefab, bool isPlayer)
    {
        if (Instance == null)
        {
            Debug.LogError("SpawnManager.Instance est null. Assure-toi que le SpawnManager est initialisé avant d'appeler SpawnCharacter.", this);
            return null;
        }

        if (race == null)
        {
            Debug.LogError("RaceData est null. Fournis une RaceData valide.", this);
            return null;
        }
        if (visualPrefab == null)
        {
            Debug.LogError("visualPrefab est null. Fournis un prefab visuel valide.", this);
            return null;
        }

        // Utiliser spawnGameObject.transform.position si pos est (0, 0, 0) et spawnGameObject est assigné
        Vector3 spawnPos = pos == Vector3.zero && spawnGameObject != null ? spawnGameObject.transform.position : pos;
        if (spawnPos == Vector3.zero)
        {
            Debug.LogWarning("Position de spawn est (0, 0, 0). Vérifie la valeur de pos ou spawnGameObject.", this);
        }
        Debug.Log($"Instantiation du personnage à la position : {spawnPos}", this);

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

        // Configurer CharacterInteractable sur l'enfant
        CharacterInteractable interactable = characterPrefabObj.GetComponentInChildren<CharacterInteractable>();
        if (interactable != null)
        {
            if (interactable.Character == null)
            {
                interactable.Character = character;
                Debug.Log($"Assignation du composant Character à CharacterInteractable sur {characterPrefabObj.name}.", this);
            }
        }
        else
        {
            Debug.LogWarning($"Aucun CharacterInteractable trouvé sur les enfants de {characterPrefabObj.name}.", this);
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

        if (isPlayer)
        {
            PlayerController controller = obj.AddComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("Échec de l'ajout du composant PlayerController.", this);
                return false;
            }

            PlayerInteractionDetector detector = obj.GetComponent<PlayerInteractionDetector>() ?? obj.AddComponent<PlayerInteractionDetector>();
            if (detector == null)
            {
                Debug.LogError("Échec de l'ajout du composant PlayerInteractionDetector.", this);
                return false;
            }

        }
        else
        {
            NPCController controller = obj.GetComponent<NPCController>();
            if (controller == null)
            {
                Debug.LogError("Échec de l'ajout du composant NPCController.", this);
                return false;
            }

            NPCInteractionDetector detector = obj.GetComponent<NPCInteractionDetector>() ?? obj.AddComponent<NPCInteractionDetector>();
            if (detector == null)
            {
                Debug.LogError("Échec de l'ajout du composant NPCInteractionDetector.", this);
                return false;
            }
        }

        return true;
    }

    private bool InitializeCharacter(GameObject obj, RaceSO race, GameObject visualPrefab, float health, float mana, float str, float agi)
    {
        if (obj == null)
        {
            Debug.LogError("L'objet passé à InitializeCharacter est null.", this);
            return false;
        }

        Character character = obj.GetComponent<Character>();
        if (character == null)
        {
            Debug.LogError("Le composant Character est manquant sur le characterPrefab.", this);
            return false;
        }
        // Trouver l'enfant nommé "Visual" et l'assigner à visualRoot
        Transform visual = obj.transform.Find("Visual");
        if (visual == null)
        {
            Debug.LogError("Aucun Transform nommé 'Visual' trouvé comme enfant du prefab. Assignation impossible.", this);
            return false;
        }

        character.AssignVisualRoot(visual);
        Debug.Log(character.VisualRoot, this);
        try
        {
            character.InitializeStats(health, mana, str, agi);
            character.InitializeRace(race);
            Debug.Log($"Position après InitializeStats : {obj.transform.position}", this);
            character.InitializeAll();
            Debug.Log($"Position après InitializeAll : {obj.transform.position}", this);
            character.InitializeAnimator();
            Debug.Log($"Position après InitializeAnimator : {obj.transform.position}", this);
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