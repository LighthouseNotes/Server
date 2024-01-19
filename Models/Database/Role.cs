namespace Server.Models.Database;

public class Role : Base
{
    [Key] public long Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }
}