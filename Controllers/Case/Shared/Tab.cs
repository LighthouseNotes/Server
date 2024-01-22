using System.Security.Cryptography;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace Server.Controllers.Case.Shared;

[ApiController]
[Route("/case/{caseId}/shared")]
[AuditApi(EventTypeName = "HTTP")]
public class SharedTabsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public SharedTabsController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // GET: /case/?/shared/tabs
    [HttpGet("tabs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.SharedTab>>> GetTabs(string caseId)
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
        long userId = preflightResponse.Details.UserId;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return a list of the user's tabs
        return sCase.SharedTabs.Select(t => new API.SharedTab
        {
            Id = _sqids.Encode(t.Id),
            Name = t.Name,
            Created = TimeZoneInfo.ConvertTimeFromUtc(t.Created, timeZone),
            Creator = new API.User
            {
                Id = _sqids.Encode(t.Creator.Id),
                DisplayName = t.Creator.DisplayName,
                EmailAddress = t.Creator.EmailAddress,
                GivenName = t.Creator.GivenName,
                LastName = t.Creator.LastName,
                JobTitle = t.Creator.JobTitle,
                ProfilePicture = t.Creator.ProfilePicture,
                Roles = t.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization
                {
                    DisplayName = t.Creator.Organization.DisplayName,
                    Name = t.Creator.Organization.Name
                }
            }
        }).ToList();
    }

    // GET: /case/?/shared/tab/?
    [HttpGet("tab/{tabId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.SharedTab>> GetTab(string caseId, string tabId)
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
        long userId = preflightResponse.Details.UserId;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Fetch tab from the database
        Database.SharedTab? tab = await _dbContext.SharedTab
            .Where(st => st.Id == _sqids.Decode(tabId)[0] && st.Case == sCase)
            .Include(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .Include(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .SingleOrDefaultAsync();

        // If tab is null then return a HTTP 404 error as it does not exist
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return tab details
        return new API.SharedTab
        {
            Id = _sqids.Encode(tab.Id),
            Name = tab.Name,
            Created = TimeZoneInfo.ConvertTimeFromUtc(tab.Created, timeZone),
            Creator = new API.User
            {
                Id = _sqids.Encode(tab.Creator.Id),
                DisplayName = tab.Creator.DisplayName,
                EmailAddress = tab.Creator.EmailAddress,
                GivenName = tab.Creator.GivenName,
                LastName = tab.Creator.LastName,
                JobTitle = tab.Creator.JobTitle,
                ProfilePicture = tab.Creator.ProfilePicture,
                Roles = tab.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization
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
    public async Task<ActionResult<API.SharedTab>> CreateTab(string caseId, API.AddTab tabAddObject)
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
        long userId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedTabs)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Fetch the creator user from the database 
        Database.User user = await _dbContext.User
            .Include(user => user.Roles)
            .Include(user => user.Organization)
            .SingleAsync(u => u.Id == userId);

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

        // Log creation of the tab
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` created the shared tab `{tabModel.Name}` for `{sCase.DisplayName}`.",
                UserID = userId, OrganizationID = organizationId
            });

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return the newly created tab details
        return CreatedAtAction(nameof(GetTabs), new { caseId }, new API.SharedTab
        {
            Id = _sqids.Encode(tabModel.Id),
            Name = tabModel.Name,
            Created = TimeZoneInfo.ConvertTimeFromUtc(tabModel.Created, timeZone),
            Creator = new API.User
            {
                Id = _sqids.Encode(tabModel.Creator.Id),
                DisplayName = tabModel.Creator.DisplayName,
                EmailAddress = tabModel.Creator.EmailAddress,
                GivenName = tabModel.Creator.GivenName,
                LastName = tabModel.Creator.LastName,
                JobTitle = tabModel.Creator.JobTitle,
                ProfilePicture = tabModel.Creator.ProfilePicture,
                Roles = tabModel.Creator.Roles.Select(r => r.Name).ToList(),
                Organization = new API.Organization
                {
                    DisplayName = tabModel.Creator.Organization.DisplayName,
                    Name = tabModel.Creator.Organization.Name
                }
            }
        });
    }

    // GET: /case/?/shared/tab/?/content
    [HttpGet("tab/{tabId}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetTabContent(string caseId, string tabId)
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
        long userId = preflightResponse.Details.UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedTabs)
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Fetch the tab from the database
        Database.SharedTab? tab = sCase.SharedTabs.SingleOrDefault(t => t.Id == _sqids.Decode(tabId)[0]);

        // If tab is null then return HTTP 404 error as it does not exist
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Create a variable for object path with auth0| removed 
        string objectPath = $"cases/{caseId}/shared/tabs/{tabId}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object, if object does not exist catch exception
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

    // POST: /case/?/shared/tab/?/content
    [HttpPost("tab/{tabId}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostTabContent(string caseId, string tabId, [FromForm] IFormFile file)
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
        long userId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.SharedTabs)
            .Include(c => c.SharedHashes)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Fetch tab from the database
        Database.SharedTab? tab = sCase.SharedTabs.SingleOrDefault(t => t.Id == _sqids.Decode(tabId)[0]);

        // If tab is null then return a HTTP 404 error
        if (tab == null)
            return NotFound($"The shared tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Create a variable for filepath with auth0| removed
        string objectPath = $"cases/{caseId}/shared/tabs/{tabId}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return a HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{organizationSettings.S3BucketName}` does not exist!");

        // Check if object exists
        try
        {
            await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(organizationSettings.S3BucketName)
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

            // Log the addition of content
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` changed the content in the shared tab `{tab.Name}` for `{sCase.DisplayName}`.",
                    UserID = userId, OrganizationID = organizationId
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
                        $"`{userNameJob}` created content in the shared tab `{tab.Name}` for `{sCase.DisplayName}`.",
                    UserID = userId, OrganizationID = organizationId
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