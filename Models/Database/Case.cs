namespace Server.Models.Database;

public class Case : Base
{
    [Key] public long Id { get; init; }
    [MaxLength(10)] public required string DisplayId { get; set; }
    [MaxLength(90)] public required string Name { get; set; }
    [MaxLength(100)] public required string DisplayName { get; init; }
    [MaxLength(50)] public required string Status { get; set; }
    public ICollection<CaseUser> Users { get; } = new List<CaseUser>();
    public ICollection<SharedContemporaneousNote> SharedContemporaneousNotes { get; init; } = null!;
    public ICollection<SharedTab> SharedTabs { get; init; } = null!;
    public ICollection<SharedHash> SharedHashes { get; init; } = null!;
    public ICollection<Exhibit> Exhibits { get; } = new List<Exhibit>();
}