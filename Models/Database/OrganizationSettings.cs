namespace Server.Models.Database;

public class OrganizationSettings : Base
{
    [Key] public int Id { get; init; }

    public Organization Organization { get; init; } = null!;
    [MaxLength(2083)] public required string S3Endpoint { get; set; }
    [MaxLength(63)] public required string S3BucketName { get; set; }
    public required bool S3NetworkEncryption { get; set; } = true;
    [MaxLength(20)] public required string S3AccessKey { get; set; }
    [MaxLength(40)] public required string S3SecretKey { get; set; }
    [MaxLength(2083)] public required string MeilisearchUrl { get; set; }
    [MaxLength(64)] public required string MeilisearchApiKey { get; set;}
}