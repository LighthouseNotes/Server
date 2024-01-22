// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Server.Models.API;

public class Export
{
    public required string DisplayName { get; init; }
    public required User SIO { get; init; }
    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }
    public required string Status { get; init; }
    public required ICollection<User> Users { get; init; }
    public List<ContemporaneousNotesExport> ContemporaneousNotes { get; set; } = new();
    public List<TabExport> Tabs { get; set; } = new();

    public List<SharedContemporaneousNotesExport> SharedContemporaneousNotes { get; set; } = new();
    public List<SharedTabExport> SharedTabs { get; set; } = new();
}

public class ContemporaneousNotesExport
{
    public required string Content { get; init; }
    public DateTime DateTime { get; init; }
}

public class SharedContemporaneousNotesExport
{
    public required string Content { get; init; }
    public DateTime Created { get; init; }
    public required User Creator { get; init; }
}

public class TabExport
{
    public required string Name { get; init; }
    public required string Content { get; init; }
}

public class SharedTabExport
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public required DateTime Created { get; init; }
    public required User Creator { get; init; }
}