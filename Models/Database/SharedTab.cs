namespace Server.Models.Database;

public class SharedTab : Base
{
    [Key] public long Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }

    public User Creator { get; init; } = null!;
    public Case Case { get; init; } = null!;
}