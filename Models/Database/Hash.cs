namespace Server.Models.Database;

public class Hash : Base
{
    [Key] public long Id { get; init; }
    [StringLength(1024)] public required string ObjectName { get; init; }
    [StringLength(255)] public required string VersionId { get; init; }
    [StringLength(32)] public required string Md5Hash { get; init; }
    [StringLength(64)] public required string ShaHash { get; init; }
}