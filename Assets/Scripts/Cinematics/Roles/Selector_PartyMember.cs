using UnityEngine;

namespace MWI.Cinematics
{
    /// <summary>
    /// Resolves to the Nth member of a party. The party can be the triggering player's
    /// (default) or the other participant's. Used to bind party members as role actors —
    /// the canonical Phase 1 way to fill a multi-actor cinematic with the player's
    /// companions.
    ///
    /// <para>
    /// Index semantics — <see cref="PartyData.MemberIds"/> always stores the leader at
    /// index 0 (per <c>PartyData.ctor</c>). Followers occupy indices 1..N-1. So:
    /// </para>
    /// <list type="bullet">
    /// <item>Index 0 = party leader (typically the triggering player themselves)</item>
    /// <item>Index 1 = first follower</item>
    /// <item>Index 2 = second follower, etc.</item>
    /// </list>
    /// <para>
    /// Returns null + warns if the source character has no party, or if the index is
    /// out of bounds. Designer should mark the role <see cref="RoleSlot.IsOptional"/>
    /// when the cinematic should still play with a smaller party (drops missing followers).
    /// </para>
    /// <para>
    /// Resolution uses <see cref="Character.FindByUUID(string)"/> — the project's
    /// canonical lookup — to map <c>MemberIds[i]</c> back to the live <c>Character</c>
    /// instance.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        fileName = "Selector_PartyMember",
        menuName = "MWI/Cinematics/Selectors/Party Member")]
    public class Selector_PartyMember : RoleSelectorSO
    {
        public enum PartyOf
        {
            /// <summary>Use the triggering player's party (most common — leader scenario).</summary>
            TriggeringPlayer,
            /// <summary>Use the other participant's party (rare — e.g., scene about *their* companions).</summary>
            OtherParticipant
        }

        [Tooltip("Whose party we're indexing into. Default: TriggeringPlayer (the player who fired the cinematic).")]
        [SerializeField] private PartyOf _partyOf = PartyOf.TriggeringPlayer;

        [Tooltip("Index into PartyData.MemberIds. 0 = leader (typically the player). 1 = first follower, 2 = second, etc.")]
        [SerializeField] private int _memberIndex = 1;

        public PartyOf SourceParty => _partyOf;
        public int MemberIndex => _memberIndex;

        public override Character Resolve(CinematicContext ctx)
        {
            Character source = _partyOf == PartyOf.TriggeringPlayer
                ? ctx?.TriggeringPlayer
                : ctx?.OtherParticipant;

            if (source == null)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_PartyMember: source character ({_partyOf}) is null on scene '{ctx?.Scene?.SceneId}'.");
                return null;
            }

            var party = source.CharacterParty;
            if (party == null || !party.IsInParty || party.PartyData == null)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_PartyMember: '{source.CharacterName}' is not in a party — cannot resolve member index {_memberIndex}.");
                return null;
            }

            var memberIds = party.PartyData.MemberIds;
            if (_memberIndex < 0 || _memberIndex >= memberIds.Count)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_PartyMember: index {_memberIndex} out of range (party has {memberIds.Count} members).");
                return null;
            }

            string memberId = memberIds[_memberIndex];
            var member = Character.FindByUUID(memberId);
            if (member == null)
            {
                Debug.LogWarning($"<color=yellow>[Cinematic]</color> Selector_PartyMember: member id '{memberId}' not found in active scene (despawned / hibernated?).");
            }
            return member;
        }
    }
}
