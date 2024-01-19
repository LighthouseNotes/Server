// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.API;

public class Tab
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required DateTime Created { get; set; }
}

public class SharedTab
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required DateTime Created { get; set; }
    public required User Creator { get; set; }
}

public class AddTab
{
    public required string Name { get; set; }
}