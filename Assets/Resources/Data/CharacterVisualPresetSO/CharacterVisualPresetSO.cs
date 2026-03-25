using UnityEngine;

public abstract class CharacterVisualPresetSO : ScriptableObject
{
    [SerializeField] private GameObject _characterDefaultPrefab;
    [SerializeField] private GameObject _characterSpritePrefab;

    public GameObject CharacterSpritePrefab => _characterSpritePrefab;

    public bool HasVisualPrefab() => _characterSpritePrefab != null;

    // On peut ajouter une méthode abstraite pour forcer l'application
    // public abstract void ApplyTo(CharacterVisual visual);
}