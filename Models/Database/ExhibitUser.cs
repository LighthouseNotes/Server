using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class ExhibitUser
{
    [Key] public int Id { get; set; }

    public virtual Exhibit Exhibit { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}