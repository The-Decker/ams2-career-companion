namespace Companion.Core.Character;

/// <summary>One pure, version-selected source of truth for character level thresholds. Versions
/// 0 and 1 delegate to the shipped geometric curve unchanged; version 2 uses the deterministic
/// integer L300 curve from <c>docs/dev/character-progression-v2.md</c>.</summary>
public static class CharacterLevelProgression
{
    public const int LegacyVersion = 0;
    public const int EraCappedVersion = 1;
    public const int Level300Version = 2;
    public const int Level300Max = 300;

    public static int MaxLevel(int progressionVersion, int year, CharacterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return progressionVersion switch
        {
            LegacyVersion => rules.Levels.XpCurve.MaxLevel,
            EraCappedVersion => Math.Min(
                rules.Levels.XpCurve.MaxLevel,
                rules.Levels.SoftCapForYear(year) ?? rules.Levels.XpCurve.MaxLevel),
            Level300Version => Level300Max,
            _ => throw Unsupported(progressionVersion),
        };
    }

    /// <summary>XP required to advance from <paramref name="level"/> - 1 to that level.</summary>
    public static long XpForLevel(int progressionVersion, int level, CharacterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return progressionVersion switch
        {
            LegacyVersion or EraCappedVersion => rules.Levels.XpCurve.XpForLevel(level),
            Level300Version => Level300XpForLevel(level),
            _ => throw Unsupported(progressionVersion),
        };
    }

    /// <summary>Total XP required to have reached <paramref name="level"/>.</summary>
    public static long CumulativeXpToLevel(
        int progressionVersion,
        int level,
        CharacterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int max = progressionVersion == Level300Version
            ? Level300Max
            : progressionVersion is LegacyVersion or EraCappedVersion
                ? rules.Levels.XpCurve.MaxLevel
                : throw Unsupported(progressionVersion);
        if (level <= 1)
            return 0;
        if (level > max)
            throw new ArgumentOutOfRangeException(
                nameof(level), level, $"Level {level} exceeds progression version {progressionVersion}'s maximum {max}.");

        long cumulative = 0;
        for (int target = 2; target <= level; target++)
            cumulative = checked(cumulative + XpForLevel(progressionVersion, target, rules));
        return cumulative;
    }

    public static int LevelForTotalXp(
        int progressionVersion,
        long totalXp,
        int year,
        CharacterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return progressionVersion switch
        {
            LegacyVersion => rules.Levels.XpCurve.LevelForTotalXp(totalXp),
            EraCappedVersion => rules.Levels.LevelForTotalXp(totalXp, year, useEraSoftCap: true),
            Level300Version => Level300ForTotalXp(totalXp),
            _ => throw Unsupported(progressionVersion),
        };
    }

    private static long Level300XpForLevel(int level)
    {
        if (level < 2)
            return 0;
        if (level > Level300Max)
            throw new ArgumentOutOfRangeException(
                nameof(level), level, $"Level {level} exceeds the version-2 maximum {Level300Max}.");

        return 40L + 21L * (level - 2) / 298L;
    }

    private static int Level300ForTotalXp(long totalXp)
    {
        int level = 1;
        long cumulative = 0;
        for (int target = 2; target <= Level300Max; target++)
        {
            cumulative += Level300XpForLevel(target);
            if (totalXp < cumulative)
                break;
            level = target;
        }
        return level;
    }

    private static NotSupportedException Unsupported(int version) =>
        new($"Character progression version {version} is not supported by this build.");
}
