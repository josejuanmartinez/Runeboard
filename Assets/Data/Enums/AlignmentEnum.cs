public enum AlignmentEnum
{
    freePeople = 0,
    darkServants = 1,
    neutral = 2
}

public static class AlignmentEnumExtensions
{
    public static string ToDisplayName(this AlignmentEnum alignment) => alignment switch
    {
        AlignmentEnum.freePeople => "Free Peoples",
        AlignmentEnum.darkServants => "Dark Servants",
        _ => "Neutral"
    };
}
