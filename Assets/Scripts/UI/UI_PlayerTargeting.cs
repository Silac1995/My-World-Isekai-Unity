using UnityEngine;

public class UI_PlayerTargeting : MonoBehaviour
{
    private Character _character;
    private Transform _lastTrackedTarget;

    [Header("UI Elements")]
    [SerializeField] private UI_TargetIndicator _targetIndicator;
    [Tooltip("Base height offset above the target.")]
    [SerializeField] private float _yOffset = 1.0f;

    [Header("Targeting Settings")]
    [Tooltip("Layers that can be clicked to select a target. Usually 'RigidBody' or 'InteractionCollider'.")]
    [SerializeField] private LayerMask _targetingLayerMask = ~0; // ~0 means Everything

    public void Initialize(Character character)
    {
        _character = character;
        _lastTrackedTarget = null;
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
            
            // Try to use the collider's center if available
            var col = activeTarget.GetComponentInChildren<Collider>();
            if (col != null)
            {
                // Base it off the true center of the object instead of its pivot (feet)
                worldPos = col.bounds.center + Vector3.up * _yOffset;
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
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _targetingLayerMask))
            {
                Debug.Log($"[Targeting] Raycast hit collider: {hit.collider.gameObject.name} at {hit.point}");

                // Require clicking on a collider that is driven by a Rigidbody
                if (hit.collider.attachedRigidbody == null)
                {
                    Debug.Log($"[Targeting] The hit collider '{hit.collider.name}' does not have an attached Rigidbody. Clearing target.");
                    ClearTarget();
                    return;
                }

                var interactable = hit.collider.GetComponentInParent<InteractableObject>();
                var character = hit.collider.GetComponentInParent<Character>();

                if (interactable != null)
                {
                    Debug.Log($"[Targeting] Valid Rigidbody belongs to InteractableObject: {interactable.gameObject.name}. Setting target.");
                    _character.CharacterVisual.SetLookTarget(interactable.transform);
                }
                else if (character != null && character != _character)
                {
                    Debug.Log($"[Targeting] Valid Rigidbody belongs to Character: {character.gameObject.name}. Setting target.");
                    _character.CharacterVisual.SetLookTarget(character.transform);
                }
                else
                {
                    Debug.Log("[Targeting] Hit a Rigidbody, but it is neither an InteractableObject nor another Character. Clearing target.");
                    ClearTarget();
                }
            }
            else
            {
                ClearTarget();
            }
        }
    }

    private void ClearTarget()
    {
        if (_character.CharacterVisual.HasLookTarget)
        {
            Debug.Log("[Targeting] Clicked empty space or invalid object. Clearing target.");
            _character.CharacterVisual.SetLookTarget((Transform)null);
        }
    }
}
