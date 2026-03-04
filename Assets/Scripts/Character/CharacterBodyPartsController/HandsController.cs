using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D.Animation;

public class HandsController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CharacterBodyPartsController _bodyPartsController;
    [SerializeField] private List<CharacterHand> _hands = new List<CharacterHand>();
    [SerializeField] private SpriteLibraryAsset _spriteLibraryAssetsHands;

    [Header("Settings")]
    [SerializeField] private string _spriteLibraryCategory = "01_human";
    [SerializeField] private bool _debugMode = true;

    public List<CharacterHand> Hands => _hands;

    public void Initialize()
    {
        RetrieveHandObjects();
    }

    private void RetrieveHandObjects()
    {
        _hands.Clear();

        Transform spritesContainer = (transform.childCount > 0) ? transform.GetChild(0) : transform;

        if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Recherche dans : {spritesContainer.name}");

        SpriteRenderer[] allRenderers = spritesContainer.GetComponentsInChildren<SpriteRenderer>(true);

        if (allRenderers.Length == 0 && _debugMode)
            Debug.LogWarning($"<color=red>[HandsController]</color> Aucun SpriteRenderer trouvé dans {spritesContainer.name} !");

        // Dictionnaires pour stocker les parties par côté
        Dictionary<string, GameObject> thumbs = new Dictionary<string, GameObject>();
        Dictionary<string, GameObject> fingers = new Dictionary<string, GameObject>();

        foreach (var renderer in allRenderers)
        {
            GameObject part = renderer.gameObject;
            string lowerName = part.name.ToLower();

            string side = "";
            if (lowerName.Contains("_l")) side = "L";
            else if (lowerName.Contains("_r")) side = "R";

            if (string.IsNullOrEmpty(side)) continue;

            if (lowerName.Contains("skin_thumb"))
            {
                thumbs[side] = part;
                if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Trouvé Thumb côté {side} : {part.name}");
            }
            else if (lowerName.Contains("skin_fingers"))
            {
                fingers[side] = part;
                if (_debugMode) Debug.Log($"<color=cyan>[HandsController]</color> Trouvé Fingers côté {side} : {part.name}");
            }
        }

        // Assemblage des mains par côté
        string[] sides = { "L", "R" };
        foreach (string s in sides)
        {
            if (thumbs.ContainsKey(s) || fingers.ContainsKey(s))
            {
                CharacterHand newHand = new CharacterHand(
                    _bodyPartsController,
                    thumbs.ContainsKey(s) ? thumbs[s] : null,
                    fingers.ContainsKey(s) ? fingers[s] : null,
                    _spriteLibraryCategory,
                    s
                );

                _hands.Add(newHand);

                if (_debugMode)
                    Debug.Log($"<color=green>[HandsController]</color> Main <b>{s}</b> assemblée. " +
                              $"(Thumb: {thumbs.ContainsKey(s)}, Fingers: {fingers.ContainsKey(s)})");
            }
            else if (_debugMode)
            {
                Debug.LogWarning($"<color=yellow>[HandsController]</color> Côté <b>{s}</b> ignoré (aucune partie trouvée).");
            }
        }

        if (_hands.Count == 0 && _debugMode)
            Debug.LogWarning($"<color=red>[HandsController]</color> Scan terminé : 0 mains trouvées. Vérifie les noms des GameObjects (Skin_Thumb_L/R, Skin_Fingers_L/R).");
    }

    public void SetHandsColor(Color color)
    {
        foreach (var hand in _hands)
        {
            hand.SetColor(color);
        }
    }

    public void SetHandsCategory(string categoryName)
    {
        _spriteLibraryCategory = categoryName;

        if (_hands.Count == 0)
        {
            Debug.LogWarning("<color=orange>[HandsController]</color> Liste vide, tentative de scan forcé.");
            RetrieveHandObjects();
        }

        foreach (var hand in _hands)
        {
            hand.SetCategory(categoryName);
        }
    }

    // --- Gestion des poses par côté ---

    /// <summary>
    /// Récupère la main du côté demandé ("L" ou "R"). Retourne null si non trouvée.
    /// </summary>
    public CharacterHand GetHand(string side)
    {
        foreach (var hand in _hands)
        {
            if (hand.Side == side) return hand;
        }

        if (_debugMode) Debug.LogWarning($"<color=orange>[HandsController]</color> Aucune main trouvée pour le côté {side}.");
        return null;
    }

    // --- Main gauche ---
    public void SetLeftHandNormal() => GetHand("L")?.SetPose("normal");
    public void SetLeftHandFist() => GetHand("L")?.SetPose("fist");

    // --- Main droite ---
    public void SetRightHandNormal() => GetHand("R")?.SetPose("normal");
    public void SetRightHandFist() => GetHand("R")?.SetPose("fist");

    // --- Les deux mains ---
    [ContextMenu("Set All Hands Normal")]
    public void SetAllHandsNormal()
    {
        foreach (var hand in _hands) hand.SetPose("normal");
    }

    [ContextMenu("Set All Hands Fist")]
    public void SetAllHandsFist()
    {
        foreach (var hand in _hands) hand.SetPose("fist");
    }
}
