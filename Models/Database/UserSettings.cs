namespace Server.Models.Database;

public class UserSettings : Base
{
    [Key] public long Id { get; init; }
    public User User { get; init; } = null!;
    [MaxLength(100)] public required string TimeZone { get; set; }
    [MaxLength(50)] public required string DateFormat { get; set; }
    [MaxLength(50)] public required string TimeFormat { get; set; }
    [MaxLength(5)] public required string Locale { get; set; }
}