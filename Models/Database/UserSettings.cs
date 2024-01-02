using System.ComponentModel.DataAnnotations;

namespace Server.Models.Database;

public class UserSettings : Base
{
    [Key] public int Id { get; init; }
    public virtual User User { get; init; } = null!;
    [MaxLength(100)] public required string TimeZone { get; set; }
    [MaxLength(50)] public required string DateFormat { get; set; }
    [MaxLength(50)] public required string TimeFormat { get; set; }
    [MaxLength(5)] public required string Locale { get; set; }
}