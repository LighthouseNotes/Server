namespace Server.Models.API;

public class OrganizationSettings
{
    public string? S3Endpoint { get; init; }
    public string? S3BucketName { get; init; }
    public bool S3NetworkEncryption { get; init; } = true;
    public string? S3AccessKey { get; init; }
    public string? S3SecretKey { get; init; }
}

public class UserSettings
{
    public required string TimeZone { get; init; }
    public required string DateFormat { get; init; }
    public required string TimeFormat { get; init; }
    public required string Locale { get; init; }
}