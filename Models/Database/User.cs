namespace Server.Models.Database;

public class User : Base
{
    [Key] public long Id { get; init; }
    [MaxLength(255)] public required string Auth0Id { get; set; }
    [MaxLength(100)] public required string JobTitle { get; set; }
    [MaxLength(200)] public required string DisplayName { get; set; }
    [MaxLength(100)] public required string GivenName { get; set; }
    [MaxLength(100)] public required string LastName { get; set; }
    [MaxLength(320)] public required string EmailAddress { get; set; }
    [MaxLength(2083)] public required string ProfilePicture { get; set; }
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public UserSettings Settings { get; set; } = null!;
    public Organization Organization { get; set; } = null!;
    public IEnumerable<Event> Events { get; } = new List<Event>();
}