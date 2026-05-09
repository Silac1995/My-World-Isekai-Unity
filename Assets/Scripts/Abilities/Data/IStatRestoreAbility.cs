using System.Collections.Generic;

public interface IStatRestoreAbility
{
    IReadOnlyList<StatRestoreEntry> StatRestoresOnTarget { get; }
    IReadOnlyList<StatRestoreEntry> StatRestoresOnSelf { get; }
}
