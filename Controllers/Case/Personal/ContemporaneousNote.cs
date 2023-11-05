using System.Security.Cryptography;
using System.Text;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace LighthouseNotesServer.Controllers.Case.Personal;

[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ContemporaneousNotesController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public ContemporaneousNotesController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }


    // GET:  /case/5/contemporaneous-notes
    [HttpGet("/case/{caseId:guid}/contemporaneous-notes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetContemporaneousNotes(Guid caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(organization.Configuration.S3NetworkEncryption)
            .Build();

        // Create a variable for object path NOTE: removing auth0| from objectPath 
        string objectPath =
            $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/contemporaneous-notes.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Configuration.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return Problem(
                $"Can not find the S3 object for contemporaneous notes at the following path `{objectPath}`. Its likely the object does not exist because you have not created any notes!");
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = sCase.Hashes.SingleOrDefault(h =>
            h.User == user && h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for contemporaneous notes!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organization.Configuration.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return Problem("MD5 hash verification failed!");

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem("SHA256 hash verification failed!");

        // Return file
        return File(memoryStream.ToArray(), "application/octet-stream", "");
    }

    // POST: /case/5/contemporaneous-notes
    [HttpPost("/case/{caseId:guid}/contemporaneous-notes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> CreateOrAddContemporaneousNotes(Guid caseId,
        API.AddContemporaneousNotes contemporaneousNotesContent)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(organization.Configuration.S3NetworkEncryption)
            .Build();

        // Create a variable for object path NOTE: removing auth0| from objectPath 
        string objectPath =
            $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/contemporaneous-notes.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Configuration.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Check if object exists
        try
        {
            await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
            );

            // Create memory stream to store file contents
            MemoryStream existingDataStream = new();

            // Get object and copy file contents to stream
            await minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
                .WithCallbackStream(stream => { stream.CopyTo(existingDataStream); })
            );

            // Convert new data to bytes
            byte[] newData = Encoding.UTF8.GetBytes(contemporaneousNotesContent.Content);

            // Convert existing data stream to bytes
            byte[] existingData = existingDataStream.ToArray();

            // Add new data to existing data
            byte[] finalData = existingData.Concat(newData).ToArray();

            // Create a data stream to store the updated file contents
            MemoryStream finalDataStream = new();

            // Write the updated data
            finalDataStream.Write(finalData);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            finalDataStream.Position = 0;

            // Save the updated file to the s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
                .WithStreamData(finalDataStream)
                .WithObjectSize(finalDataStream.Length)
                .WithContentType("application/octet-stream")
            );


            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(finalDataStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(finalDataStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the addition of content
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` added `{contemporaneousNotesContent.Content}` to contemporaneous notes for `{sCase.DisplayName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        // Object does not exist so create it
        catch (ObjectNotFoundException)
        {
            // Create memory stream to store file contents
            MemoryStream memoryStream = new();
            byte[] data = Encoding.UTF8.GetBytes(contemporaneousNotesContent.Content);
            memoryStream.Write(data);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the creation
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` created contemporaneous notes for `{sCase.DisplayName}` with the content `{contemporaneousNotesContent.Content}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        catch (MinioException e)
        {
            return Problem(
                $"An unknown error occured while adding to or creating contemporaneous notes. For more information see the following error message: `{e.Message}`");
        }
    }

    // GET: /case/5/contemporaneous-notes/image/100.jpg
    [HttpGet("/case/{caseId:guid}/contemporaneous-notes/image/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> GetContemporaneousNotesImage(Guid caseId, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(organization.Configuration.S3NetworkEncryption)
            .Build();

        // Create a variable for object path NOTE: removing auth0| from objectPath 
        string objectPath = $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/images/{fileName}";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Configuration.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Fetch object metadata
        ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
            .WithBucket(organization.Configuration.S3BucketName)
            .WithObject(objectPath)
        );

        // Fetch object hash from database
        Database.Hash? objectHashes = sCase.Hashes.SingleOrDefault(h =>
            h.User == user && h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organization.Configuration.S3BucketName)
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return Problem("MD5 hash verification failed!");

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem("SHA256 hash verification failed!");

        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(organization.Configuration.S3BucketName)
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST:  /case/5/contemporaneous-notes/image
    [HttpPost("/case/{caseId:guid}/contemporaneous-notes/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> AddContemporaneousNotesImage(Guid caseId, IList<IFormFile> uploadFiles)
    {
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(organization.Configuration.S3NetworkEncryption)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Configuration.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Loop through each file
        foreach (IFormFile file in uploadFiles)
        {
            // Create variable for file name
            string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();

            // Create a variable for object path NOTE: removing auth0| from objectPath 
            string objectPath =
                $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/images/{fileName}";

            // Create memory stream to hold file contents 
            using MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Configuration.S3BucketName)
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the creation of the image
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` uploaded an image to contemporaneous notes for case `{sCase.DisplayName}` with name `{fileName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });
        }

        // Return Ok
        return Ok(new { count = uploadFiles.Count, size });
    }

    private async Task<PreflightResponse> PreflightChecks(Guid caseId)
    {
        // Get user id from claim
        string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        // If user id is null then it does not exist in JWT so return a HTTP 400 error
        if (userId == null)
            return new PreflightResponse
                { Error = BadRequest("User ID can not be found in the JSON Web Token (JWT)!") };

        // Get organization id from claim
        string? organizationId = User.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;

        // If organization id  is null then it does not exist in JWT so return a HTTP 400 error
        if (organizationId == null)
            return new PreflightResponse
                { Error = BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!") };

        // Fetch organization from the database by primary key
        Database.Organization? organization = await _dbContext.Organization.FindAsync(organizationId);

        // If organization is null then it does not exist so return a HTTP 404 error
        if (organization == null)
            return new PreflightResponse
                { Error = NotFound($"A organization with the ID `{organizationId}` can not be found!") };

        // If user does not exist in organization return a HTTP 403 error
        if (organization.Users.All(u => u.Id != userId))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"A user with the ID `{userId}` was not found in the organization with the ID `{organizationId}`!")
            };

        // If case does not exist in organization return a HTTP 404 error
        if (organization.Cases.All(c => c.Id != caseId))
            return new PreflightResponse { Error = NotFound($"A case with the ID `{caseId}` could not be found!") };

        // Fetch case from database and include case users and then include user details
        Database.Case sCase = organization.Cases.Single(c => c.Id == caseId);


        // If user does not have access to the requested case return a HTTP 403 error
        if (sCase.Users.All(u => u.User.Id != userId))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"The user with the ID `{userId}` does not have access to the case with the ID `{caseId}`!")
            };

        // Fetch user from database
        Database.User user = organization.Users.Single(u => u.Id == userId);

        // Return organization, user and case entity 
        return new PreflightResponse { Organization = organization, User = user, SCase = sCase };
    }


    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }
        public Database.Case? SCase { get; init; }
    }
}