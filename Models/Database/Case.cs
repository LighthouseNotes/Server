using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models.Database;

public class Case : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; init; }

    [MaxLength(10)] public required string DisplayId { get; set; }
    [MaxLength(90)] public required string Name { get; set; }
    [MaxLength(100)] public required string DisplayName { get; set; }
    [MaxLength(50)] public required string Status { get; set; }
    public virtual ICollection<CaseUser> Users { get; } = new List<CaseUser>();
    public virtual ICollection<SharedContemporaneousNote> SharedContemporaneousNotes { get; init; } = null!;
    public virtual ICollection<SharedTab> SharedTabs { get; init; } = null!;
    public virtual ICollection<SharedHash> SharedHashes { get; init; } = null!;
    public virtual ICollection<Exhibit> Exhibits { get; } = new List<Exhibit>();
}