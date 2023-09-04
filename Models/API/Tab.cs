namespace LighthouseNotesServer.Models.API;

public class Tab
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}

public class AddTab
{
    public required string Name { get; set; }
}