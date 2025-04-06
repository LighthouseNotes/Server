using System.ComponentModel.DataAnnotations.Schema;

// ReSharper disable CollectionNeverUpdated.Global

namespace Server.Models.Database;

public class User : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [MaxLength(320)]
    public required string EmailAddress { get; set; }

    [MaxLength(100)] public required string JobTitle { get; set; }
    [MaxLength(200)] public required string DisplayName { get; set; }
    [MaxLength(100)] public required string GivenName { get; set; }
    [MaxLength(100)] public required string LastName { get; set; }
    public UserSettings Settings { get; init; } = null!;
    public ICollection<Event> Events { get; } = new List<Event>();
}