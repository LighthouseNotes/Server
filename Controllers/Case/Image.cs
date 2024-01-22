using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case;

[Route("/case/{caseId}")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ImageController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public ImageController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }
    
    // GET: /case/?/?/image/100.jpg
    [HttpGet("{type}/image/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> GetImage(string caseId, string type, string fileName)
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
        long rawUserId = preflightResponse.Details .UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", rawUserId);

        // Get case user from the database including the required entities 
        Database.CaseUser? caseUser = await _dbContext.CaseUser
            .Where(cu => cu.Id == _sqids.Decode(caseId)[0] && cu.User.Id == rawUserId)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return a HTTP 404 error 
        if (caseUser == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }
        
        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Convert user ID to squid 
        string userId = _sqids.Encode(rawUserId);
        
        // Create a variable for object path with auth0| removed 
        string objectPath;
        
        // Change object path based on type
        switch (type)
        {
            case "contemporaneous-note":
                objectPath = $"cases/{caseId}/{userId }/contemporaneous-notes/images/{fileName}";
                break;
            case "tab":
                objectPath = $"cases/{caseId}/{userId}/tabs/images/{fileName}";
                break;
            default:
                return BadRequest("Invalid type, must be `contemporaneous-note` or `tab`!");
        }
        
        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;
        
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
        Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
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
            .WithBucket(organizationSettings.S3BucketName)
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
    public async Task<ActionResult> PostImage(string caseId, string type, IList<IFormFile> uploadFiles)
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
        long rawUserId = preflightResponse.Details .UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", rawUserId);

        // Get case user from the database including the required entities 
        Database.CaseUser? caseUser = await _dbContext.CaseUser
            .Where(cu => cu.Id == _sqids.Decode(caseId)[0] && cu.User.Id == rawUserId)
            .Include(cu => cu.Hashes)
            .Include(cu => cu.Case)
            .SingleOrDefaultAsync();

        // If case user does not exist then return a HTTP 404 error 
        if (caseUser == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }
        
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

        
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
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");
        
        // Convert user ID to squid 
        string userId = _sqids.Encode(rawUserId);
        
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
                    objectPath = $"cases/{caseId}/{userId}/contemporaneous-notes/images/{fileName}";
                    break;
                case "tab":
                    objectPath = $"cases/{caseId}/{userId}/tabs/images/{fileName}";
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
                    .WithBucket(organizationSettings.S3BucketName)
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
                    .WithBucket(organizationSettings.S3BucketName)
                    .WithObject(objectPath)
                    .WithStreamData(memoryStream)
                    .WithObjectSize(memoryStream.Length)
                    .WithContentType("application/octet-stream")
                );
                
                // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                memoryStream.Position = 0;
                
                // Fetch object metadata
               objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                    .WithBucket(organizationSettings.S3BucketName)
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
                            $"{userNameJob} uploaded an image to a {type} for case `{caseUser.Case.DisplayName}` with name `{fileName}`.",
                        UserID = rawUserId, OrganizationID = organizationId
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
    public async Task<ActionResult<string>> GetSharedImage(string caseId, string type, string fileName)
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
        long userId = preflightResponse.Details .UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);
        
        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }
        
        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
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
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;
        
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
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
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
            .WithBucket(organizationSettings.S3BucketName)
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
    public async Task<ActionResult> PostSharedImage(string caseId, string type, IList<IFormFile> uploadFiles)
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
        long userId = preflightResponse.Details .UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);
        
        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }
        
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

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
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");
        
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
                    .WithBucket(organizationSettings.S3BucketName)
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
                    .WithBucket(organizationSettings.S3BucketName)
                    .WithObject(objectPath)
                    .WithStreamData(memoryStream)
                    .WithObjectSize(memoryStream.Length)
                    .WithContentType("application/octet-stream")
                );
                
                // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
                memoryStream.Position = 0;
                
                // Fetch object metadata
               objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                    .WithBucket(organizationSettings.S3BucketName)
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
                            $"`{userNameJob}` uploaded an image to a shared {type} for case `{sCase.DisplayName}` with name `{fileName}`.",
                        UserID = userId, OrganizationID = organizationId
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
            .Select(u => new PreflightResponseDetails()
            {
                OrganizationId = u.Organization.Id,
                OrganizationSettings = u.Organization.Settings,
                UserId = u.Id,
                UserNameJob = $"{u.DisplayName} ({u.JobTitle})"
            }).SingleOrDefaultAsync();

        // If query result is null then the user does not exit in the organization so return a HTTP 404 error
        if (userQueryResult == null)
            return new PreflightResponse
            {
                Error = NotFound(
                    $"A user with the Auth0 user ID `{auth0UserId}` was not found in the organization with the Auth0 organization ID `{organizationId}`!")
            };

        return new PreflightResponse()
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
    }

  
}
