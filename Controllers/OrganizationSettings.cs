namespace Server.Controllers;

[Route("/organization/settings")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class OrganizationSettingsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public OrganizationSettingsController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /organization/config
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<ActionResult<API.OrganizationSettings>> GetSettings()
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

        // Return the organization's settings
        return new API.OrganizationSettings
        {
            S3Endpoint = organizationSettings.S3Endpoint,
            S3AccessKey = organizationSettings.S3AccessKey,
            S3BucketName = organizationSettings.S3BucketName,
            S3NetworkEncryption = organizationSettings.S3NetworkEncryption,
            S3SecretKey = organizationSettings.S3SecretKey,
            MeilisearchUrl = organizationSettings.MeilisearchUrl,
            MeilisearchApiKey = organizationSettings.MeilisearchApiKey
        };
    }

    // PUT: /organization/settings
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<IActionResult> UpdateSettings(API.OrganizationSettings updatedOrganizationSettings)
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

        // If s3 endpoint is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.S3Endpoint) &&
            updatedOrganizationSettings.S3Endpoint != organizationSettings.S3Endpoint)
        {
            // Log s3 endpoint change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `S3 Endpoint` setting from {organizationSettings.S3Endpoint}` to `{updatedOrganizationSettings.S3Endpoint}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.S3Endpoint = updatedOrganizationSettings.S3Endpoint;
        }

        // If s3 network encryption is not null and does not equal the value in the database update it 
        if (updatedOrganizationSettings.S3NetworkEncryption != organizationSettings.S3NetworkEncryption)
        {
            // Log s3 network encryption change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `S3 Network Encryption` setting from {organizationSettings.S3NetworkEncryption}` to `{updatedOrganizationSettings.S3NetworkEncryption}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.S3NetworkEncryption = updatedOrganizationSettings.S3NetworkEncryption;
        }

        // If s3 bucket name is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.S3BucketName) &&
            updatedOrganizationSettings.S3BucketName != organizationSettings.S3BucketName)
        {
            // Log s3 bucket name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `S3 Bucket Name` setting from {organizationSettings.S3BucketName}` to `{updatedOrganizationSettings.S3BucketName}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.S3BucketName = updatedOrganizationSettings.S3BucketName;
        }

        // If s3 access key is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.S3AccessKey) &&
            updatedOrganizationSettings.S3AccessKey != organizationSettings.S3AccessKey)
        {
            // Log s3 access key change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `S3 Access Key` setting from {organizationSettings.S3AccessKey}` to `{updatedOrganizationSettings.S3AccessKey}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.S3AccessKey = updatedOrganizationSettings.S3AccessKey;
        }

        // If s3 secret key is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.S3SecretKey) &&
            updatedOrganizationSettings.S3SecretKey != organizationSettings.S3SecretKey)
        {
            // Log s3 secret key change however do not log the change value
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `S3 Secret Key` setting.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.S3SecretKey = updatedOrganizationSettings.S3SecretKey;
        }

        // If Meilisearch Url is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.MeilisearchUrl) &&
            updatedOrganizationSettings.MeilisearchUrl != organizationSettings.MeilisearchUrl)
        {
            // Log s3 secret key change however do not log the change value
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `Meilisearch URL` setting from {organizationSettings.MeilisearchUrl}` to `{updatedOrganizationSettings.MeilisearchUrl}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.MeilisearchUrl = updatedOrganizationSettings.MeilisearchUrl;
        }

        // If Meilisearch Api key is not null and does not equal the value in the database update it 
        if (!string.IsNullOrWhiteSpace(updatedOrganizationSettings.MeilisearchApiKey) &&
            updatedOrganizationSettings.MeilisearchApiKey != organizationSettings.MeilisearchApiKey)
        {
            // Log s3 secret key change however do not log the change value
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` updated the organizations `Meilisearch API Key` setting.",
                    UserID = userId, OrganizationID = organizationId
                });

            organizationSettings.MeilisearchApiKey = updatedOrganizationSettings.MeilisearchApiKey;
        }

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Return HTTP 204 No Content
        return NoContent();
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
                UserNameJob = $"{u.DisplayName} ({u.JobTitle})"
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
    }
}