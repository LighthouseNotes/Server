using System.Security.Cryptography;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case.Shared;

[ApiController]
[Route("/case/{caseId}/shared")]
[AuditApi(EventTypeName = "HTTP")]
public class SharedContemporaneousNotesController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public SharedContemporaneousNotesController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // GET:  /case/?/shared/contemporaneous-notes
    [HttpGet("contemporaneous-notes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<List<API.SharedContemporaneousNotes>>> GetContemporaneousNotes(string caseId)
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
        long userId = preflightResponse.Details .UserId;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedContemporaneousNotes)
                .ThenInclude(scn => scn.Creator)
                    .ThenInclude(u => u.Roles)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(scn => scn.Creator)
                .ThenInclude(u => u.Organization)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);
        
        // Return a list of the user's contemporaneous notes
        return sCase.SharedContemporaneousNotes.Select(cn => new API.SharedContemporaneousNotes()
        {
            Id = _sqids.Encode(cn.Id),
            Created = TimeZoneInfo.ConvertTimeFromUtc(cn.Created, timeZone),
            Creator = new API.User()
            {
                Id = _sqids.Encode(cn.Creator.Id),
                DisplayName = cn.Creator.DisplayName,
                EmailAddress = cn.Creator.EmailAddress,
                GivenName = cn.Creator.GivenName,
                LastName = cn.Creator.LastName,
                JobTitle = cn.Creator.JobTitle,
                ProfilePicture = cn.Creator.ProfilePicture,
                Roles = cn.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization()
                {
                    DisplayName = cn.Creator.Organization.DisplayName,
                    Name = cn.Creator.Organization.Name
                }
            }
        }).ToList();
    }

    // GET: /case/?/shared/contemporaneous-note/?
    [HttpGet("contemporaneous-note/{noteId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetContemporaneousNote(string caseId, string noteId)
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
            .Include(c => c.SharedContemporaneousNotes)
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null)
        {
            return NotFound($"The case `{caseId}` does not exist!"); 
            // The case might not exist or the user does not have access to the case
        }
        
        // Convert Note ID squid to ID
        long rawNoteId = _sqids.Decode(noteId)[0];

        // Fetch the contemporaneous note from the database
        Database.SharedContemporaneousNote?  contemporaneousNote =
            sCase.SharedContemporaneousNotes.SingleOrDefault(cn => cn.Id == rawNoteId);

        // If contemporaneous note is null then return a HTTP 404 error as it does not exist
        if (contemporaneousNote == null) return NotFound($"Can not find the note with the ID `{noteId}`");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{caseId}/shared/contemporaneous-notes/{noteId}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP a 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for contemporaneous notes!");

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
        
        // Return file
        return File(memoryStream.ToArray(), "application/octet-stream", "");
    }

    // POST: /case/?/shared/contemporaneous-notes
    [HttpPost("contemporaneous-note")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostContemporaneousNote(string caseId, [FromForm] IFormFile file)
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
            .Include(c => c.SharedContemporaneousNotes)
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

        // Fetch the creator user from the database 
        Database.User user =  await _dbContext.User.SingleAsync(u=> u.Id == userId);
        
        // Create contemporaneous note record in the database
        Database.SharedContemporaneousNote contemporaneousNote = new()
        {
            Creator = user
        };
        
        // Add the note to the collection
        sCase.SharedContemporaneousNotes.Add(contemporaneousNote);

        // Save changes to the database so a GUID is generated
        await _dbContext.SaveChangesAsync();

        // Create a variable for object path with auth0| removed 
        string objectPath =
            $"cases/{caseId}/shared/contemporaneous-notes/{_sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        try
        {
            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

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
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
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

            // Log the creation
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` created a shared contemporaneous note for `{sCase.DisplayName}` with the ID `{_sqids.Encode(contemporaneousNote.Id)}`.",
                    UserID = userId, OrganizationID = organizationId
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
        public required Database.UserSettings UserSettings { get; init; }
    }
}