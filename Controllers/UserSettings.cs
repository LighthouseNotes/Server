namespace Server.Controllers;

[Route("/user/settings")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UsersSettingsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public UsersSettingsController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // GET: /user/settings
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Settings>> GetUserSettings()
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
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Set scheme to HTTPS if network encryption is enabled else use HTTP
        string scheme = organizationSettings.S3NetworkEncryption ? "https" : "http";

        // Return the settings
        return new API.Settings
        {
            UserId = _sqids.Encode(userId),
            TimeZone = userSettings.TimeZone,
            DateFormat = userSettings.DateFormat,
            TimeFormat = userSettings.TimeFormat,
            Locale = userSettings.Locale,
            S3Endpoint = $"{scheme}://{organizationSettings.S3Endpoint}/{organizationSettings.S3BucketName}"
        };
    }

    // PUT: /user/settings
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<IActionResult> UpdateUserSettings(API.UserSettings userSettingsObject)
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

        // If the provided time zone does not match the user's time zone the update it
        if (userSettings.TimeZone != userSettingsObject.TimeZone)
        {
            // Log user time zone change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User updated their `time zone` setting from `{userSettings.TimeZone}` to `{userSettingsObject.TimeZone}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            userSettings.TimeZone = userSettingsObject.TimeZone;
        }

        // If the provided date format does not match the user's date format the update it
        if (userSettings.DateFormat != userSettingsObject.DateFormat)
        {
            // Log user date format change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User updated their `date format` setting from `{userSettings.DateFormat}` to `{userSettingsObject.DateFormat}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            userSettings.DateFormat = userSettingsObject.DateFormat;
        }

        // If the provided time format does not match the time format the update it
        if (userSettings.TimeFormat != userSettingsObject.TimeFormat)
        {
            // Log user time format change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User updated their `time format` setting from `{userSettings.TimeFormat}` to `{userSettingsObject.TimeFormat}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            userSettings.TimeFormat = userSettingsObject.TimeFormat;
        }

        // If the provided locale does not match the users locale the update it
        if (userSettings.Locale != userSettingsObject.Locale)
        {
            // Log user locale change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User updated their `locale` setting from {userSettings.Locale}` to `{userSettingsObject.Locale}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            userSettings.Locale = userSettingsObject.Locale;
        }

        // Save database changes 
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
        public required Database.UserSettings UserSettings { get; init; }
    }
}