using UnityEngine;

public class CharacterBodyPartsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Character character;
    [SerializeField] private EyesController eyesController;
    [SerializeField] private HairController _hairController;
    public EyesController EyesController => eyesController;
    public HairController HairController => _hairController;


    public void InitializeSpriteLibrariesToEveryBodyController()
    {
        EyesController.InitializeSpriteLibraries();

    }
    void Start()
    {
        ValidateReferences();
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
        else
        {
            Debug.Log($"CharacterBodyPartsController: EyesController on '{eyesController.gameObject.name}' successfully assigned.");
        }
    }
}