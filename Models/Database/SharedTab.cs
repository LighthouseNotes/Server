namespace Server.Models.Database;

public class SharedTab : Base
{
    [Key] public long Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }

    public User Creator { get; set; } = null!;
    public Case Case { get; set; } = null!;
}