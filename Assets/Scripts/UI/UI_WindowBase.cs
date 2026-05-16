using UnityEngine;
using UnityEngine.UI;

public class UI_WindowBase : MonoBehaviour
{
    [Header("Window Base")]
    [SerializeField] private Button _buttonClose;

    protected virtual void Awake()
    {
        if (_buttonClose != null)
        {
            _buttonClose.onClick.AddListener(CloseWindow);
        }

        // Project convention: every UI_WindowBase variant uses RenderMode.ScreenSpaceCamera
        // (see CLAUDE.md rule #39 + wiki/systems/player-hud.md). Prefab assets can't
        // reference scene cameras, so we resolve the worldCamera at runtime via Camera.main.
        // Without this, a ScreenSpaceCamera canvas with worldCamera=null renders nothing.
        // Looks at the root + every child so it handles both authoring shapes:
        //   - Canvas on the root (legacy non-variant panels like UI_StorageFurniturePanel).
        //   - Canvas on the inherited 'Canvas' child (variants of UI_WindowBase.prefab).
        var canvases = GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            var c = canvases[i];

            // Safeguard against UI_WindowBase.prefab's historical zero-scale Canvas
            // RectTransform default — a variant inheriting scale=(0,0,0) renders nothing
            // even if everything else is correct. Force back to identity scale here so
            // a forgotten variant override doesn't silently break the window.
            // (See wiki/systems/player-hud.md "scale=(0,0,0) invisibility" gotcha.)
            var rt = c.transform as RectTransform;
            if (rt != null && rt.localScale.sqrMagnitude < 0.0001f)
            {
                rt.localScale = Vector3.one;
            }

            if (c.renderMode != RenderMode.ScreenSpaceCamera) continue;
            if (c.worldCamera != null) continue;
            c.worldCamera = Camera.main;
            if (c.worldCamera == null)
            {
                Debug.LogWarning($"<color=orange>[UI_WindowBase]</color> {name}: Canvas '{c.name}' is ScreenSpaceCamera but Camera.main is null at Awake — the window will not render. Ensure a Camera tagged 'MainCamera' exists in the scene before any UI_WindowBase variant initialises, or assign canvas.worldCamera manually.");
            }
        }
    }

    protected virtual void OnDestroy()
    {
        if (_buttonClose != null)
        {
            _buttonClose.onClick.RemoveListener(CloseWindow);
        }
    }

    public virtual void OpenWindow()
    {
        gameObject.SetActive(true);
    }

    public virtual void CloseWindow()
    {
        gameObject.SetActive(false);
    }
}
