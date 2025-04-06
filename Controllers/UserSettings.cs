namespace Server.Controllers;

[Route("/user/settings")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UsersSettingsController(DatabaseContext dbContext) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET: /user/settings
    // Will return the users settings
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<API.UserSettings>> GetUserSettings()
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null)
            return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Return the settings
        return new API.UserSettings
        {
            TimeZone = userSettings.TimeZone,
            DateFormat = userSettings.DateFormat,
            TimeFormat = userSettings.TimeFormat,
            Locale = userSettings.Locale
        };
    }

    // PUT: /user/settings
    // Will update a users settings
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<IActionResult> UpdateUserSettings(API.UserSettings userSettingsObject)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null)
            return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // If the provided time zone does not match the user's time zone the update it
        if (userSettings.TimeZone != userSettingsObject.TimeZone)
        {
            // Log user time zone change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User updated their `time zone` setting from `{userSettings.TimeZone}` to `{userSettingsObject.TimeZone}`.",
                    EmailAddress = emailAddress
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
                    EmailAddress = emailAddress
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
                    EmailAddress = emailAddress
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
                    EmailAddress = emailAddress
                });

            userSettings.Locale = userSettingsObject.Locale;
        }

        // Save database changes
        await dbContext.SaveChangesAsync();

        // Return HTTP 204 No Content
        return NoContent();
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user email from JWT claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return new PreflightResponse { Error = BadRequest("Email address cannot be found in the JSON Web Token (JWT)!") };

        // Query user details including user settings
        PreflightResponseDetails? preflightData = await dbContext.User
            .Where(u => u.EmailAddress == emailAddress)
            .Include(u => u.Settings)
            .Select(u => new PreflightResponseDetails { EmailAddress = u.EmailAddress, UserSettings = u.Settings })
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
        public required Database.UserSettings UserSettings { get; init; }
    }
}