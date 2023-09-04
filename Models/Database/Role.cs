using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class Role
{
    [Key] public int Id { get; set; }

    public required string Name { get; set; }
    public virtual User User { get; set; } = null!;
}