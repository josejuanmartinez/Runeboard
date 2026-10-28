public enum AIDifficulty
{
    Easy = 1,
    Normal = 2,
    Hard = 3,
    VeryHard = 4
}

public static class AIDifficultySettings
{
    public static AIDifficulty CurrentDifficulty { get; set; } = AIDifficulty.Normal;

    // Cards each AI character may play per turn. Human characters act once a turn (the
    // champion twice), so Normal gives AI characters one order each (it used to be two,
    // which with many more characters than the player let the AI out-act them heavily).
    public static int PicksPerCharacter => CurrentDifficulty switch
    {
        AIDifficulty.Easy => 1,
        AIDifficulty.Normal => 1,
        AIDifficulty.Hard => 2,
        _ => 3
    };
}
