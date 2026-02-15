using UnityEngine;

public class CharacterBodyPartsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character character;
    [SerializeField] private EyesController eyesController;
    [SerializeField] private HairController _hairController;
    [SerializeField] private EarsController _earsController;
    [SerializeField] private MouthController _mouthController;

    public EyesController EyesController => eyesController;
    public HairController HairController => _hairController;
    public EarsController EarsController => _earsController;
    public MouthController MouthController => _mouthController;

    void Start()
    {
        ValidateReferences();
    }
    public void InitializeAllBodyParts()
    {

        // On lance le scan des GameObjects pour chaque membre
        if (EyesController != null) EyesController.Initialize();
        if (HairController != null) HairController.Initialize();
        if (EarsController != null) EarsController.Initialize();
        if (MouthController != null) MouthController.Initialize();

        Debug.Log("<color=white>[BodyParts]</color> All body parts initialized.");
    }

    private void ValidateReferences()
    {
        if (character == null)
        {
            Debug.LogError($"CharacterBodyPartsController on {gameObject.name}: Character reference is missing! Please assign it in the Inspector.");
        }
        else
        {
            Debug.Log($"CharacterBodyPartsController: Character '{character.name}' successfully assigned.");
        }

        if (eyesController == null)
        {
            Debug.LogError($"CharacterBodyPartsController on {gameObject.name}: EyesController reference is missing! Please assign it in the Inspector.");
        }
        if (_earsController == null)
        {
            Debug.LogError($"CharacterBodyPartsController on {gameObject.name}: EarsController is missing!");
        }
        if(_hairController == null)
        {
            Debug.LogError($"CharacterBodyPartsController on {gameObject.name}: HairController is missing!");
        }
        if(_mouthController == null)
        {
                Debug.LogError($"CharacterBodyPartsController on {gameObject.name}: MouthController is missing!");
        }
        else
        {
            Debug.Log($"CharacterBodyPartsController: EyesController on '{eyesController.gameObject.name}' successfully assigned.");
        }
    }

}