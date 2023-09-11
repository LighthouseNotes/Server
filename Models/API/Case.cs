using System.Runtime.Serialization;

namespace LighthouseNotesServer.Models.API;

public class Case
{
    public Guid Id { get; set; }
    public required string DisplayId { get; set; }
    public required string Name { get; set; }
    public required string DisplayName { get; set; }
    public required User SIO { get; set; }
    public DateTime Modified { get; set; }
    public DateTime Created { get; set; }
    public required string Status { get; set; }
    public required ICollection<User> Users { get; init; }
}

public class AddCase
{
    public required string DisplayId { get; set; }
    public required string Name { get; set; }
    
    [DataMember(Name = "SIOUserId", EmitDefaultValue = false)]
    public string? SIOUserId { get; set; }

    [DataMember(Name = "UserIds", EmitDefaultValue = false)]
    public List<string>? UserIds { get; set; }
}

public class UpdateCase
{
    public string? DisplayId { get; set; } = null;
    public string? Name { get; set; } = null;
    public string? SIOUserId { get; set; } = null;
    public string? Status { get; set; } = null;
    public List<string>? UserIds { get; set; } = null;
}