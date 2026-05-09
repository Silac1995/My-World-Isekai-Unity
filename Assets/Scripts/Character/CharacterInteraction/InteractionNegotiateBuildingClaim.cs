using UnityEngine;
using MWI.WorldSystem;

/// <summary>
/// A community leader negotiates with the owner of a building that falls inside the community zone.
/// On acceptance, the building is adopted into the community's ConstructedBuildings.
/// On refusal, the building stays independent.
/// </summary>
public class InteractionNegotiateBuildingClaim : InteractionInvitation
{
    private readonly Building _building;
    private readonly CommunityData _community;
    private readonly MapController _mapController;

    public InteractionNegotiateBuildingClaim(Building building, CommunityData community, MapController mapController)
    {
        _building = building;
        _community = community;
        _mapController = mapController;
    }

    public override bool CanExecute(Character source, Character target)
    {
        if (_building == null || _community == null || _mapController == null) return false;

        // Source (leader) must be a leader of the community
        if (!_community.IsLeader(source.CharacterId)) return false;

        // Target must be alive
        if (!target.IsAlive()) return false;

        // Building must not already be part of the community
        if (_community.ConstructedBuildings.Exists(b => b.BuildingId == _building.BuildingId)) return false;

        return true;
    }

    public override string GetInvitationMessage(Character source, Character target)
    {
        return $"Your {_building.BuildingName} falls within our community's territory. Would you consider integrating it with us?";
    }

    public override bool? EvaluateCustomInvitation(Character source, Character target)
    {
        // NPC owners evaluate based on relationship with the leader
        if (target.CharacterRelation != null)
        {
            var relation = target.CharacterRelation.GetRelationshipWith(source);
            float relationValue = relation?.RelationValue ?? 0f;
            // Positive relationship = accept, negative = refuse
            return relationValue >= 0f;
        }

        // Fallback to default sociability check
        return null;
    }

    public override void OnAccepted(Character source, Character target)
    {
        if (_building == null || _community == null || _mapController == null) return;

        // Adopt the building into the community
        _building.transform.SetParent(_mapController.transform);

        if (!_community.ConstructedBuildings.Exists(b => b.BuildingId == _building.BuildingId))
        {
            _community.ConstructedBuildings.Add(BuildingSaveData.FromBuilding(_building, _mapController.transform.position));
        }

        Debug.Log($"<color=green>[BuildingClaim]</color> {target.CharacterName} agreed to incorporate '{_building.BuildingName}' into community '{_community.MapId}'.");
    }

    public override string GetAcceptMessage()
    {
        return "That sounds reasonable. You can count my building as part of the community.";
    }

    public override string GetRefuseMessage()
    {
        return "I'd prefer to keep my property independent for now.";
    }

    public override void OnRefused(Character source, Character target)
    {
        Debug.Log($"<color=orange>[BuildingClaim]</color> {target.CharacterName} refused to integrate '{_building.BuildingName}' into community '{_community.MapId}'.");
    }
}
