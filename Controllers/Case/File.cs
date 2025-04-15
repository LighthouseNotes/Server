using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Server.Controllers.Case;

[Route("/case/{caseId}")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ImageController(DatabaseContext dbContext, SqidsEncoder<long> sqids, IConfiguration configuration) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET: /case/?/?/file/100.jpg
    // Will return a presigned url for a file
    [HttpGet("{type}/file/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<string>> GetFile(string caseId, string type, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned am HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return am HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create a variable for object path
        string objectPath;

        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/{emailAddress}/contemporaneous-notes/files/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/{emailAddress}/tabs/files/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return an HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

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
            return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null)
            return Problem($"Unable to find hash values for the file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "Could not find hash value for the file!");

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
            return Problem($"MD5 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "MD5 hash verification failed!");

        // Check generated SHA256 hash matches the hash in the database
        if (!BitConverter.ToString(sha256Hash).Replace("-", "").Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
            return Problem(
                $"SHA256 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "SHA256 hash verification failed!");

        // Fetch presigned url for object
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST: /case/?/?/file
    // Will upload the provided file to Minio S3 and return a presigned url
    [HttpPost("{type}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<string>> PostFile(string caseId, string type, IFormFile file)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.Hashes)
            .Include(cu => cu.Case)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        // Create variable for file name
        string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();

        // Create a variable for object path
        string objectPath;

        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/{emailAddress}/contemporaneous-notes/files/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/{emailAddress}/tabs/files/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return an HTTP 500 error
            if (objectHashes == null)
                return Problem(
                    "The file you are trying to upload matches the name of an existing file, however the hash of the existing file cannot be found!");

            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

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
                return Problem($"MD5 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                    title: "MD5 hash verification failed!");

            // Check generated SHA256 hash matches the hash in the database
            if (!BitConverter.ToString(sha256Hash).Replace("-", "")
                    .Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
                return Problem(
                    $"SHA256 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                    title: "SHA256 hash verification failed!");
        }
        // Catch object not found exception and upload the object
        catch (ObjectNotFoundException)
        {
            // Create memory stream to hold file contents
            MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Fetch object metadata
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            caseUser.Hashes.Add(new Database.Hash
            {
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await dbContext.SaveChangesAsync();

            // Log the creation of the file
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"{userNameJob} uploaded an file to a {type} for case `{caseUser.Case.DisplayName}` with name `{fileName}`.",
                    EmailAddress = emailAddress
                });
        }

        // Fetch presigned url for object
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // GET: /case/?/shared/?/file/100.jpg
    // Will return a presigned url for a file
    [HttpGet("shared/{type}/file/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<string>> GetSharedFile(string caseId, string type, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned am HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return am HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create a variable for object
        string objectPath;

        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/shared/contemporaneous-notes/files/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/shared/tabs/files/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return an HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

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
            return NotFound($"A object with the path `{objectPath}` can not be found in the S3 Bucket!");
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null)
            return Problem(
                $"Unable to find hash values for the shared file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "Could not find hash value for the shared file!");

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
            return Problem(
                $"MD5 hash verification failed for shared file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "MD5 hash verification failed!");

        // Check generated SHA256 hash matches the hash in the database
        if (!BitConverter.ToString(sha256Hash).Replace("-", "").Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
            return Problem(
                $"SHA256 hash verification failed for the shared file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                title: "SHA256 hash verification failed!");

        // Fetch presigned url for object
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST: /case/?/shared/?/file
    [HttpPost("shared/{type}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<string>> PostSharedFile(string caseId, string type, IFormFile file)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        // Create variable for file name
        string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();

        // Create a variable for object path
        string objectPath;

        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/shared/contemporaneous-notes/files/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/shared/tabs/files/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );

            // Fetch object hash from database
            Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

            // If object hash is null then a hash does not exist so return an HTTP 500 error
            if (objectHashes == null)
                return Problem(
                    "The file you are trying to upload matches the name of an existing file, however the hash of the existing file cannot be found!");

            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

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
                return Problem($"MD5 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                    title: "MD5 hash verification failed!");

            // Check generated SHA256 hash matches the hash in the database
            if (!BitConverter.ToString(sha256Hash).Replace("-", "")
                    .Equals(objectHashes.ShaHash, StringComparison.InvariantCultureIgnoreCase))
                return Problem(
                    $"SHA256 hash verification failed for file `{objectMetadata.ObjectName}` at `{objectPath}`!",
                    title: "SHA256 hash verification failed!");
        }
        // Catch object not found exception and upload the object
        catch (ObjectNotFoundException)
        {
            // Create memory stream to hold file contents
            MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Fetch object metadata
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.SharedHashes.Add(new Database.SharedHash
            {
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await dbContext.SaveChangesAsync();

            // Log the creation of the file
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` uploaded an file to a shared {type} for case `{sCase.DisplayName}` with name `{fileName}`.",
                    EmailAddress = emailAddress
                });
        }

        // Fetch presigned url for object
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user email from JWT claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return new PreflightResponse { Error = BadRequest("Email Address cannot be found in the JSON Web Token (JWT)!") };

        // Query user details
        PreflightResponseDetails? preflightData = await dbContext.User
            .Where(u => u.EmailAddress == emailAddress)
            .Select(u => new PreflightResponseDetails { EmailAddress = u.EmailAddress, UserNameJob = $"{u.DisplayName} ({u.JobTitle})" })
            .SingleOrDefaultAsync();
        // If query result is null then the user does not exist
        if (preflightData == null)
            return new PreflightResponse { Error = NotFound($"A user with the user email: `{emailAddress}` was not found!") };

        // Return preflight response
        return new PreflightResponse { Details = preflightData };
    }

    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public PreflightResponseDetails? Details { get; init; }
    }

    private class PreflightResponseDetails
    {
        public required string EmailAddress { get; init; }
        public required string UserNameJob { get; init; }
    }
}