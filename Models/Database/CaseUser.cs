using System.ComponentModel.DataAnnotations;

namespace Server.Models.Database;

public class CaseUser : Base
{
    [Key] public int Id { get; init; }
    public virtual User User { get; init; } = null!;
    public bool IsSIO { get; set; }
    public virtual ICollection<ContemporaneousNote> ContemporaneousNotes { get; init; } = null!;
    public virtual ICollection<Tab> Tabs { get; init; } = null!;
    public virtual ICollection<Hash> Hashes { get; init; } = null!;
}