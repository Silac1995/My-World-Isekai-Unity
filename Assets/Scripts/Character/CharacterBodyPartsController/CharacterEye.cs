using UnityEngine;
using UnityEngine.U2D.Animation;

[System.Serializable]
public class CharacterEye
{
    [Header("Eye GameObjects")]
    [SerializeField] private GameObject eyeBrow;
    [SerializeField] private GameObject eyeBase;
    [SerializeField] private GameObject eyeSclera;
    [SerializeField] private GameObject eyePupil;

    [Header("Eyebrow")]
    [SerializeField] private string eyebrowsCategory;
    [SerializeField] private string eyebrowLabel;
    [SerializeField] private string eyebrowState = "_normal";

    [Header("Eye")]
    [SerializeField] private string eyesCategory;
    [SerializeField] private string eyeLabel;

    [Header("Renderers and Resolvers")]
    [SerializeField] private SpriteRenderer eyeBaseRenderer;
    [SerializeField] private SpriteResolver eyeBaseResolver;
    [SerializeField] private SpriteRenderer eyeScleraRenderer;
    [SerializeField] private SpriteResolver eyeScleraResolver;
    [SerializeField] private SpriteResolver eyeBrowResolver;
    [SerializeField] private SpriteRenderer eyePupilRenderer;
    [SerializeField] private SpriteResolver eyePupilResolver;
    [SerializeField] private SpriteLibrary eyebrowsBaseSpriteLibrary;
    [SerializeField] private SpriteLibrary eyeBaseSpriteLibrary;

    [Header("Individual Settings")]
    [SerializeField] private bool isCurrentlyClosed = false;
    [SerializeField] private bool canEyeClose = true;

    // --- Getters et Setters simplifiés ---
    public GameObject EyeBrow { get => eyeBrow; set => eyeBrow = value; }
    public GameObject EyeBase { get => eyeBase; set => eyeBase = value; }
    public GameObject EyeSclera { get => eyeSclera; set => eyeSclera = value; }
    public GameObject EyePupil { get => eyePupil; set => eyePupil = value; }
    public string EyeLabel { get => eyeLabel; set => eyeLabel = value; }
    public bool IsCurrentlyClosed { get => isCurrentlyClosed; set => isCurrentlyClosed = value; }
    public bool CanEyeClose { get => canEyeClose; set => canEyeClose = value; }

    public CharacterEye(GameObject eyeBrow, GameObject eyeBase, GameObject eyeSclera, GameObject eyePupil, string eyeLabel, string eyebrowLabel, string eyesCategory = "01", string eyebrowsCategory = "01")
    {
        this.eyeBrow = eyeBrow;
        this.eyeBase = eyeBase;
        this.eyeSclera = eyeSclera;
        this.eyePupil = eyePupil;
        this.eyeLabel = string.IsNullOrEmpty(eyeLabel) ? "Eye_R" : eyeLabel;
        this.eyebrowLabel = string.IsNullOrEmpty(eyebrowLabel) ? "Eyebrow_R" : eyebrowLabel;
        this.eyesCategory = eyesCategory;
        this.eyebrowsCategory = eyebrowsCategory;

        eyeBaseRenderer = eyeBase?.GetComponent<SpriteRenderer>();
        eyeBaseResolver = eyeBase?.GetComponent<SpriteResolver>();
        eyeScleraRenderer = eyeSclera?.GetComponent<SpriteRenderer>();
        eyeScleraResolver = eyeSclera?.GetComponent<SpriteResolver>();
        eyeBrowResolver = eyeBrow?.GetComponent<SpriteResolver>();
        eyePupilRenderer = eyePupil?.GetComponent<SpriteRenderer>();
        eyePupilResolver = eyePupil?.GetComponent<SpriteResolver>();
        eyeBaseSpriteLibrary = eyeBase?.GetComponent<SpriteLibrary>();
        eyebrowsBaseSpriteLibrary = eyeBrow?.GetComponent<SpriteLibrary>();

        // Fixe toutes les catégories à eyesCategory, sauf eyebrows
        eyeBaseResolver.SetCategoryAndLabel(eyesCategory, EyeLabel);
        eyeScleraResolver.SetCategoryAndLabel(eyesCategory, EyeLabel+"_sclera");
        eyePupilResolver.SetCategoryAndLabel(eyesCategory, EyeLabel+"_pupil");

        CheckIfCanClose();
    }

    /// <summary>
    /// Vérifie si le sprite "_Closed" existe
    /// </summary>
    private void CheckIfCanClose()
    {
        if (eyeBaseSpriteLibrary == null || eyeBaseResolver == null)
        {
            canEyeClose = false;
            return;
        }

        string closedLabel = $"{eyeLabel}_closed";
        canEyeClose = eyeBaseSpriteLibrary.GetSprite(eyesCategory, closedLabel) != null;
    }

    /// <summary>
    /// Ferme ou ouvre l’œil
    /// </summary>
    public void SetClosed(bool closed)
    {
        if (closed && !canEyeClose) return;

        isCurrentlyClosed = closed;

        string label = closed ? $"{eyeLabel}_closed" : $"{eyeLabel}_base";

        // EyeBase
        if (eyeBaseResolver != null)
        {
            eyeBaseResolver.SetCategoryAndLabel(eyesCategory, label);
            eyeBaseResolver.ResolveSpriteToSpriteRenderer();
        }

        // Sclera et pupil
        if (eyeSclera != null) eyeSclera.SetActive(!closed);
        if (eyePupil != null) eyePupil.SetActive(!closed);
    }

    /// <summary>
    /// Change la couleur de la pupille
    /// </summary>
    public void SetPupilColor(Color color)
    {
        if (eyePupilRenderer != null) eyePupilRenderer.color = color;
    }

    public void SetEyebrow(string categoryName)
    {
        this.eyebrowsCategory = categoryName;
        eyeBrowResolver.SetCategoryAndLabel(this.eyebrowsCategory, this.eyebrowState);
    }
    public void SetEyebrowState(string labelString)
    {
        eyebrowState = labelString;
        eyeBrowResolver.SetCategoryAndLabel(this.eyebrowsCategory, this.eyebrowLabel + this.eyebrowState);
    }

}
