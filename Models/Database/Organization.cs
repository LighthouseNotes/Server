using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LighthouseNotesServer.Models.Database;

public class Organization
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required string Id { get; set; }

    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public virtual ICollection<User> Users { get; } = new List<User>();
    public virtual ICollection<Case> Cases { get; } = new List<Case>();
    public virtual OrganizationConfiguration? Configuration { get; set; }
}