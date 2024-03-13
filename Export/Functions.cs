using System.Security.Cryptography;
using System.Web;
using HtmlAgilityPack;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Server.Export;

public class Functions
{
    private readonly SqidsEncoder<long> _sqids;
    private string _caseId;
    private string _userId;
    private Database.Case _case;
    private Database.CaseUser _caseUser;
    private Database.OrganizationSettings _organizationSettings;
    private TimeZoneInfo _timeZone;

    public Functions(SqidsEncoder<long> sqids, string caseId, string userId, Database.Case sCase,
        Database.CaseUser caseUser, Database.OrganizationSettings organizationSettings, TimeZoneInfo timeZone)
    {
        _sqids = sqids;
        _caseId = caseId;
        _userId = userId;
        _case = sCase;
        _caseUser = caseUser;
        _organizationSettings = organizationSettings;
        _timeZone = timeZone;
    }

    public async Task<API.ContemporaneousNotesExport> GetContemporaneousNote(
        Database.ContemporaneousNote contemporaneousNote)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        // Variables 
        MemoryStream memoryStream = new();
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{_caseId}/{_userId}/contemporaneous-notes/{_sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.ContemporaneousNotesExport()
            {
                Content =
                    $"Can not find the S3 object for contemporaneous note: `{_sqids.Encode(contemporaneousNote.Id)}` at the following path: `{objectPath}`.",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone)
            };
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = _caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return new API.ContemporaneousNotesExport
            {
                Content =
                    $"Unable to find hash value for contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone)
            };

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return new API.ContemporaneousNotesExport
            {
                Content =
                    $"MD5 hash verification failed for contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone)
            };

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.ContemporaneousNotesExport()
            {
                Content =
                    $"SHA256 hash verification failed for contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone)
            };

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Read file contents to string
        using StreamReader sr = new(memoryStream);
        string content = await sr.ReadToEndAsync();

        // Create HTML document
        HtmlDocument htmlDoc = new();

        // Load value into HTML
        htmlDoc.LoadHtml(content);

