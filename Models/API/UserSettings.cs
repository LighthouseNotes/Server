namespace LighthouseNotesServer.Models.API;

public class UserSettings
{
    public required string TimeZone { get; init; }
    public required string DateFormat { get; init; }
    public required string TimeFormat { get; init; }
    public required string Locale { get; init; }
}