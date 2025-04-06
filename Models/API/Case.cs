// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Server.Models.API;

public class Case
{
    public required string Id { get; init; }
    public required string DisplayId { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required User LeadInvestigator { get; init; }
    public DateTime Modified { get; init; }
    public DateTime Accessed { get; init; }
    public DateTime Created { get; init; }
    public required string Status { get; init; }
    public required ICollection<User> Users { get; init; }
}

public class AddCase
{
    public required string DisplayId { get; set; }
    public required string Name { get; set; }
    public string? LeadInvestigatorEmailAddress { get; set; }
    public List<string>? EmailAddresses { get; set; }
}

public class UpdateCase
{
    public string? DisplayId { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? LeadInvestigatorEmailAddress { get; set; } = null;
    public string? Status { get; set; } = null;
}