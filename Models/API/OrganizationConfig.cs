namespace LighthouseNotesServer.Models.API;

public class OrganizationConfig
{
    public string? S3Endpoint { get; init; }
    public string? S3BucketName { get; init; }
    public bool S3NetworkEncryption { get; init; } = true;
    public string? S3AccessKey { get; init; }
    public string? S3SecretKey { get; init; }
}