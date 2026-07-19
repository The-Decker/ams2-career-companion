namespace Companion.Core.Career;

/// <summary>
/// The career's MORTALITY axis (character death &amp; injury, docs/dev/character-death-injury.md §2),
/// chosen once at creation and carried forward like <see cref="PlayerCareerState.FormAware"/> and the
/// SMGP gates. <see cref="Off"/> (default 0) means no injury and no death, the classic behavior, and
/// because it is the enum default it is omitted from the serialized start state
/// (<c>[JsonIgnore(WhenWritingDefault)]</c>), so every pre-feature career stays BYTE-IDENTICAL.
/// </summary>
public enum MortalityMode
{
    /// <summary>No injury, no death, the current behavior. The default (0), so it serializes to
    /// nothing and a career that never opts in is byte-identical to a pre-feature save.</summary>
    Off = 0,

    /// <summary>Injury/death ON, with a full FILE-level save &amp; reload safety net (manual slots +
    /// autosave); a snapshot may be restored at any time, including to un-do a death.</summary>
    Normal = 1,

    /// <summary>Injury/death ON, and there is NO save system at all, no manual saves, no restore,
    /// ever. The live career file just plays forward, and on death it is physically DELETED. (Mike:
    /// "NO RESTORE EVER IN HARDCORE AND THE SAVE GETS PHYSICALLY DELETED.")</summary>
    Hardcore = 2,
}
