namespace Server.Models.Database;

public class Exhibit : Base
{
    [Key] public long Id { get; init; }
    [MaxLength(10)] public required string Reference { get; init; }
    [MaxLength(200)] public required string Description { get; init; }
    public required DateTime DateTimeSeizedProduced { get; init; }
    [MaxLength(200)] public required string WhereSeizedProduced { get; init; }
    [MaxLength(200)] public required string SeizedBy { get; init; }
}