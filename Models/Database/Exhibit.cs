using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LighthouseNotesServer.Models.Database;

public class Exhibit : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public virtual Case Case { get; set; } = null!;
    public required string Reference { get; set; }
    public required string Description { get; set; }
    public required DateTime DateTimeSeizedProduced { get; set; }
    public required string WhereSeizedProduced { get; set; }
    public required string SeizedBy { get; set; }
    public virtual ICollection<ExhibitUser> Users { get; } = new List<ExhibitUser>();
}