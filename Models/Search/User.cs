namespace Server.Models.Search;

public class User
{
    public long Id { get; set; }
    public string? JobTitle { get; init; }
    public string? DisplayName { get; init; }
    public string? GivenName { get; init; }
    public string? LastName { get; init; }
}