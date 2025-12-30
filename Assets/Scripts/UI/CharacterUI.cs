using TMPro;
using UnityEngine;

public class CharacterUI : MonoBehaviour
{
    [Header("Targeting")]
    [SerializeField] private Character character;
    [SerializeField] private bool useColliderTop = true; // Se place automatiquement au sommet du perso
    [SerializeField] private Transform manualAnchor;    // Ou utilise une ancre placée à la main

    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI nameText;

    private Collider targetCollider;
    private string lastCharacterName;

    private void Start()
    {
        if (character != null)
        {
            targetCollider = character.GetComponentInChildren<Collider>();
            UpdateNameText();
        }
    }

    private void LateUpdate()
    {
        if (character == null || Camera.main == null) return;

        // --- 1. CALCUL DE LA POSITION ---
        Vector3 targetPos;
        if (manualAnchor != null)
        {
            targetPos = manualAnchor.position;
        }
        else if (useColliderTop && targetCollider != null)
        {
            // Se place au sommet du collider
            targetPos = new Vector3(targetCollider.bounds.center.x, targetCollider.bounds.max.y, targetCollider.bounds.center.z);
        }
        else
        {
            // Fallback sur le transform + offset simple
            targetPos = character.transform.position + Vector3.up * 2f;
        }

        transform.position = targetPos;

        // --- 2. BILLBOARD (FACE À LA CAMÉRA) ---
        // On copie la rotation de la caméra pour que le texte soit TOUJOURS 
        // parfaitement parallèle à l'écran du joueur (standard en UI World Space)
        transform.rotation = Camera.main.transform.rotation;

        // --- 3. MISE À JOUR DU NOM ---
        if (character.CharacterName != lastCharacterName)
        {
            UpdateNameText();
        }
    }

    private void UpdateNameText()
    {
        if (nameText != null && character != null)
        {
            lastCharacterName = character.CharacterName;
            nameText.text = lastCharacterName;
        }
    }
}