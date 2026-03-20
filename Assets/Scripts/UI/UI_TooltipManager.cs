using UnityEngine;
using TMPro;

public class UI_TooltipManager : MonoBehaviour
{
    public static UI_TooltipManager Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI _descriptionText;
    private RectTransform _rectTransform;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;
        _rectTransform = GetComponent<RectTransform>();
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!gameObject.activeSelf) return;

        Vector2 position = Input.mousePosition;
        
        // Determine pivot based on screen quadrant to avoid clipping
        float pivotX = position.x / Screen.width;
        float pivotY = position.y / Screen.height;
        
        // Add a small 0.05 margin so it doesn't render exactly under the cursor causing click-blocks
        _rectTransform.pivot = new Vector2(pivotX > 0.5f ? 1.05f : -0.05f, pivotY > 0.5f ? 1.05f : -0.05f);
        
        transform.position = position;
    }

    public void ShowTooltip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            HideTooltip();
            return;
        }

        if (_descriptionText != null)
        {
            _descriptionText.text = text;
        }
        
        // Immediately update position so it doesn't flash at the old location for a frame
        Update();
        gameObject.SetActive(true);
    }

    public void HideTooltip()
    {
        gameObject.SetActive(false);
    }
}
