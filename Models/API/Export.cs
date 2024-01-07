namespace Server.Models.API;

public class Export
{
    public required string DisplayName { get; set; }
    public required User SIO { get; set; }
    public DateTime Modified { get; set; }
    public DateTime Created { get; set; }
    public required string Status { get; set; }
    public required ICollection<User> Users { get; init; }
    public List<ContemporaneousNotesExport> ContemporaneousNotes { get; set; } = new();
    public List<TabExport> Tabs { get; set; } = new();
}

public class ContemporaneousNotesExport
{
    public required string Content { get; set; }
    public DateTime DateTime {get; set; }
}

public class TabExport
{
    public string Name { get; set; }
    public string Content { get; set; }
}

public class SharedTabExport
{
    public string Name { get; set; }
    public string Content { get; set; }
}