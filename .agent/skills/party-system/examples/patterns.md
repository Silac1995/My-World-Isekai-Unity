# Party System — Code Patterns

## Creating a party (server-only)
```csharp
// Requires Leadership skill on the character
character.CharacterParty.CreateParty("My Group");
// Or with default name ("{CharacterName}'s Party"):
character.CharacterParty.CreateParty();
```

## Inviting a character (via InteractionInvitation pipeline)
```csharp
// Create the invitation with the Leadership SkillSO reference
var invitation = new PartyInvitation(leadershipSkillSO);

// Execute through the standard invitation flow
if (invitation.CanExecute(source, target))
{
    invitation.Execute(source, target);
    // target.CharacterInvitation evaluates accept/refuse
    // OnAccepted -> target auto-joins source's party
}
```

## Joining a party directly (server-only, for testing/admin)
```csharp
// By party ID
npc.CharacterParty.JoinParty(partyId);

// By leader reference (convenience)
npc.CharacterParty.JoinCharacterParty(leader);
```

## Querying party state
```csharp
// Check if character is in a party
if (character.IsInParty()) { ... }
// Or directly:
if (character.CharacterParty.IsInParty) { ... }

// Check if character is the leader
if (character.CharacterParty.IsPartyLeader) { ... }

// Get party data
PartyData data = character.CharacterParty.PartyData;
string leaderName = Character.FindByUUID(data.LeaderId)?.CharacterName;
int memberCount = data.MemberCount;

// Check if two characters are in the same party
bool sameParty = charA.CharacterParty.IsInParty
    && charB.CharacterParty.IsInParty
    && charA.CharacterParty.PartyData.PartyId == charB.CharacterParty.PartyData.PartyId;
```

## Looking up parties via the registry
```csharp
// Find a party by ID
PartyData party = PartyRegistry.GetParty(partyId);

// Find which party a character belongs to
PartyData party = PartyRegistry.GetPartyForCharacter(characterId);

// Enumerate all active parties (e.g., for MacroSimulator)
foreach (PartyData party in PartyRegistry.GetAllParties())
{
    // Process each party...
}
```

## Listening to party events (UI / client-side)
```csharp
// In a UI component, bind to the local player's CharacterParty
CharacterParty party = localPlayer.CharacterParty;

party.OnJoinedParty += (data) => ShowPartyPanel(data);
party.OnLeftParty += () => HidePartyPanel();
party.OnPartyStateChanged += (state) => {
    if (state == PartyState.Gathering) ShowGatheringUI();
};
party.OnFollowModeChanged += (mode) => UpdateFollowModeDisplay(mode);
party.OnMemberKicked += (charId) => RemoveMemberFromList(charId);

// IMPORTANT: Always unsubscribe in OnDestroy()
```

## Triggering gathering (called by MapTransitionDoor / MapTransitionZone)
```csharp
// This is called internally — you typically don't call it directly.
// MapTransitionDoor.Interact() does this automatically for party leaders:
if (interactor.CharacterParty.IsPartyLeader)
{
    MapController targetMap = MapController.GetByMapId(targetMapId);
    if (targetMap.Type == MapType.Region || targetMap.Type == MapType.Dungeon)
    {
        interactor.CharacterParty.StartGathering(targetMapId, dest);
        return; // Skip normal transition
    }
}
```

## MapType usage
```csharp
// Always use the Type property (not the deprecated IsInteriorOffset)
MapController map = MapController.GetByMapId(mapId);
if (map.Type == MapType.Region) { /* outdoor area */ }
if (map.Type == MapType.Interior) { /* building interior */ }
if (map.Type == MapType.Dungeon) { /* dungeon */ }

// Setting map type (e.g., in BuildingInteriorSpawner)
mapController.SetMapType(MapType.Interior);
```
