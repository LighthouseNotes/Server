namespace LighthouseNotesServer.Models.API;

public class User
{
    public required string Id { get; set; }
    public required string JobTitle { get; set; }
    public required string DisplayName { get; set; }
    public required string GivenName { get; set; }
    public required string LastName { get; set; }
    public required string EmailAddress { get; set; }
    public required string ProfilePicture { get; set; }
    public required Organization Organization { get; set; }
    public required List<string> Roles { get; set; }
}

public class AddUser
{
    public required string Id { get; set; }
    public required string JobTitle { get; set; }
    public required string GivenName { get; set; }
    public required string LastName { get; set; }
    public required string DisplayName { get; set; }
    public required string EmailAddress { get; set; }
    public required string ProfilePicture { get; set; }
    public required List<string> Roles { get; set; }
}

public class UpdateUser
{
    public string? JobTitle { get; set; } = null;
    public string? GivenName { get; set; } = null;
    public string? LastName { get; set; } = null;
    public string? DisplayName { get; set; } = null;
    public string? EmailAddress { get; set; } = null;
}