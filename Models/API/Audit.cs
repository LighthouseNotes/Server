namespace LighthouseNotesServer.Models.API;

public class UserAudit
{
    public DateTime DateTime { get; set; }
    public required string Action { get; set; }
}