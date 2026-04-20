using UnityEngine;

/// <summary>
/// Root of the dev-mode UI. Lives on the DevModePanel prefab. Listens to
/// DevModeManager.OnDevModeChanged to show/hide its content root. Tabs (child modules)
/// self-register via Start() — no explicit registry needed for the first slice.
/// </summary>
public class DevModePanel : MonoBehaviour
{
    [SerializeField] private GameObject _contentRoot;

    private void OnEnable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged += HandleDevModeChanged;
            HandleDevModeChanged(DevModeManager.Instance.IsEnabled);
        }
    }

    private void OnDisable()
    {
        if (DevModeManager.Instance != null)
        {
            DevModeManager.Instance.OnDevModeChanged -= HandleDevModeChanged;
        }
    }

    private void HandleDevModeChanged(bool isEnabled)
    {
        if (_contentRoot != null)
        {
            _contentRoot.SetActive(isEnabled);
        }
    }
}
