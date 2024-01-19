namespace Server.Models.Database;

public class SharedContemporaneousNote : Base
{
    [Key] public long Id { get; init; }
    public virtual User Creator { get; set; } = null!;
}