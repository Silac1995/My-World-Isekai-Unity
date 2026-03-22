using UnityEngine;

public class UI_PlayerTargeting : MonoBehaviour
{
    private Character _character;
    private Transform _selectedTarget;

    public void Initialize(Character character)
    {
        _character = character;
        _selectedTarget = null;
    }

    private void Update()
    {
        if (_character == null) return;

        UpdateTargeting();
    }

    private void UpdateTargeting()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var interactable = hit.collider.GetComponentInParent<InteractableObject>();
                var character = hit.collider.GetComponentInParent<Character>();

                if (interactable != null)
                {
                    _selectedTarget = interactable.transform;
                    _character.CharacterVisual.SetLookTarget(_selectedTarget);
                }
                else if (character != null && character != _character)
                {
                    _selectedTarget = character.transform;
                    _character.CharacterVisual.SetLookTarget(_selectedTarget);
                }
                else
                {
                    // Clear target
                    _selectedTarget = null;
                    _character.CharacterVisual.SetLookTarget((Transform)null);
                }
            }
        }
    }
}
