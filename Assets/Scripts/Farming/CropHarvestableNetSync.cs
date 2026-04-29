using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Sibling NetworkBehaviour on a CropHarvestable prefab. Hosts the three NetworkVariables
    /// that drive the crop's networked visible state — sits next to <see cref="CropHarvestable"/>
    /// because Harvestable / InteractableObject is plain MonoBehaviour and can't host NetVars
    /// directly. See farming spec §6 and the 2026-04-29 single-GameObject-per-crop rework.
    ///
    /// - <see cref="CurrentStage"/>: 0..DaysToMature. Drives growth visual + maturity gate.
    /// - <see cref="IsDepleted"/>: post-harvest perennial state. Mature only.
    /// - <see cref="CropIdNet"/>: the CropSO.Id, so clients can resolve the SO from CropRegistry
    ///   on join without the server-side _crop reference being networked.
    ///
    /// Server is sole writer. Late-joiners receive the current values automatically via NGO's
    /// initial-sync, then OnValueChanged routes to <see cref="CropHarvestable.OnNetSyncChanged"/>.
    /// </summary>
    [RequireComponent(typeof(CropHarvestable))]
    public class CropHarvestableNetSync : NetworkBehaviour
    {
        public NetworkVariable<int> CurrentStage = new NetworkVariable<int>(0);
        public NetworkVariable<bool> IsDepleted = new NetworkVariable<bool>(false);
        public NetworkVariable<FixedString64Bytes> CropIdNet = new NetworkVariable<FixedString64Bytes>(default);

        private CropHarvestable _harvestable;

        private void Awake()
        {
            _harvestable = GetComponent<CropHarvestable>();
        }

        public override void OnNetworkSpawn()
        {
            CurrentStage.OnValueChanged += HandleAnyChange;
            IsDepleted.OnValueChanged += HandleAnyChange;
            CropIdNet.OnValueChanged += HandleCropIdChange;

            if (_harvestable != null) _harvestable.OnNetSyncChanged();
        }

        public override void OnNetworkDespawn()
        {
            CurrentStage.OnValueChanged -= HandleAnyChange;
            IsDepleted.OnValueChanged -= HandleAnyChange;
            CropIdNet.OnValueChanged -= HandleCropIdChange;
        }

        private void HandleAnyChange<T>(T _, T __)
        {
            if (_harvestable != null) _harvestable.OnNetSyncChanged();
        }

        private void HandleCropIdChange(FixedString64Bytes _, FixedString64Bytes __)
        {
            if (_harvestable != null) _harvestable.OnCropIdResolved();
        }
    }
}
