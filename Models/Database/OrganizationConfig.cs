using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class OrganizationConfiguration : Base
{
    [Key] public int Id { get; set; }

    public virtual Organization Organization { get; set; } = null!;
    public required string S3Endpoint { get; set; }
    public required string S3BucketName { get; set; }
    public required bool S3NetworkEncryption { get; set; } = true;
    public required string S3AccessKey { get; set; }
    public required string S3SecretKey { get; set; }
}