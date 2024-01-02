using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models.Database;

public class Exhibit : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; init; }

    public required string Reference { get; init; }
    public required string Description { get; init; }
    public required DateTime DateTimeSeizedProduced { get; init; }
    public required string WhereSeizedProduced { get; init; }
    public required string SeizedBy { get; init; }
    public virtual ICollection<User> Users { get; } = new List<User>();
}