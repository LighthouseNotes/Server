// ReSharper disable InconsistentNaming

namespace Server.Models.Search;

public class Case
{
    public long Id { get; set; }
    public List<long>? UserIds { get; set; }
    public string? DisplayId { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? SIODisplayName { get; set; }
    public string? SIOGivenName { get; set; }
    public string? SIOLastName { get; set; }
}