        // If content contains images
        if (htmlDoc.DocumentNode.SelectNodes("//img") != null)
            // For each image that starts with .path/
            foreach (HtmlNode img in htmlDoc.DocumentNode.SelectNodes("//img")
                         .Where(u => u.Attributes["src"].Value.Contains(".path/")))
            {
                // Create variable with file name
                string fileName = img.Attributes["src"].Value.Replace(".path/", "");
                string imageObjectPath =
                    $"cases/{_caseId}/{_userId}/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.ContemporaneousNotesExport
        {
            Content = htmlDoc.DocumentNode.OuterHtml,
            DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone)
        };
    }

    public async Task<API.TabExport> GetTab(Database.Tab tab)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath = $"cases/{_caseId}/{_userId}/tabs/{_sqids.Encode(tab.Id)}/content.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return a HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.TabExport()
            {
                Content =
                    $"Can not find the S3 object for the tab with the ID `{_sqids.Encode(tab.Id)}` at the following path `{objectPath}`.",
                Name = tab.Name
            };
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = _caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return new API.TabExport()
            {
                Content = $"Unable to find hash values for the tab with the ID `{_sqids.Encode(tab.Id)}`!",
                Name = tab.Name
            };

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check generated MD5 hash matches the hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return new API.TabExport()
            {
                Content =
                    $"MD5 hash verification failed for tab with the ID `{_sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name
            };
        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.TabExport()
            {
                Content =
                    $"SHA256 hash verification failed for tab with the ID `{_sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name
            };

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Read file contents to string
        using StreamReader sr = new(memoryStream);
        string content = await sr.ReadToEndAsync();

        // Create HTML document
        HtmlDocument htmlDoc = new();

        // Load value into HTML
        htmlDoc.LoadHtml(content);

        // If content contains images
        if (htmlDoc.DocumentNode.SelectNodes("//img") != null)
            // For each image that starts with .path/
            foreach (HtmlNode img in htmlDoc.DocumentNode.SelectNodes("//img")
                         .Where(u => u.Attributes["src"].Value.Contains(".path/")))
            {
                // Create variable with file name
                string fileName = img.Attributes["src"].Value.Replace(".path/", "");

                // Get presigned s3 url and update image src 
                string imageObjectPath = $"cases/{_caseId}/{_userId}/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.TabExport()
        {
            Name = tab.Name,
            Content = htmlDoc.DocumentNode.OuterHtml
        };
    }

    public async Task<API.SharedContemporaneousNotesExport> GetSharedContemporaneousNote(
        Database.SharedContemporaneousNote contemporaneousNote)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        // Variables 
        MemoryStream memoryStream = new();
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{_caseId}/shared/contemporaneous-notes/{_sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.SharedContemporaneousNotesExport()
            {
                Content =
                    $"Can not find the S3 object for the shared contemporaneous note: `{_sqids.Encode(contemporaneousNote.Id)}` at the following path: `{objectPath}`.",

                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(contemporaneousNote.Creator.Id), Auth0Id = contemporaneousNote.Creator.Auth0Id,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName, LastName = contemporaneousNote.Creator.LastName,
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    ProfilePicture = contemporaneousNote.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = contemporaneousNote.Creator.Organization.DisplayName,
                        DisplayName = contemporaneousNote.Creator.Organization.DisplayName
                    },
                    Roles = contemporaneousNote.Creator.Roles.Select(r => r.Name).ToList()
                }
            };
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = _case.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return new API.SharedContemporaneousNotesExport()
            {
                Content =
                    $"Unable to find hash value for the shared contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(contemporaneousNote.Creator.Id), Auth0Id = contemporaneousNote.Creator.Auth0Id,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName, LastName = contemporaneousNote.Creator.LastName,
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    ProfilePicture = contemporaneousNote.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = contemporaneousNote.Creator.Organization.DisplayName,
                        DisplayName = contemporaneousNote.Creator.Organization.DisplayName
                    },
                    Roles = contemporaneousNote.Creator.Roles.Select(r => r.Name).ToList()
                }
            };

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return new API.SharedContemporaneousNotesExport()
            {
                Content =
                    $"MD5 hash verification failed for contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(contemporaneousNote.Creator.Id), Auth0Id = contemporaneousNote.Creator.Auth0Id,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName, LastName = contemporaneousNote.Creator.LastName,
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    ProfilePicture = contemporaneousNote.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = contemporaneousNote.Creator.Organization.DisplayName,
                        DisplayName = contemporaneousNote.Creator.Organization.DisplayName
                    },
                    Roles = contemporaneousNote.Creator.Roles.Select(r => r.Name).ToList()
                }
            };

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.SharedContemporaneousNotesExport()
            {
                Content =
                    $"SHA256 hash verification failed for contemporaneous note with the ID `{_sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(contemporaneousNote.Creator.Id), Auth0Id = contemporaneousNote.Creator.Auth0Id,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName, LastName = contemporaneousNote.Creator.LastName,
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    ProfilePicture = contemporaneousNote.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = contemporaneousNote.Creator.Organization.DisplayName,
                        DisplayName = contemporaneousNote.Creator.Organization.DisplayName
                    },
                    Roles = contemporaneousNote.Creator.Roles.Select(r => r.Name).ToList()
                }
            };

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Read file contents to string
        using StreamReader sr = new(memoryStream);
        string content = await sr.ReadToEndAsync();

        // Create HTML document
        HtmlDocument htmlDoc = new();

        // Load value into HTML
        htmlDoc.LoadHtml(content);

        // If content contains images
        if (htmlDoc.DocumentNode.SelectNodes("//img") != null)
            // For each image that starts with .path/
            foreach (HtmlNode img in htmlDoc.DocumentNode.SelectNodes("//img")
                         .Where(u => u.Attributes["src"].Value.Contains(".path/")))
            {
                // Create variable with file name
                string fileName = img.Attributes["src"].Value.Replace(".path/", "");
                string imageObjectPath =
                    $"cases/{_caseId}/shared/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetSharedImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.SharedContemporaneousNotesExport()
        {
            Content = htmlDoc.DocumentNode.OuterHtml,
            Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, _timeZone),
            Creator = new API.User
            {
                Id = _sqids.Encode(contemporaneousNote.Creator.Id), Auth0Id = contemporaneousNote.Creator.Auth0Id,
                JobTitle = contemporaneousNote.Creator.JobTitle,
                DisplayName = contemporaneousNote.Creator.DisplayName,
                GivenName = contemporaneousNote.Creator.GivenName, LastName = contemporaneousNote.Creator.LastName,
                EmailAddress = contemporaneousNote.Creator.EmailAddress,
                ProfilePicture = contemporaneousNote.Creator.ProfilePicture,
                Organization = new API.Organization
                {
                    Name = contemporaneousNote.Creator.Organization.DisplayName,
                    DisplayName = contemporaneousNote.Creator.Organization.DisplayName
                },
                Roles = contemporaneousNote.Creator.Roles.Select(r => r.Name).ToList()
            }
        };
    }

    public async Task<API.SharedTabExport> GetSharedTab(Database.SharedTab tab)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath = $"cases/{_caseId}/shared/tabs/{_sqids.Encode(tab.Id)}/content.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return a HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.SharedTabExport
            {
                Content =
                    $"Can not find the S3 object for the tab with the ID `{_sqids.Encode(tab.Id)}` at the following path `{objectPath}`.",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(tab.Creator.Id), Auth0Id = tab.Creator.Auth0Id,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName, LastName = tab.Creator.LastName,
                    EmailAddress = tab.Creator.EmailAddress,
                    ProfilePicture = tab.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = tab.Creator.Organization.DisplayName, DisplayName = tab.Creator.Organization.DisplayName
                    },
                    Roles = tab.Creator.Roles.Select(r => r.Name).ToList()
                }
            };
        }

        Database.SharedHash? objectHashes = _case.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return new API.SharedTabExport
            {
                Content = $"Unable to find hash values for the tab with the ID `{_sqids.Encode(tab.Id)}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(tab.Creator.Id), Auth0Id = tab.Creator.Auth0Id,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName, LastName = tab.Creator.LastName,
                    EmailAddress = tab.Creator.EmailAddress,
                    ProfilePicture = tab.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = tab.Creator.Organization.DisplayName, DisplayName = tab.Creator.Organization.DisplayName
                    },
                    Roles = tab.Creator.Roles.Select(r => r.Name).ToList()
                }
            };

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check generated MD5 hash matches the hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return new API.SharedTabExport
            {
                Content =
                    $"MD5 hash verification failed for tab with the ID `{_sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(tab.Creator.Id), Auth0Id = tab.Creator.Auth0Id,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName, LastName = tab.Creator.LastName,
                    EmailAddress = tab.Creator.EmailAddress,
                    ProfilePicture = tab.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = tab.Creator.Organization.DisplayName, DisplayName = tab.Creator.Organization.DisplayName
                    },
                    Roles = tab.Creator.Roles.Select(r => r.Name).ToList()
                }
            };
        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.SharedTabExport
            {
                Content =
                    $"SHA256 hash verification failed for tab with the ID `{_sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, _timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(tab.Creator.Id), Auth0Id = tab.Creator.Auth0Id,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName, LastName = tab.Creator.LastName,
                    EmailAddress = tab.Creator.EmailAddress,
                    ProfilePicture = tab.Creator.ProfilePicture,
                    Organization = new API.Organization
                    {
                        Name = tab.Creator.Organization.DisplayName, DisplayName = tab.Creator.Organization.DisplayName
                    },
                    Roles = tab.Creator.Roles.Select(r => r.Name).ToList()
                }
            };

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Read file contents to string
        using StreamReader sr = new(memoryStream);
        string content = await sr.ReadToEndAsync();

        // Create HTML document
        HtmlDocument htmlDoc = new();

        // Load value into HTML
        htmlDoc.LoadHtml(content);

        // If content contains images
        if (htmlDoc.DocumentNode.SelectNodes("//img") != null)
            // For each image that starts with .path/
            foreach (HtmlNode img in htmlDoc.DocumentNode.SelectNodes("//img")
                         .Where(u => u.Attributes["src"].Value.Contains(".path/")))
            {
                // Create variable with file name
                string fileName = img.Attributes["src"].Value.Replace(".path/", "");

                // Get presigned s3 url and update image src 
                string imageObjectPath = $"cases/{_caseId}/shared/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetSharedImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.SharedTabExport
        {
            Name = tab.Name,
            Content = htmlDoc.DocumentNode.OuterHtml,
            Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, _timeZone),
            Creator = new API.User
            {
                Id = _sqids.Encode(tab.Creator.Id), Auth0Id = tab.Creator.Auth0Id,
                JobTitle = tab.Creator.JobTitle,
                DisplayName = tab.Creator.DisplayName,
                GivenName = tab.Creator.GivenName, LastName = tab.Creator.LastName,
                EmailAddress = tab.Creator.EmailAddress,
                ProfilePicture = tab.Creator.ProfilePicture,
                Organization = new API.Organization
                {
                    Name = tab.Creator.Organization.DisplayName, DisplayName = tab.Creator.Organization.DisplayName
                },
                Roles = tab.Creator.Roles.Select(r => r.Name).ToList()
            }
        };
    }

    private async Task<string> GetImage(string objectPath)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        ObjectStat? objectMetadata;
        // Fetch object metadata
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // If object does not exist return a HTTP 404 error
        catch (ObjectNotFoundException)
        {
            return "./Export/image-error.jpeg";
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = _caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null) return "./Export/image-error.jpeg";

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check generated MD5 hash matches the hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return "./Export/image-error.jpeg";

        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return "./Export/image-error.jpeg";

        // Fetch presigned url for object  
        string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        return presignedS3Url;
    }

    private async Task<string> GetSharedImage(string objectPath)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(_organizationSettings.S3Endpoint)
            .WithCredentials(_organizationSettings.S3AccessKey, _organizationSettings.S3SecretKey)
            .WithSSL(_organizationSettings.S3NetworkEncryption)
            .Build();

        ObjectStat? objectMetadata;
        // Fetch object metadata
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // If object does not exist return a HTTP 404 error
        catch (ObjectNotFoundException)
        {
            return "./Export/image-error.jpeg";
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = _case.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null) return "./Export/image-error.jpeg";

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check generated MD5 hash matches the hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return "./Export/image-error.jpeg";

        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return "./Export/image-error.jpeg";

        // Fetch presigned url for object  
        string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_organizationSettings.S3BucketName)
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        return presignedS3Url;
    }
}