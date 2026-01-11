using UnityEngine;

public abstract class StorageWearableSO : WearableSO
{
    [Header("Storage Configuration")]
    [SerializeField] private int _capacity = 10;
    public int Capacity => _capacity;

    // Pas de CreateAssetMenu ici, c'est une classe de base
}