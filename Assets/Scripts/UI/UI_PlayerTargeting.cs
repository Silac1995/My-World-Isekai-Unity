using UnityEngine;
using UnityEngine.EventSystems;

public class UI_PlayerTargeting : MonoBehaviour
{
    private Character _character;
    private Transform _lastTrackedTarget;
    private InteractableObject _selectedInteractable;

    [Header("UI Elements")]
    [SerializeField] private UI_TargetIndicator _targetIndicator;
    [Tooltip("Base height offset above the target.")]
    [SerializeField] private float _yOffset = 1.0f;

    [Header("Targeting Settings")]
    [Tooltip("Layers that can be clicked to select a target. Usually 'RigidBody' or 'InteractionCollider'.")]
    [SerializeField] private LayerMask _targetingLayerMask = ~0; // ~0 means Everything

    /// <summary>
    /// The currently selected InteractableObject, if any.
    /// Used by PlayerInteractionDetector to lock E-interaction to this target.
    /// </summary>
    public InteractableObject SelectedInteractable => _selectedInteractable;

    public void Initialize(Character character)
    {
        _character = character;
        _lastTrackedTarget = null;
        _selectedInteractable = null;
    }

    private void Update()
    {
        if (_character == null) return;

        UpdateTargeting();
        UpdateIndicatorTracking();
    }

    private void UpdateIndicatorTracking()
    {
        if (_targetIndicator == null) return;

        if (_character.CharacterVisual.HasLookTarget)
        {
            Transform activeTarget = _character.CharacterVisual.LookTarget;

            if (_lastTrackedTarget != activeTarget)
            {
                _lastTrackedTarget = activeTarget;
                _targetIndicator.SetTarget(activeTarget);
            }

            if (!_targetIndicator.gameObject.activeSelf)
                _targetIndicator.gameObject.SetActive(true);

            Vector3 worldPos = activeTarget.position + Vector3.up * _yOffset;
            
            var col = activeTarget.GetComponentInChildren<Collider>();
            if (col != null)
            {
                // Anchor to the true top-center of the object's physical bounds
                worldPos = new Vector3(col.bounds.center.x, col.bounds.max.y, col.bounds.center.z) + Vector3.up * _yOffset;
            }

            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
            _targetIndicator.transform.position = screenPos;
        }
        else
        {
            if (_lastTrackedTarget != null)
            {
                _lastTrackedTarget = null;
                _targetIndicator.SetTarget(null);
            }

            if (_targetIndicator.gameObject.activeSelf)
                _targetIndicator.gameObject.SetActive(false);
        }
    }

    private void UpdateTargeting()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Guard: Do NOT process clicks that land on UI elements (buttons, panels, etc.)
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _targetingLayerMask))
            {
                Debug.Log($"[Targeting] Raycast hit collider: {hit.collider.gameObject.name} at {hit.point}");

                // Require clicking on a collider that is driven by a Rigidbody
                if (hit.collider.attachedRigidbody == null)
                {
                    Debug.Log($"[Targeting] The hit collider '{hit.collider.name}' does not have an attached Rigidbody. Clearing target.");
                    ClearSelection();
                    return;
                }

                var interactable = hit.collider.GetComponentInParent<InteractableObject>();
                var character = hit.collider.GetComponentInParent<Character>();

                if (interactable != null)
                {
                    Debug.Log($"[Targeting] Valid Rigidbody belongs to InteractableObject: {interactable.gameObject.name}. Setting target.");
                    SelectInteractable(interactable);
                }
                else if (character != null && character != _character)
                {
                    // Resolve the Character's CharacterInteractable so we have a proper InteractableObject reference
                    var charInteractable = character.GetComponent<CharacterInteractable>();
                    if (charInteractable != null)
                    {
                        Debug.Log($"[Targeting] Valid Rigidbody belongs to Character: {character.gameObject.name}. Setting target via CharacterInteractable.");
                        SelectInteractable(charInteractable);
                    }
                    else
                    {
                        Debug.Log($"[Targeting] Character {character.gameObject.name} has no CharacterInteractable. Setting LookTarget only.");
                        _selectedInteractable = null;
                        _character.CharacterVisual.SetLookTarget(character.transform);
                    }
                }
                else
                {
                    Debug.Log("[Targeting] Hit a Rigidbody, but it is neither an InteractableObject nor another Character. Clearing target.");
                    ClearSelection();
                }
            }
            else
            {
                ClearSelection();
            }
        }
    }

    /// <summary>
    /// Selects an InteractableObject as the current target. 
    /// Sets the LookTarget on CharacterVisual so the sprite faces the target 
    /// and the target indicator tracks it.
    /// Called by click raycast or externally by TAB cycling.
    /// </summary>
    public void SelectInteractable(InteractableObject target)
    {
        if (target == null)
        {
            ClearSelection();
            return;
        }

        _selectedInteractable = target;
        _character.CharacterVisual.SetLookTarget(target.transform);
        Debug.Log($"<color=cyan>[Targeting]</color> Selected interactable: {target.name}");
    }

    /// <summary>
    /// Clears the current selection and LookTarget.
    /// </summary>
    public void ClearSelection()
    {
        if (_selectedInteractable != null || _character.CharacterVisual.HasLookTarget)
        {
            Debug.Log("[Targeting] Clearing selection.");
        }
        _selectedInteractable = null;
        _character.CharacterVisual.SetLookTarget((Transform)null);
    }
}
