namespace Server.Models.Database;

public class Exhibit : Base
{
    [Key] public long Id { get; init; }
    public required string Reference { get; init; }
    public required string Description { get; init; }
    public required DateTime DateTimeSeizedProduced { get; init; }
    public required string WhereSeizedProduced { get; init; }
    public required string SeizedBy { get; init; }
}