using System.Security.Cryptography;
using System.Text;
using System.Web;
using BlazorTemplater;
using HtmlAgilityPack;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;
using Syncfusion.HtmlConverter;
using Syncfusion.Pdf;
// ReSharper disable AccessToModifiedClosure

namespace Server.Controllers.Case;

[ApiController]
[Route("/case/{caseId}/export")]
[AuditApi(EventTypeName = "HTTP")]
public class ExportController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly ILoggerFactory _logger;
    private readonly SqidsEncoder<long> _sqids;

    public ExportController(DatabaseContext dbContext, ILoggerFactory logger, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _logger = logger;
        _sqids = sqids;
    }

    // GET:  /case/5/export
    [HttpGet]
    [AuditIgnore]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> ExportContemporaneousNotes(string caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return a HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        string organizationId = preflightResponse.Details.OrganizationId;
        Database.OrganizationSettings organizationSettings = preflightResponse.Details.OrganizationSettings;
        long rawUserId = preflightResponse.Details.UserId;
        string userId = _sqids.Encode(rawUserId);
        string userNameJob = preflightResponse.Details.UserNameJob;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        // IAuditScope auditScope = this.GetCurrentAuditScope();
        // auditScope.SetCustomField("OrganizationID", organizationId);
        // auditScope.SetCustomField("UserID", userId);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == rawUserId))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Organization)
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Roles)
            .Include(c => c.SharedHashes)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Get case user from the database including the required entities 
        Database.CaseUser? caseUser = await _dbContext.CaseUser
            .Where(cu => cu.Case.Id == _sqids.Decode(caseId)[0] && cu.User.Id == rawUserId)
            .Include(cu => cu.ContemporaneousNotes)
            .Include(cu => cu.Tabs)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return a HTTP 404 error 
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        //////////////////
        // Case details //
        //////////////////
        Database.User sioUser = sCase.Users.First(u => u.IsSIO).User;
        API.Export model = new()
        {
            DisplayName = sCase.DisplayName,
            SIO = new API.User
            {
                Id = _sqids.Encode(sioUser.Id),  Auth0Id = sioUser.Auth0Id, 
                JobTitle = sioUser.JobTitle, DisplayName = sioUser.DisplayName,
                GivenName = sioUser.GivenName, LastName = sioUser.LastName, EmailAddress = sioUser.EmailAddress,
                ProfilePicture = sioUser.ProfilePicture,
                Organization = new API.Organization
                    { Name = sioUser.Organization.DisplayName, DisplayName = sioUser.Organization.DisplayName },
                Roles = sioUser.Roles.Select(r => r.Name).ToList()
            },
            Modified = sCase.Modified,
            Created = sCase.Created,
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };

        /////////////////////////
        // Personal Comp Notes //
        /////////////////////////
        foreach (Database.ContemporaneousNote contemporaneousNote in caseUser.ContemporaneousNotes)
        {
            // Variables 
            MemoryStream memoryStream = new();
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Create a variable for object path with auth0| removed 
            string objectPath = $"cases/{caseId}/{userId}/contemporaneous-notes/{_sqids.Encode(contemporaneousNote.Id)}/note.txt";

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return HTTP 500 error
            if (objectHashes == null)
                return Problem("Unable to find hash values for the requested image!");

            // Get object and copy file contents to stream
            await minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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
                return Problem("MD5 hash verification failed!");

            // Check SHA256 hash generated matches hash in the database
            if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                return Problem("SHA256 hash verification failed!");

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
                    objectPath =
                        $"cases/{caseId}/{userId}/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                    // Fetch object metadata
                    try
                    {
                        objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                            .WithBucket(organizationSettings.S3BucketName)
                            .WithObject(objectPath)
                        );
                    }
                    // If object does not exist return a HTTP 404 error
                    catch (ObjectNotFoundException)
                    {
                        return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
                    }

                    // Fetch object hash from database
                    objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                        h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                    // If object hash is null then a hash does not exist so return a HTTP 500 error
                    if (objectHashes == null)
                        return Problem("Unable to find hash values for the requested image!");

                    // Create memory stream to store file contents
                    memoryStream = new MemoryStream();

                    // Get object and copy file contents to stream
                    await minio.GetObjectAsync(new GetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
                    );

                    // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                    memoryStream.Position = 0;

                    // Create MD5 and SHA256
                    // Generate MD5 and SHA256 hash
                    md5Hash = await md5.ComputeHashAsync(memoryStream);
                    sha256Hash = await sha256.ComputeHashAsync(memoryStream);

                    // Check generated MD5 hash matches the hash in the database
                    if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Check generated SHA256 hash matches the hash in the database
                    if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Fetch presigned url for object  
                    string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithExpiry(3600)
                    );

                    img.Attributes["src"].Value = presignedS3Url;
                }

            // Add contemporaneous note to model 
            model.ContemporaneousNotes.Add(new API.ContemporaneousNotesExport
            {
                Content = htmlDoc.DocumentNode.OuterHtml,
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone)
            });
        }

        ///////////
        // Tabs //
        //////////
        // Loop through each tab and add to model
        foreach (Database.Tab tab in caseUser.Tabs)
        {
            // Variables 
            MemoryStream memoryStream = new();
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Create variable for object path with auth0| removed
            string objectPath = $"cases/{caseId}/{userId}/tabs/{_sqids.Encode(tab.Id)}/content.txt";

            // Get object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return HTTP 500 error
            if (objectHashes == null)
                return Problem($"Unable to find hash values for the tab with the ID `{tab.Id}`!");

            // Get object and copy file contents to stream
            await minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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
                return Problem("MD5 hash verification failed!");

            // Check SHA256 hash generated matches hash in the database
            if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                return Problem("SHA256 hash verification failed!");

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
                    objectPath = $"cases/{caseId}/{userId}/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                    // Fetch object metadata
                    try
                    {
                        objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                            .WithBucket(organizationSettings.S3BucketName)
                            .WithObject(objectPath)
                        );
                    }
                    // If object does not exist return a HTTP 404 error
                    catch (ObjectNotFoundException)
                    {
                        return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
                    }

                    // Fetch object hash from database
                    objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                        h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                    // If object hash is null then a hash does not exist so return a HTTP 500 error
                    if (objectHashes == null)
                        return Problem("Unable to find hash values for the requested image!");

                    // Create memory stream to store file contents
                    memoryStream = new MemoryStream();

                    // Get object and copy file contents to stream
                    await minio.GetObjectAsync(new GetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
                    );

                    // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                    memoryStream.Position = 0;

                    // Create MD5 and SHA256
                    // Generate MD5 and SHA256 hash
                    md5Hash = await md5.ComputeHashAsync(memoryStream);
                    sha256Hash = await sha256.ComputeHashAsync(memoryStream);

                    // Check generated MD5 hash matches the hash in the database
                    if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Check generated SHA256 hash matches the hash in the database
                    if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Fetch presigned url for object  
                    string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithExpiry(3600)
                    );

                    img.Attributes["src"].Value = presignedS3Url;
                }

            // Add tab to model
            model.Tabs.Add(new API.TabExport
            {
                Name = tab.Name,
                Content = htmlDoc.DocumentNode.OuterHtml
            });
        }

        ////////////////////////
        // Shared comp notes //
        //////////////////////
        foreach (Database.SharedContemporaneousNote contemporaneousNote in sCase.SharedContemporaneousNotes)
        {
            // Variables 
            MemoryStream memoryStream = new();
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Create a variable for object path with auth0| removed 
            string objectPath = $"cases/{caseId}/shared/contemporaneous-notes/{_sqids.Encode(contemporaneousNote.Id)}/note.txt";

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return HTTP 500 error
            if (objectHashes == null)
                return Problem("Unable to find hash values for the requested image!");

            // Get object and copy file contents to stream
            await minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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
                return Problem("MD5 hash verification failed!");

            // Check SHA256 hash generated matches hash in the database
            if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                return Problem("SHA256 hash verification failed!");

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
                    objectPath =
                        $"cases/{caseId}/shared/contemporaneous-notes/images/{HttpUtility.UrlDecode(fileName)}";

                    // Fetch object metadata
                    try
                    {
                        objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                            .WithBucket(organizationSettings.S3BucketName)
                            .WithObject(objectPath)
                        );
                    }
                    // If object does not exist return a HTTP 404 error
                    catch (ObjectNotFoundException)
                    {
                        return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
                    }

                    // Fetch object hash from database
                    objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                        h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                    // If object hash is null then a hash does not exist so return a HTTP 500 error
                    if (objectHashes == null)
                        return Problem("Unable to find hash values for the requested image!");

                    // Create memory stream to store file contents
                    memoryStream = new MemoryStream();

                    // Get object and copy file contents to stream
                    await minio.GetObjectAsync(new GetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
                    );

                    // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                    memoryStream.Position = 0;

                    // Create MD5 and SHA256
                    // Generate MD5 and SHA256 hash
                    md5Hash = await md5.ComputeHashAsync(memoryStream);
                    sha256Hash = await sha256.ComputeHashAsync(memoryStream);

                    // Check generated MD5 hash matches the hash in the database
                    if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Check generated SHA256 hash matches the hash in the database
                    if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Fetch presigned url for object  
                    string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithExpiry(3600)
                    );

                    img.Attributes["src"].Value = presignedS3Url;
                }

            // Add contemporaneous note to model 
            model.SharedContemporaneousNotes.Add(new API.SharedContemporaneousNotesExport
            {
                Content = htmlDoc.DocumentNode.OuterHtml,
                Created = TimeZoneInfo.ConvertTimeFromUtc(contemporaneousNote.Created, timeZone),
                Creator = new API.User
                {
                    Id = _sqids.Encode(contemporaneousNote.Creator.Id),  Auth0Id = contemporaneousNote.Creator.Auth0Id,
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
            });
        }

        //////////////////
        // Shared Tabs //
        ////////////////
        // Loop through each tab and add to model
        foreach (Database.SharedTab tab in sCase.SharedTabs)
        {
            // Variables 
            MemoryStream memoryStream = new();
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Create variable for object path with auth0| removed
            string objectPath = $"cases/{caseId}/shared/tabs/{_sqids.Encode(tab.Id)}/content.txt";

            // Get object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return HTTP 500 error
            if (objectHashes == null)
                return Problem($"Unable to find hash values for the tab with the ID `{tab.Id}`!");

            // Get object and copy file contents to stream
            await minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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
                return Problem("MD5 hash verification failed!");

            // Check SHA256 hash generated matches hash in the database
            if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                return Problem("SHA256 hash verification failed!");

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
                    objectPath = $"cases/{caseId}/shared/tabs/images/{HttpUtility.UrlDecode(fileName)}";

                    // Fetch object metadata
                    try
                    {
                        objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                            .WithBucket(organizationSettings.S3BucketName)
                            .WithObject(objectPath)
                        );
                    }
                    // If object does not exist return a HTTP 404 error
                    catch (ObjectNotFoundException)
                    {
                        return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
                    }

                    // Fetch object hash from database
                    objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                        h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                    // If object hash is null then a hash does not exist so return a HTTP 500 error
                    if (objectHashes == null)
                        return Problem("Unable to find hash values for the requested image!");

                    // Create memory stream to store file contents
                    memoryStream = new MemoryStream();

                    // Get object and copy file contents to stream
                    await minio.GetObjectAsync(new GetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
                    );

                    // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                    memoryStream.Position = 0;

                    // Create MD5 and SHA256
                    // Generate MD5 and SHA256 hash
                    md5Hash = await md5.ComputeHashAsync(memoryStream);
                    sha256Hash = await sha256.ComputeHashAsync(memoryStream);

                    // Check generated MD5 hash matches the hash in the database
                    if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Check generated SHA256 hash matches the hash in the database
                    if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                        return Problem($"MD5 hash verification failed for: `{objectPath}`!");

                    // Fetch presigned url for object  
                    string presignedS3Url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
                        .WithBucket(organizationSettings.S3BucketName)
                        .WithObject(objectPath)
                        .WithExpiry(3600)
                    );

                    img.Attributes["src"].Value = presignedS3Url;
                }

            // Add tab to model
            model.SharedTabs.Add(new API.SharedTabExport
            {
                Name = tab.Name,
                Content = htmlDoc.DocumentNode.OuterHtml,
                Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
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
            });
        }


        /////////////////////////////
        // HTML to PDF conversion //
        ///////////////////////////

        // Convert blazor page to HTML
        string html = new ComponentRenderer<ExportTemplate>()
            .Set(c => c.Model, model)
            .AddService(_logger)
            .Render();

        // Initialize HTML to PDF converter
        HtmlToPdfConverter htmlConverter = new();

        const string baseUrl = "C:/Temp/HTMLFiles/";

        //Initialize Blink Converter Settings
        BlinkConverterSettings blinkConverterSettings = new();

        // Read MudBlazor css file and set custom CSS
        using StreamReader streamReader = new(@"C:\Users\ben\source\repos\LighthouseNotes\Server\MudBlazor.min.css",
            Encoding.UTF8);
        string mudBlazorCSS = await streamReader.ReadToEndAsync();
        blinkConverterSettings.Css = mudBlazorCSS;

        // Set HTML converter settings
        htmlConverter.ConverterSettings = blinkConverterSettings;

        // Convert URL to PDF
        PdfDocument document = htmlConverter.Convert(html, baseUrl);

        // Create memory stream to store the PDF
        MemoryStream pdfMemoryStream = new();

        // Save and close the PDF document.
        document.Save(pdfMemoryStream);
        document.Close(true);
        pdfMemoryStream.Flush();
        pdfMemoryStream.Position = 0;

        // Create a variable for object path with auth0| removed 
        string pdfObjectPath = $"cases/{caseId}/{userId}/export/Lighthouse Notes Export {sCase.DisplayName}.pdf";

        // Log PDF export creation
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` created a PDF export for the case `{caseId}`.",
                UserID = rawUserId, OrganizationID = organizationId
            });

        // Save file to s3 bucket
        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
            .WithObject(pdfObjectPath)
            .WithStreamData(pdfMemoryStream)
            .WithObjectSize(pdfMemoryStream.Length)
            .WithContentType("application/octet-stream")
        );

        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
            .WithObject(pdfObjectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user ID from claim
        string? auth0UserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        // If user ID is null then it does not exist in JWT so return a HTTP 400 error
        if (auth0UserId == null)
            return new PreflightResponse
                { Error = BadRequest("User ID can not be found in the JSON Web Token (JWT)!") };

        // Get organization ID from claim
        string? organizationId = User.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;

        // If organization ID  is null then it does not exist in JWT so return a HTTP 400 error
        if (organizationId == null)
            return new PreflightResponse
                { Error = BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!") };

        // Select organization ID, organization settings, user ID and user name and job and settings from the user table
        PreflightResponseDetails? userQueryResult = await _dbContext.User
            .Where(u => u.Auth0Id == auth0UserId && u.Organization.Id == organizationId)
            .Select(u => new PreflightResponseDetails
            {
                OrganizationId = u.Organization.Id,
                OrganizationSettings = u.Organization.Settings,
                UserId = u.Id,
                UserNameJob = $"{u.DisplayName} ({u.JobTitle})",
                UserSettings = u.Settings
            }).SingleOrDefaultAsync();

        // If query result is null then the user does not exit in the organization so return a HTTP 404 error
        if (userQueryResult == null)
            return new PreflightResponse
            {
                Error = NotFound(
                    $"A user with the Auth0 user ID `{auth0UserId}` was not found in the organization with the Auth0 organization ID `{organizationId}`!")
            };

        return new PreflightResponse
        {
            Details = userQueryResult
        };
    }

    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public PreflightResponseDetails? Details { get; init; }
    }

    private class PreflightResponseDetails
    {
        public required string OrganizationId { get; init; }
        public required Database.OrganizationSettings OrganizationSettings { get; init; }
        public long UserId { get; init; }
        public required string UserNameJob { get; init; }
        public required Database.UserSettings UserSettings { get; init; }
    }
}