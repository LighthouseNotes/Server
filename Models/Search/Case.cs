// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.Search;

public class Case
{
    public long Id { get; init; }
    public List<string>? EmailAddresses { get; set; }
    public string? DisplayId { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? LeadInvestigatorDisplayName { get; set; }
    public string? LeadInvestigatorGivenName { get; set; }
    public string? LeadInvestigatorLastName { get; set; }
}