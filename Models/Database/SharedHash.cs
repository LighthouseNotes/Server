using System.ComponentModel.DataAnnotations;

namespace Server.Models.Database;

public class SharedHash
{
    [Key] public int Id { get; init; }
    public required string ObjectName { get; init; }
    public required string VersionId { get; init; }
    public required string Md5Hash { get; init; }
    public required string ShaHash { get; init; }
}