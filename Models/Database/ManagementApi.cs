namespace LighthouseNotesServer.Models.Database;

public class ManagementApi : Base
{
    public int Id { get; set; }
    public required string AccessToken { get; set; }
    public DateTime Expires { get; set; }
}