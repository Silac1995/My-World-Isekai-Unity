using System.Text;
using TMPro;
using UnityEngine;
using MWI.WorldSystem;

namespace MWI.UI.CityManagement
{
    /// <summary>
    /// TierUpTab — surfaces next-tier requirements with progress + a Promote button.
    /// Click fires <see cref="AdministrativeBuilding.RequestPromoteLevelServerRpc"/>; the
    /// ClientRpc result toast is logged (Task 8 wires a proper toast channel).
    ///
    /// Plan 4c Task 7.
    /// </summary>
    public class UI_TierUpTab : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text _currentLevelLabel;
        [SerializeField] private TMP_Text _nextLevelLabel;
        [SerializeField] private TMP_Text _requirementsBody;
        [SerializeField] private UnityEngine.UI.Button _promoteButton;

        private AdministrativeBuilding _ab;

        public void Initialize(AdministrativeBuilding ab)
        {
            _ab = ab;
            if (_promoteButton != null)
            {
                _promoteButton.onClick.RemoveAllListeners();
                _promoteButton.onClick.AddListener(OnPromoteClicked);
            }
        }

        /// <summary>Pull a fresh snapshot of the next-tier progress from the bound AB.</summary>
        public void RefreshFromAB()
        {
            if (_ab == null || _ab.OwnerCommunity == null) return;

            var community = _ab.OwnerCommunity;
            var currentTier = community.CurrentTier;
            string currentLabel = currentTier != null ? currentTier.DisplayName : community.level.ToString();
            if (_currentLevelLabel != null) _currentLevelLabel.text = $"Current: {currentLabel}";

            // Walk the SO ladder (supports designer-authored off-enum tiers); fall back
            // to the legacy enum next-level lookup for pre-migration saves.
            var nextReq = currentTier != null
                ? MWI.WorldSystem.CommunityTierRegistry.GetNext(currentTier)
                : MWI.WorldSystem.CommunityTierRegistry.GetForNextLevelFrom(community.level);
            if (nextReq == null)
            {
                if (_nextLevelLabel != null) _nextLevelLabel.text = "Max tier reached.";
                if (_requirementsBody != null) _requirementsBody.text = string.Empty;
                if (_promoteButton != null) _promoteButton.interactable = false;
                return;
            }

            if (_nextLevelLabel != null) _nextLevelLabel.text = $"Next: {nextReq.DisplayName}";

            var sb = new StringBuilder();
            int memberCount = community.members != null ? community.members.Count : 0;
            sb.Append("Population: ").Append(memberCount).Append("/").Append(nextReq.MinPopulation);
            sb.Append(memberCount >= nextReq.MinPopulation ? " ✓\n" : " ✗\n");

            int treasury = _ab.GetTreasuryBalance(MWI.Economy.CurrencyId.Default);
            sb.Append("Treasury: ").Append(treasury).Append("/").Append(nextReq.MinTreasury);
            sb.Append(treasury >= nextReq.MinTreasury ? " ✓\n" : " ✗\n");

            if (nextReq.RequiredBuildings != null && nextReq.RequiredBuildings.Count > 0)
            {
                sb.Append("Buildings:\n");
                // Group by SO to avoid printing the same name multiple times.
                var seen = new System.Collections.Generic.HashSet<BuildingSO>();
                for (int i = 0; i < nextReq.RequiredBuildings.Count; i++)
                {
                    var so = nextReq.RequiredBuildings[i];
                    if (so == null || !seen.Add(so)) continue;
                    int needed = CountInList(nextReq.RequiredBuildings, so);
                    int have = CountOwnedCompleted(community, so);
                    string label = string.IsNullOrEmpty(so.BuildingName) ? so.name : so.BuildingName;
                    sb.Append("  ").Append(label).Append(": ").Append(have).Append("/").Append(needed);
                    sb.Append(have >= needed ? " ✓\n" : " ✗\n");
                }
            }
            if (_requirementsBody != null) _requirementsBody.text = sb.ToString();

            // Optimistic gating — server re-validates on the RPC; local state may be
            // slightly stale (rule #19b client-side pre-gate compromise).
            bool localPass = memberCount >= nextReq.MinPopulation
                          && treasury >= nextReq.MinTreasury
                          && AllRequiredBuildingsMet(community, nextReq);
            if (_promoteButton != null) _promoteButton.interactable = localPass;
        }

        private static bool AllRequiredBuildingsMet(Community community, MWI.WorldSystem.CommunityTierRequirementsSO req)
        {
            if (req.RequiredBuildings == null) return true;
            var seen = new System.Collections.Generic.HashSet<BuildingSO>();
            for (int i = 0; i < req.RequiredBuildings.Count; i++)
            {
                var so = req.RequiredBuildings[i];
                if (so == null || !seen.Add(so)) continue;
                int needed = CountInList(req.RequiredBuildings, so);
                int have = CountOwnedCompleted(community, so);
                if (have < needed) return false;
            }
            return true;
        }

        private static int CountInList(System.Collections.Generic.IReadOnlyList<BuildingSO> list, BuildingSO so)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == so) n++;
            return n;
        }

        private static int CountOwnedCompleted(Community community, BuildingSO so)
        {
            if (community.ownedBuildings == null) return 0;
            int n = 0;
            for (int i = 0; i < community.ownedBuildings.Count; i++)
            {
                var b = community.ownedBuildings[i];
                if (b == null || b.IsUnderConstruction) continue;
                if (b.Blueprint == so) n++;
            }
            return n;
        }

        private void OnPromoteClicked()
        {
            if (_ab == null) return;
            _ab.RequestPromoteLevelServerRpc();
        }
    }
}
