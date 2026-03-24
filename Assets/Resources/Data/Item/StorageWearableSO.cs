using UnityEngine;

public abstract class StorageWearableSO : WearableSO
{
    [Header("Storage Configuration")]
    [Tooltip("Nombre de slots pour les objets divers et consommables")]
    [SerializeField] private int _miscCapacity;

    [Tooltip("Nombre de slots réservés aux armes")]
    [SerializeField] private int _weaponCapacity;

    public int MiscCapacity => _miscCapacity;
    public int WeaponCapacity => _weaponCapacity;

}