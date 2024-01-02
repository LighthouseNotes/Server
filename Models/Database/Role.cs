using System.ComponentModel.DataAnnotations;

namespace Server.Models.Database;

public class Role : Base
{
    [Key] public int Id { get; init; }

    [MaxLength(50)] public required string Name { get; init; }
}