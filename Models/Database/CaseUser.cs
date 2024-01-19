namespace Server.Models.Database;

public class CaseUser : Base
{
    [Key] public long Id { get; init; }
    public User User { get; init; } = null!;
    public bool IsSIO { get; set; }
    public Case Case { get; set; } = null!;
    public ICollection<ContemporaneousNote> ContemporaneousNotes { get; init; } = null!;
    public ICollection<Tab> Tabs { get; init; } = null!;
    public ICollection<Hash> Hashes { get; init; } = null!;
}