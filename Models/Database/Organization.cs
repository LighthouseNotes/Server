using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models.Database;

public class Organization : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(255)]
    public required string Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }
    [MaxLength(255)] public required string DisplayName { get; init; }
    public virtual IEnumerable<User> Users { get; } = new List<User>();
    public virtual OrganizationSettings Settings{ get; init; } = null!;
}