using System.Runtime.Serialization;
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace LighthouseNotesServer.Models.API;

public class Exhibit
{
    public Guid Id { get; init; }
    public required string Reference { get; init; }
    public required string Description { get; init; }
    public required DateTime DateTimeSeizedProduced { get; init; }
    public required string WhereSeizedProduced { get; init; }
    public required string SeizedBy { get; init; }
    public required ICollection<User> Users { get; init; }
}


public class AddExhibit
{
    public required string Reference { get; set; }
    public required string Description { get; set; }
    public required DateTime DateTimeSeizedProduced { get; set; }
    public required string WhereSeizedProduced { get; set; }
    public required string SeizedBy { get; set; }
    [DataMember(Name = "UserIds", EmitDefaultValue = false)]
    public List<string>? UserIds { get; set; }
}