/// <summary>
/// Identifier for a character skill. Phase 1 ships only the Builder slot — used by
/// CharacterAction_FinishConstruction's per-tick consume budget formula
/// (see docs/superpowers/specs/2026-05-06-building-construction-loop-design.md).
///
/// Future phases append additional skill IDs (Crafting, Combat, Foraging, etc.) without
/// renumbering existing entries. Save-data round-trip relies on the int values being
/// stable, so always append — never reorder.
/// </summary>
public enum SkillId
{
    Builder = 0,
}
