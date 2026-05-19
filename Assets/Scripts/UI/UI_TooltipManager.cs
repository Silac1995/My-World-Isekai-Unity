using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UI_TooltipManager : MonoBehaviour
{
    public static UI_TooltipManager Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI _descriptionText;
    [Tooltip("Render order vs other canvases. 999 keeps the tooltip above any UI_WindowBase variant (sortingOrder=50).")]
    [SerializeField] private int _sortingOrder = 999;

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

        // Promote to a top-most overlay canvas so the tooltip is never occluded by a
        // ScreenSpaceCamera variant window. If a Canvas already exists on this GameObject,
        // just bump it to override sorting; otherwise add one + a GraphicRaycaster.
        EnsureTopMostCanvas();

        gameObject.SetActive(false);
    }

    private void EnsureTopMostCanvas()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
        }
        canvas.overrideSorting = true;
        canvas.sortingOrder = _sortingOrder;
        if (GetComponent<GraphicRaycaster>() == null)
        {
            // GraphicRaycaster is needed for the canvas to participate in pointer raycasts,
            // but the tooltip itself is not interactable — set raycastTarget=false on its
            // children to avoid blocking clicks. Adding the raycaster is harmless.
            gameObject.AddComponent<GraphicRaycaster>();
        }
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
