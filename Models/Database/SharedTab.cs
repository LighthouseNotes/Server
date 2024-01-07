using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Models.Database;

public class SharedTab : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }

    public virtual User Creator { get; set; } = null!;
}