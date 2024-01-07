using System.Security.Cryptography;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case.Shared;

[ApiController]
[Route("/case/{caseId:guid}/shared")]
[AuditApi(EventTypeName = "HTTP")]
public class SharedTabsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public SharedTabsController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /case/?/shared/tabs
    [HttpGet("tabs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.SharedTab>>> GetTabs(Guid caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, or case are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);

        // Return a list of the user's tabs
        return sCase.SharedTabs.Select(t => new API.SharedTab
        {
            Id = t.Id,
            Name = t.Name,
            Created = t.Created,
            Creator = new API.User()
            {
                Id = t.Creator.Id,
                DisplayName = t.Creator.DisplayName,
                EmailAddress = t.Creator.EmailAddress,
                GivenName = t.Creator.GivenName,
                LastName = t.Creator.LastName,
                JobTitle = t.Creator.JobTitle,
                ProfilePicture = t.Creator.ProfilePicture,
                Roles = t.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization()
                {
                    DisplayName = t.Creator.Organization.DisplayName,
                    Name = t.Creator.Organization.Name
                }
            }
        }).ToList();
    }

    // GET: /case/?/shared/tab/?
    [HttpGet("tab/{tabId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.SharedTab>> GetTab(Guid caseId, Guid tabId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, or case are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);

        // Fetch tab from the database
        Database.SharedTab? tab = sCase.SharedTabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return a HTTP 404 error as it does not exist
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Return tab details
        return new API.SharedTab
        {
            Id = tab.Id,
            Name = tab.Name,
            Created = tab.Created,
            Creator = new API.User()
            {
            Id = tab.Creator.Id,
            DisplayName = tab.Creator.DisplayName,
            EmailAddress = tab.Creator.EmailAddress,
            GivenName = tab.Creator.GivenName,
            LastName = tab.Creator.LastName,
            JobTitle = tab.Creator.JobTitle,
            ProfilePicture = tab.Creator.ProfilePicture,
            Roles = tab.Creator.Roles.Select(r => r.Name).ToList(),
            Organization = new API.Organization()
            {
                DisplayName = tab.Creator.Organization.DisplayName,
                Name = tab.Creator.Organization.Name
            }
        }
        };
    }

    // POST: /case/?/shared/tab
    [HttpPost("tab")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.SharedTab>> CreateTab(Guid caseId, API.AddTab tabAddObject)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, or case are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("UserID", user.Id);

        // Create tab model
        Database.SharedTab tabModel = new()
        {
            Name = tabAddObject.Name,
            Creator = user
        };

        // Add tab to database
        sCase.SharedTabs.Add(tabModel);

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Return the newly created tab details
        return CreatedAtAction(nameof(GetTabs), new { caseId }, new API.SharedTab
        {
            Id = tabModel.Id, 
            Name = tabModel.Name, 
            Created = tabModel.Created, 
            Creator = new API.User()
            {
                Id = tabModel.Creator.Id,
                DisplayName = tabModel.Creator.DisplayName,
                EmailAddress = tabModel.Creator.EmailAddress,
                GivenName = tabModel.Creator.GivenName,
                LastName = tabModel.Creator.LastName,
                JobTitle = tabModel.Creator.JobTitle,
                ProfilePicture = tabModel.Creator.ProfilePicture,
                Roles = tabModel.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization()
                {
                    DisplayName = tabModel.Creator.Organization.DisplayName,
                    Name = tabModel.Creator.Organization.Name
                }
            }
        });
    }

    // GET: /case/?/shared/tab/?/content
    [HttpGet("tab/{tabId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetTabContent(Guid caseId, Guid tabId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, or case are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
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

        // Fetch the tab from the database
        Database.SharedTab? tab = sCase.SharedTabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error as it does not exist
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath = $"cases/{sCase.Id}/shared/tabs/{tab.Id}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
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
                $"Can not find the S3 object for the shared tab with the ID `{tabId}` at the following path `{objectPath}`.");
        }

        // Fetch object hash from database
        Database.SharedHash? objectHashes = sCase.SharedHashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return a HTTP 500 error
        if (objectHashes == null)
            return Problem($"Unable to find hash values for the shared tab with the ID `{tabId}`!");

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

    // POST: /case/?/shared/tab/?/content
    [HttpPost("tab/{tabId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostTabContent(Guid caseId, Guid tabId, [FromForm] IFormFile file)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned a HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user, or case are null return a HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
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

        // Fetch tab from the database
        Database.SharedTab? tab = sCase.SharedTabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return a HTTP 404 error
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Settings.S3Endpoint)
            .WithCredentials(organization.Settings.S3AccessKey, organization.Settings.S3SecretKey)
            .WithSSL(organization.Settings.S3NetworkEncryption)
            .Build();

        // Create a variable for filepath with auth0| removed
        string objectPath = $"cases/{sCase.Id}/shared/tabs/{tab.Id}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organization.Settings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organization.Settings.S3BucketName}` does not exist!");

        // Check if object exists
        try
        {
            await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organization.Settings.S3BucketName)
                .WithObject(objectPath)
            );

            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save the updated file to the s3 bucket
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
            sCase.SharedHashes.Add(new Database.SharedHash
            {
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
                        $"User `{user.DisplayName} ({user.JobTitle})` changed the content in the shared tab `{tab.Name}` for `{sCase.DisplayName}`.",
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

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to S3 bucket
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
                        $"User `{user.DisplayName} ({user.JobTitle})` created content in the shared tab `{tab.Name}` for `{sCase.DisplayName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        catch (MinioException e)
        {
            return Problem(
                $"An unknown error occured while adding to or creating the shared tab. For more information see the following error message: `{e.Message}`");
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

        // If user does not have access to the requested case return a HTTP 403 error
        if (sCase.Users.All(cu => cu.User != user))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"The user with the ID `{userId}` does not have access to the case with the ID `{caseId}`!")
            };

        // Return organization, user, case and case user entity 
        return new PreflightResponse
            { Organization = user.Organization, User = user, SCase = sCase };
    }

    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }
        public Database.Case? SCase { get; init; }
    }
}