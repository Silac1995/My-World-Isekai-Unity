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
        if (!Input.GetMouseButtonDown(0)) return;

        // Guard: Do NOT process clicks that land on UI elements (buttons, panels, etc.)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, _targetingLayerMask))
        {
            Debug.Log("[Targeting] Raycast missed. Clearing selection.");
            ClearSelection();
            return;
        }

        Debug.Log($"[Targeting] Raycast hit: {hit.collider.gameObject.name}, attachedRB={hit.collider.attachedRigidbody != null}");

        // Require clicking on a collider that is driven by a Rigidbody
        if (hit.collider.attachedRigidbody == null)
        {
            ClearSelection();
            return;
        }

        // Resolve the hit into an InteractableObject through the same path TAB uses
        InteractableObject resolved = ResolveInteractableFromHit(hit.collider);
        if (resolved != null)
        {
            Debug.Log($"[Targeting] Resolved to: {resolved.gameObject.name}");
            SelectInteractable(resolved);
        }
        else
        {
            Debug.Log("[Targeting] ResolveInteractableFromHit returned null. Clearing.");
            ClearSelection();
        }
    }

    /// <summary>
    /// Resolves a hit collider into the correct InteractableObject to select.
    /// Characters always resolve to their root CharacterInteractable so both
    /// click and TAB go through the exact same SelectInteractable path.
    /// </summary>
    private InteractableObject ResolveInteractableFromHit(Collider hitCollider)
    {
        // Characters always resolve to the root CharacterInteractable via the facade
        var character = hitCollider.GetComponentInParent<Character>();
        if (character != null && character != _character)
        {
            var charInteractable = character.CharacterInteractable;
            if (charInteractable != null)
                return charInteractable;

            // No CharacterInteractable — fall through to generic InteractableObject search
        }

        return hitCollider.GetComponentInParent<InteractableObject>();
    }

    /// <summary>
    /// Single entry point for selecting a target. Both click and TAB converge here.
    /// Sets the LookTarget, target indicator, and PlannedTarget (if in battle).
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

        // If in battle, propagate the target to CharacterCombat so the turn system knows who to act on
        if (_character.CharacterCombat != null && _character.CharacterCombat.IsInBattle)
        {
            var targetCharacter = target.GetComponentInParent<Character>();
            if (targetCharacter != null)
            {
                _character.CharacterCombat.SetPlannedTarget(targetCharacter);
            }
        }
    }

    /// <summary>
    /// Clears the current selection and LookTarget.
    /// </summary>
    public void ClearSelection()
    {
        _selectedInteractable = null;
        _character.CharacterVisual.SetLookTarget((Transform)null);
    }
}
