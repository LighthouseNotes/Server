// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable CollectionNeverUpdated.Global

using System.Security.Cryptography;
using System.Text;

namespace Server.Models.API;

public class User
{
    public required string EmailAddress { get; init; }
    public required string JobTitle { get; init; }
    public required string DisplayName { get; init; }
    public required string GivenName { get; init; }
    public required string LastName { get; init; }
    public string ProfilePicture => GenerateProfilePictureUrl(EmailAddress);

    private static string GenerateProfilePictureUrl(string email)
    {
        // Create SHA256 hash of user email address
        byte[] sha256Hash = SHA256.HashData(Encoding.ASCII.GetBytes(email));

        // Convert hash to string
        string emailHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant();

        // Return URL for gravatar avatar with email hash in URL
        return $"https://gravatar.com/avatar/{emailHash}";
    }
}

public class AddUser
{
    public required string JobTitle { get; set; }
    public required string GivenName { get; set; }
    public required string LastName { get; set; }
    public required string DisplayName { get; set; }
}

public class UpdateUser
{
    public string? JobTitle { get; set; }
    public string? GivenName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
}

public class UserSettings
{
    public required string TimeZone { get; init; }
    public required string DateFormat { get; init; }
    public required string TimeFormat { get; init; }
    public required string Locale { get; init; }
}