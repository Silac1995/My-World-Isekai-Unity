using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UI_InteractionMenu : MonoBehaviour
{
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform buttonsContainer;
    
    private Transform followTarget;
    private Camera mainCamera;
    
    private void Awake()
    {
        mainCamera = Camera.main;
    }

    public void Initialize(List<InteractableObject.InteractionOption> options)
    {
        // Nettoyer les anciens boutons si besoin (bien qu'il vienne d'être instancié)
        foreach (Transform child in buttonsContainer)
        {
            Destroy(child.gameObject);
        }
        
        foreach (var option in options)
        {
            GameObject btnObj = Instantiate(buttonPrefab, buttonsContainer);
            Button btn = btnObj.GetComponent<Button>();
            TextMeshProUGUI txt = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            
            if (txt != null)
                txt.text = option.Name;
                
            btn.onClick.AddListener(() => 
            {
                option.Action?.Invoke();
                CloseMenu();
            });
        }
    }

    public void CloseMenu()
    {
        gameObject.SetActive(false);
    }
}
