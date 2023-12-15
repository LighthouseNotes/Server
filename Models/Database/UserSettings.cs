using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class UserSettings : Base
{
    [Key]
    public int Id { get; set; }
    public virtual User? User { get; set; } = null!;
    public required string TimeZone { get; set; }
    public required string DateFormat { get; set; }
    public required string TimeFormat { get; set; }
    public required string Locale { get; set; }
}