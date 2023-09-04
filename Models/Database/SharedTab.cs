using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LighthouseNotesServer.Models.Database;

public class SharedTab
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    public virtual Case Case { get; set; } = null!;
    public required string Name { get; set; }
}