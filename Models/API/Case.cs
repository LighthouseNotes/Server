// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.API;

public class Case
{
    public required string Id { get; init; }
    public required string DisplayId { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required User SIO { get; init; }
    public DateTime Modified { get; init; }
    public DateTime Created { get; init; }
    public required string Status { get; init; }
    public required ICollection<User> Users { get; init; }
}

public class AddCase
{
    public required string DisplayId { get; set; }
    public required string Name { get; set; }
    public string? SIOUserId { get; set; }
    public List<string>? UserIds { get; set; }
}

public class UpdateCase
{
    public string? DisplayId { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? SIOUserId { get; set; } = null;
    public string? Status { get; set; } = null;
    public List<string>? UserIds { get; set; } = null;
}