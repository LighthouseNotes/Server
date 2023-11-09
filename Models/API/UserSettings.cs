namespace LighthouseNotesServer.Models.API;

public class UserSettings
{
    public required string TimeZone { get; set; }
    public required string DateFormat { get; set; }
    public required string TimeFormat { get; set; }
    public required string Locale { get; set; }
}