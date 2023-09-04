using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LighthouseNotesServer.Models.Database;

public class Case
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public virtual Organization Organization { get; set; } = null!;
    public required string DisplayId { get; set; }
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public virtual User SIO { get; set; } = null!;
    public DateTime Modified { get; set; }
    public DateTime Created { get; init; }
    public required string Status { get; set; }
    public virtual ICollection<CaseUser> Users { get; } = new List<CaseUser>();
    public virtual ICollection<Exhibit> Exhibits { get; } = new List<Exhibit>();
    public virtual ICollection<Tab> Tabs { get; } = new List<Tab>();
    public virtual ICollection<SharedTab> SharedTabs { get; } = new List<SharedTab>();
    public virtual ICollection<Hash> Hashes { get; } = new List<Hash>();
}