using Unity.Netcode;
using UnityEngine;

namespace MWI.Farming
{
    /// <summary>
    /// Sibling NetworkBehaviour on a CropHarvestable prefab. Exists because the inherited
    /// Harvestable / InteractableObject chain is plain MonoBehaviour, so the NetworkVariable
    /// for the "ready vs depleted" sprite has to live on a separate component. See farming
    /// spec §6 (perennial harvestable visual sync) and §9.1 (NetworkVariable late-joiner sync).
    ///
    /// Server is authoritative — only the server writes IsDepleted. Late-joiners receive the
    /// current value automatically via NGO's NetworkVariable initial-sync, then OnValueChanged
    /// fires once and routes to <see cref="CropHarvestable.ApplyDepletedVisual"/>.
    /// </summary>
    [RequireComponent(typeof(CropHarvestable))]
    public class CropHarvestableNetSync : NetworkBehaviour
    {
        public NetworkVariable<bool> IsDepleted = new NetworkVariable<bool>(false);

        private CropHarvestable _harvestable;

        private void Awake()
        {
            _harvestable = GetComponent<CropHarvestable>();
        }

        public override void OnNetworkSpawn()
        {
            IsDepleted.OnValueChanged += HandleIsDepletedChanged;
            // Apply current value once on join (covers host + late-joining clients).
            if (_harvestable != null) _harvestable.ApplyDepletedVisual(IsDepleted.Value);
        }

        public override void OnNetworkDespawn()
        {
            IsDepleted.OnValueChanged -= HandleIsDepletedChanged;
        }

        private void HandleIsDepletedChanged(bool _, bool isNow)
        {
            if (_harvestable != null) _harvestable.ApplyDepletedVisual(isNow);
        }
    }
}
