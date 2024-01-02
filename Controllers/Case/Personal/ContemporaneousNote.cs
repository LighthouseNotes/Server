using System.Security.Cryptography;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case.Personal;

[ApiController]
[Route("/case/{caseId:guid}")]
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

    // GET:  /case/?/contemporaneous-notes
    [HttpGet("contemporaneous-notes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<List<API.ContemporaneousNotes>>> GetContemporaneousNotes(Guid caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null || preflightResponse.CaseUser == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.User user = preflightResponse.User;
        Database.CaseUser caseUser = preflightResponse.CaseUser;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);

        // Return a list of the user's contemporaneous notes
        return caseUser.ContemporaneousNotes.Select(cn => new API.ContemporaneousNotes()
        {
            Id = cn.Id,
            Created = cn.Created
        }).ToList();
    }

    // GET: /case/?/contemporaneous-note/?
    [HttpGet("contemporaneous-note/{noteId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetContemporaneousNote(Guid caseId, Guid noteId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return a HTTP 500 error
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

        // Fetch the contemporaneous note from the database
        Database.ContemporaneousNote? contemporaneousNote =
            caseUser.ContemporaneousNotes.SingleOrDefault(cn => cn.Id == noteId);

        // If contemporaneous note is null then return a HTTP 404 error as it does not exist
        if (contemporaneousNote == null) return NotFound($"Can not find the note with the ID `{noteId}`");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/{noteId}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP a 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Settings.S3BucketName)
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return Problem(
                $"Can not find the S3 object for contemporaneous note: `{noteId}` at the following path: `{objectPath}`.");
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for contemporaneous notes!");

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
        
        // Return file
        return File(memoryStream.ToArray(), "application/octet-stream", "");
    }

    // POST: /case/?/contemporaneous-notes
    [HttpPost("contemporaneous-note")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostContemporaneousNote(Guid caseId, [FromForm] IFormFile file)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, case, or case user are null return a HTTP 500 error
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

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create contemporaneous note record in the database
        Database.ContemporaneousNote contemporaneousNote = new();
        caseUser.ContemporaneousNotes.Add(contemporaneousNote);

        // Save changes to the database so a GUID is generated
        await _dbContext.SaveChangesAsync();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{caseId}/{user.Id.Replace("auth0|", "")}/contemporaneous-notes/{contemporaneousNote.Id}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        try
        {
            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

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
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
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

            // Log the creation
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` created contemporaneous note for `{sCase.DisplayName}` with the ID `{contemporaneousNote.Id}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        catch (MinioException e)
        {
            return Problem(
                $"An unknown error occured while adding a contemporaneous note. For more information see the following error message: `{e.Message}`");
        }
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