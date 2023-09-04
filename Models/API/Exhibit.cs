using System.Runtime.Serialization;

namespace LighthouseNotesServer.Models.API;

public class Exhibit
{
    public Guid Id { get; set; }
    public required string Reference { get; set; }
    public required string Description { get; set; }
    public required DateTime DateTimeSeizedProduced { get; set; }
    public required string WhereSeizedProduced { get; set; }
    public required string SeizedBy { get; set; }
    public required ICollection<ExhibitUser> Users { get; set; }
}

public class ExhibitUser
{
    public required string Id { get; set; }
    public required string DisplayName { get; set; }
    public required string JobTitle { get; set; }
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