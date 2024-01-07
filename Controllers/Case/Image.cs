using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case;

[Route("/case/{caseId:guid}")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ImageController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public ImageController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }
    
    // GET: /case/?/?/image/100.jpg
    [HttpGet("{type}/image/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> GetImage(Guid caseId, string type, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null || preflightResponse.CaseUser == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.CaseUser caseUser = preflightResponse.CaseUser;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);
        
        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath;
        
        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/images/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/tabs/images/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }
        
        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        // Fetch object metadata
        ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
            .WithObject(objectPath)
        );

        // Fetch object hash from database
        Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
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
            return Problem($"MD5 hash verification failed for: `{objectPath}`!");

        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem($"MD5 hash verification failed for: `{objectPath}`!");
        
        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST: /case/?/?/image
    [HttpPost("{type}/image/")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostImage(Guid caseId, string type, IList<IFormFile> uploadFiles)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null || preflightResponse.CaseUser == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;
        Database.CaseUser caseUser = preflightResponse.CaseUser;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);
        
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");
        
        // Response
        bool success = true;
        List<string> problemFileNames = new();
        // Loop through each file
        foreach (IFormFile file in uploadFiles)
        {
            // Create variable for file name
            string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();
            
            // Create a variable for object path with auth0| removed 
            string objectPath;
        
            // Change object path based on type
            switch (type)
            {
                case "contemporaneous-note":
                    objectPath = $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/images/{fileName}";
                    break;
                case "tab":
                    objectPath = $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/tabs/images/{fileName}";
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
                    .WithBucket(organization.Settings.S3BucketName)
                    .WithObject(objectPath)
                );
                
                // Fetch object hash from database
                Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
                    h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                // If object hash is null then a hash does not exist so return a HTTP 500 error
                if (objectHashes == null)
                    return Problem("The image you are trying to upload matches the name of an existing image, however the hash of the existing image cannot be found!");
                
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
        
                // Check generated MD5 and SHA256 hash matches the hash in the database
                if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash ||
                    BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                {
                    success = false;
                    problemFileNames.Add(fileName);
                }
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
                    .WithBucket(organization.Settings.S3BucketName)
                    .WithObject(objectPath)
                    .WithStreamData(memoryStream)
                    .WithObjectSize(memoryStream.Length)
                    .WithContentType("application/octet-stream")
                );
                
                // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                memoryStream.Position = 0;
                
                // Fetch object metadata
               objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                    .WithBucket(organization.Settings.S3BucketName)
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
                await _dbContext.SaveChangesAsync();

                // Log the creation of the image
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User `{user.DisplayName} ({user.JobTitle})` uploaded an image to a {type} for case `{sCase.DisplayName}` with name `{fileName}`.",
                        UserID = user.Id, OrganizationID = organization.Id
                    });
            }
            
        }

        if (success == false)
        {
            return Conflict(
                $"The following file names already exist; `{string.Join(",", problemFileNames)}`, please rename them and try again!");

        }

        // Return Ok
        return Ok(new { count = uploadFiles.Count, size });
    }
    
    // GET: /case/?/shared/?/image/100.jpg
    [HttpGet("shared/{type}/image/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> GetSharedImage(Guid caseId, string type, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null || preflightResponse.CaseUser == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;
        
        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);
        
        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath;
        
        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/shared/contemporaneous-notes/images/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/shared/tabs/images/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }
        
        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        // Fetch object metadata
        ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
            .WithObject(objectPath)
        );

        // Fetch object hash from database
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
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
            return Problem($"MD5 hash verification failed for: `{objectPath}`!");

        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem($"MD5 hash verification failed for: `{objectPath}`!");
        
        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(organization.Settings.S3BucketName)
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST: /case/?/shared/?/image
    [HttpPost("shared/{type}/image/")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostSharedImage(Guid caseId, string type, IList<IFormFile> uploadFiles)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null || preflightResponse.CaseUser == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);
        
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");
        
        // Response
        bool success = true;
        List<string> problemFileNames = new();
        // Loop through each file
        foreach (IFormFile file in uploadFiles)
        {
            // Create variable for file name
            string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();
            
            // Create a variable for object path with auth0| removed 
            string objectPath;
        
            // Change object path based on type
            switch (type)
            {
                case "contemporaneous-note":
                    objectPath = $"cases/{caseId}/shared/contemporaneous-notes/images/{fileName}";
                    break;
                case "tab":
                    objectPath = $"cases/{caseId}/shared/tabs/images/{fileName}";
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
                    .WithBucket(organization.Settings.S3BucketName)
                    .WithObject(objectPath)
                );
                
                // Fetch object hash from database
                Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
                    h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

                // If object hash is null then a hash does not exist so return a HTTP 500 error
                if (objectHashes == null)
                    return Problem("The image you are trying to upload matches the name of an existing image, however the hash of the existing image cannot be found!");
                
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
        
                // Check generated MD5 and SHA256 hash matches the hash in the database
                if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash ||
                    BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
                {
                    success = false;
                    problemFileNames.Add(fileName);
                }
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
                    .WithBucket(organization.Settings.S3BucketName)
                    .WithObject(objectPath)
                    .WithStreamData(memoryStream)
                    .WithObjectSize(memoryStream.Length)
                    .WithContentType("application/octet-stream")
                );
                
                // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                memoryStream.Position = 0;
                
                // Fetch object metadata
               objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                    .WithBucket(organization.Settings.S3BucketName)
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
                await _dbContext.SaveChangesAsync();

                // Log the creation of the image
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User `{user.DisplayName} ({user.JobTitle})` uploaded an image to a {type} for case `{sCase.DisplayName}` with name `{fileName}`.",
                        UserID = user.Id, OrganizationID = organization.Id
                    });
            }
            
        }

        if (success == false)
        {
            return Conflict(
                $"The following file names already exist; `{string.Join(",", problemFileNames)}`, please rename them and try again!");

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

        // If organization id is null then it does not exist in JWT so return a HTTP 400 error
        if (organizationId == null)
            return new PreflightResponse
                { Error = BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!") };

        // Get the user from the database by user ID and organization ID
        Database.User? user = await _dbContext.User.Where(u => u.Id == userId && u.Organization.Id == organizationId)
            .Include(user => user.Organization)
            .SingleOrDefaultAsync();

        // If user is null then they do not exist so return a HTTP 404 error
        if (user == null)
            return new PreflightResponse
            {
                Error = NotFound(
                    $"A user with the ID `{userId}` can not be found in the organization with the ID `{organizationId}`!")
            };

        // If case does not exist in organization return a HTTP 404 error
        Database.Case? sCase = await _dbContext.Case.SingleOrDefaultAsync(c => c.Id == caseId);
        if (sCase == null)
            return new PreflightResponse { Error = NotFound($"A case with the ID `{caseId}` could not be found!") };

        // Get the case user
        Database.CaseUser? caseUser = sCase.Users.SingleOrDefault(cu => cu.User == user);

        // If user does not have access to the requested case return a HTTP 403 error
        if (caseUser == null)
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"The user with the ID `{userId}` does not have access to the case with the ID `{caseId}`!")
            };

        // Return organization, user, case and case user entity 
        return new PreflightResponse
            { Organization = user.Organization, User = user, SCase = sCase, CaseUser = caseUser };
    }

    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }
        public Database.Case? SCase { get; init; }
        public Database.CaseUser? CaseUser { get; init; }
    }
}
