using UnityEngine;

public class UI_PlayerTargeting : MonoBehaviour
{
    private Character _character;

    [Header("UI Elements")]
    [SerializeField] private RectTransform _targetIndicator;
    [SerializeField] private float _yOffset = 1.0f;

    [Header("Targeting Settings")]
    [Tooltip("Layers that can be clicked to select a target. Usually 'RigidBody' or 'InteractionCollider'.")]
    [SerializeField] private LayerMask _targetingLayerMask = ~0; // ~0 means Everything

    public void Initialize(Character character)
    {
        _character = character;
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
            if (!_targetIndicator.gameObject.activeSelf)
                _targetIndicator.gameObject.SetActive(true);

            Transform activeTarget = _character.CharacterVisual.LookTarget;
            Vector3 worldPos = activeTarget.position + Vector3.up * _yOffset;
            
            // Try to use the collider's center if available
            var col = activeTarget.GetComponentInChildren<Collider>();
            if (col != null)
            {
                worldPos = col.bounds.center;
            }

            Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
            _targetIndicator.position = screenPos;
        }
        else
        {
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
