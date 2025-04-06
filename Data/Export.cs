using System.Security.Cryptography;
using System.Web;
using HtmlAgilityPack;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Server.Data;

public class Functions(
    SqidsEncoder<long> sqids,
    string caseId,
    string emailAddress,
    Database.Case sCase,
    Database.CaseUser sCaseUser,
    TimeZoneInfo timeZone,
    IConfiguration configuration)
{
    public async Task<API.ContemporaneousNotesExport> GetContemporaneousNote(
        Database.ContemporaneousNote contemporaneousNote)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Variables
        MemoryStream memoryStream = new();
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Create a variable for object path
        string objectPath =
            $"cases/{caseId}/{emailAddress}/contemporaneous-notes/{sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.ContemporaneousNotesExport
            {
                Content =
                    $"Can not find the S3 object for contemporaneous note: `{sqids.Encode(contemporaneousNote.Id)}` at the following path: `{objectPath}`.",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
            };
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = sCaseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return new API.ContemporaneousNotesExport
            {
                Content =
                    $"Unable to find hash value for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
            };

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
                    $"MD5 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
            };

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.ContemporaneousNotesExport
            {
                Content =
                    $"SHA256 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
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
                    $"cases/{caseId}/{emailAddress}/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.ContemporaneousNotesExport
        {
            Content = htmlDoc.DocumentNode.OuterHtml, DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
        };
    }

    public async Task<API.TabExport> GetTab(Database.Tab tab)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create a variable for object path
        string objectPath = $"cases/{caseId}/{emailAddress}/tabs/{sqids.Encode(tab.Id)}/content.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return an HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.TabExport
            {
                Content =
                    $"Can not find the S3 object for the tab with the ID `{sqids.Encode(tab.Id)}` at the following path `{objectPath}`.",
                Name = tab.Name
            };
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = sCaseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return am HTTP 500 error
        if (objectHashes == null)
            return new API.TabExport
            {
                Content = $"Unable to find hash values for the tab with the ID `{sqids.Encode(tab.Id)}`!", Name = tab.Name
            };

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
            return new API.TabExport
            {
                Content =
                    $"MD5 hash verification failed for tab with the ID `{sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name
            };
        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.TabExport
            {
                Content =
                    $"SHA256 hash verification failed for tab with the ID `{sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
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
                string imageObjectPath = $"cases/{caseId}/{emailAddress}/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.TabExport { Name = tab.Name, Content = htmlDoc.DocumentNode.OuterHtml };
    }

    public async Task<API.SharedContemporaneousNotesExport> GetSharedContemporaneousNote(
        Database.SharedContemporaneousNote contemporaneousNote)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Variables
        MemoryStream memoryStream = new();
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Create a variable for object path
        string objectPath =
            $"cases/{caseId}/shared/contemporaneous-notes/{sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.SharedContemporaneousNotesExport
            {
                Content =
                    $"Can not find the S3 object for the shared contemporaneous note: `{sqids.Encode(contemporaneousNote.Id)}` at the following path: `{objectPath}`.",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName,
                    LastName = contemporaneousNote.Creator.LastName
                }
            };
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return new API.SharedContemporaneousNotesExport
            {
                Content =
                    $"Unable to find hash value for the shared contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName,
                    LastName = contemporaneousNote.Creator.LastName
                }
            };

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
            return new API.SharedContemporaneousNotesExport
            {
                Content =
                    $"MD5 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName,
                    LastName = contemporaneousNote.Creator.LastName
                }
            };

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.SharedContemporaneousNotesExport
            {
                Content =
                    $"SHA256 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = contemporaneousNote.Creator.EmailAddress,
                    JobTitle = contemporaneousNote.Creator.JobTitle,
                    DisplayName = contemporaneousNote.Creator.DisplayName,
                    GivenName = contemporaneousNote.Creator.GivenName,
                    LastName = contemporaneousNote.Creator.LastName
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
                    $"cases/{caseId}/shared/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetSharedImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.SharedContemporaneousNotesExport
        {
            Content = htmlDoc.DocumentNode.OuterHtml,
            Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
            Creator = new API.User
            {
                EmailAddress = contemporaneousNote.Creator.EmailAddress,
                JobTitle = contemporaneousNote.Creator.JobTitle,
                DisplayName = contemporaneousNote.Creator.DisplayName,
                GivenName = contemporaneousNote.Creator.GivenName,
                LastName = contemporaneousNote.Creator.LastName
            }
        };
    }

    public async Task<API.SharedTabExport> GetSharedTab(Database.SharedTab tab)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create a variable for object path
        string objectPath = $"cases/{caseId}/shared/tabs/{sqids.Encode(tab.Id)}/content.txt";

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return an HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return new API.SharedTabExport
            {
                Content =
                    $"Can not find the S3 object for the tab with the ID `{sqids.Encode(tab.Id)}` at the following path `{objectPath}`.",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = tab.Creator.EmailAddress,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName,
                    LastName = tab.Creator.LastName
                }
            };
        }

        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null)
            return new API.SharedTabExport
            {
                Content = $"Unable to find hash values for the tab with the ID `{sqids.Encode(tab.Id)}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
                Creator = new API.User
                {
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName,
                    LastName = tab.Creator.LastName,
                    EmailAddress = tab.Creator.EmailAddress
                }
            };

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
                    $"MD5 hash verification failed for tab with the ID `{sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = tab.Creator.EmailAddress,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName,
                    LastName = tab.Creator.LastName
                }
            };
        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return new API.SharedTabExport
            {
                Content =
                    $"SHA256 hash verification failed for tab with the ID `{sqids.Encode(tab.Id)}` at the path `{objectPath}`!",
                Name = tab.Name,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
                Creator = new API.User
                {
                    EmailAddress = tab.Creator.EmailAddress,
                    JobTitle = tab.Creator.JobTitle,
                    DisplayName = tab.Creator.DisplayName,
                    GivenName = tab.Creator.GivenName,
                    LastName = tab.Creator.LastName
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
                string imageObjectPath = $"cases/{caseId}/shared/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                // Get the image path
                string imagePath = await GetSharedImage(imageObjectPath);

                // Update the image src to the image path
                img.Attributes["src"].Value = imagePath;
            }

        return new API.SharedTabExport
        {
            Name = tab.Name,
            Content = htmlDoc.DocumentNode.OuterHtml,
            Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
            Creator = new API.User
            {
                EmailAddress = tab.Creator.EmailAddress,
                JobTitle = tab.Creator.JobTitle,
                DisplayName = tab.Creator.DisplayName,
                GivenName = tab.Creator.GivenName,
                LastName = tab.Creator.LastName
            }
        };
    }

    private async Task<string> GetImage(string objectPath)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        ObjectStat? objectMetadata;
        // Fetch object metadata
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // If object does not exist return an HTTP 404 error
        catch (ObjectNotFoundException)
        {
            return "./Export/image-error.jpeg";
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = sCaseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null) return "./Export/image-error.jpeg";

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
        if (!BitConverter.ToString(md5Hash).Replace("-", "").Equals(objectHashes.Md5Hash, StringComparison.InvariantCultureIgnoreCase))
            return "./Export/image-error.jpeg";

        // Check generated SHA256 hash matches the hash in the database
        if (!BitConverter.ToString(sha256Hash).Replace("-", "").Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
            return "./Export/image-error.jpeg";

        // Fetch presigned url for object
        string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        return presignedS3Url;
    }

    private async Task<string> GetSharedImage(string objectPath)
    {
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        ObjectStat? objectMetadata;
        // Fetch object metadata
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // If object does not exist return an HTTP 404 error
        catch (ObjectNotFoundException)
        {
            return "./Export/image-error.jpeg";
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null) return "./Export/image-error.jpeg";

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
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
        if (!BitConverter.ToString(md5Hash).Replace("-", "").Equals(objectHashes.Md5Hash, StringComparison.InvariantCultureIgnoreCase))
            return "./Export/image-error.jpeg";

        // Check generated SHA256 hash matches the hash in the database
        if (!BitConverter.ToString(sha256Hash).Replace("-", "").Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
            return "./Export/image-error.jpeg";

        // Fetch presigned url for object
        string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        return presignedS3Url;
    }
}