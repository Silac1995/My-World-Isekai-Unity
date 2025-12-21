using UnityEngine;
using System.Collections.Generic;

public class WorldUIManager : MonoBehaviour
{
    //public static WorldUIManager Instance { get; private set; }

    //[Header("Prefabs")]
    //[SerializeField] private GameObject interactionPromptPrefab;
    //[SerializeField] private GameObject healthBarPrefab;

    //private GameObject currentPrompt;
    ////private Dictionary<Character, HealthBarUI> activeHealthBars = new();

    //private void Awake()
    //{
    //    if (Instance != null && Instance != this)
    //    {
    //        Destroy(gameObject);
    //        return;
    //    }
    //    Instance = this;
    //}

    //public void ShowPrompt(Transform target)
    //{
    //    HidePrompt(); // Un seul prompt actif à la fois

    //    if (interactionPromptPrefab == null)
    //    {
    //        Debug.LogError("interactionPromptPrefab non assigné.", this);
    //        return;
    //    }

    //    currentPrompt = Instantiate(interactionPromptPrefab, transform);
    //    currentPrompt.GetComponent<InteractionPromptUI>()?.SetTarget(target);
    //}

    //public void HidePrompt()
    //{
    //    if (currentPrompt != null)
    //    {
    //        Destroy(currentPrompt);
    //        currentPrompt = null;
    //    }
    //}

    //public void ShowHealthBar(Character character)
    //{
    //    if (activeHealthBars.ContainsKey(character)) return;

    //    GameObject barGO = Instantiate(healthBarPrefab, transform);
    //    HealthBarUI bar = barGO.GetComponent<HealthBarUI>();
    //    bar.SetTarget(character);
    //    activeHealthBars[character] = bar;
    //}

    //public void HideHealthBar(Character character)
    //{
    //    if (activeHealthBars.TryGetValue(character, out HealthBarUI bar))
    //    {
    //        Destroy(bar.gameObject);
    //        activeHealthBars.Remove(character);
    //    }
    //}
}
