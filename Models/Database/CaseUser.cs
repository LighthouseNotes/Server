using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class CaseUser
{
    [Key] public int Id { get; set; }

    public virtual Case Case { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}