// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.Search;

public class User
{
    public string? EmailAddress { get; init; }
    public string? JobTitle { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? LastName { get; init; }
}