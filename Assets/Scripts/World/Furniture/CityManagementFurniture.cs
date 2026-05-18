using UnityEngine;

/// <summary>
/// AB-preplaced furniture (Plan 4c Task 7). When a community leader (any member of
/// <see cref="AdministrativeBuilding.OwnerCommunity"/>.leaders) interacts with it, the
/// <see cref="UI_CityManagementPanel"/> opens with the three management tabs
/// (TierUp / PlaceBuilding / JoinRequests) bound to the parent AB.
///
/// Local-player-only on the client side. NPC leaders use the AB's ServerRpc surface
/// directly (PlaceCityBlueprintServerRpc / AcceptJoinRequestServerRpc / etc.) — they
/// do NOT open this UI.
///
/// Inherits <see cref="Furniture"/> (not OccupiableFurniture) — leaders interact and
/// step away; no seat lock.
///
/// Plan 4c Task 7.
/// </summary>
public class CityManagementFurniture : Furniture
{
    private AdministrativeBuilding _ab;

    protected override void Awake()
    {
        base.Awake();
        _ab = GetComponentInParent<AdministrativeBuilding>();
    }

    private void TryRegisterWithAB()
    {
        if (_ab == null)
        {
            _ab = GetComponentInParent<AdministrativeBuilding>();
        }
    }

    public override bool OnInteract(Character interactor)
    {
        TryRegisterWithAB();
        if (_ab == null)
        {
            Debug.LogWarning($"<color=orange>[CityManagementFurniture]</color> '{name}' has no AdministrativeBuilding parent.");
            return false;
        }
        if (interactor == null || _ab.OwnerCommunity == null) return false;
        if (!_ab.OwnerCommunity.IsLeader(interactor))
        {
            Debug.Log($"<color=#88aaff>[CityManagementFurniture]</color> {interactor.CharacterName} tapped E but is not a leader of '{_ab.OwnerCommunity.communityName}' — silent no-op.");
            return false;
        }

        // Local-player-only UI surface. NPC leaders use AB.* ServerRpcs directly.
        if (!interactor.IsLocalPlayer) return false;

        if (PlayerUI.Instance == null)
        {
            Debug.LogWarning("<color=orange>[CityManagementFurniture]</color> PlayerUI.Instance is null; cannot open city management window.");
            return false;
        }

        PlayerUI.Instance.OpenCityManagementWindow(_ab);
        return true;
    }
}
