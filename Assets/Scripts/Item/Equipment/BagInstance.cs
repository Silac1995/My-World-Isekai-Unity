using System.Collections.Generic;
using UnityEngine;

public class BagInstance : StorageWearableInstance
{
    public BagSO BagData => ItemSO as BagSO;

    public BagInstance(ItemSO data) : base(data) { }

    public void InitializeBagCapacity(int bonusMiscCapacity = 0)
    {
        if (BagData != null)
        {
            // On calcule le total pour les Misc, les Weapons restent fixes selon le SO
            int finalMisc = BagData.MiscCapacity + bonusMiscCapacity;

            // On appelle la méthode parente avec les deux valeurs
            InitializeStorage(finalMisc, BagData.WeaponCapacity);
        }
    }
}