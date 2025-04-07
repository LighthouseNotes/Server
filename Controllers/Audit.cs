namespace Server.Controllers;

[Route("/audit")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class AuditController(DatabaseContext dbContext) : ControllerBase
{
    // GET: /audit/user
    // Will return a paginated list of events with the type "Lighthouse Notes" ordered by date in descending order
    [HttpGet("user")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<IEnumerable<API.UserAudit>>> UserAudit([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
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
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Set pagination headers
        HttpContext.Response.Headers.Append("X-Page", page.ToString());
        HttpContext.Response.Headers.Append("X-Per-Page", pageSize.ToString());
        HttpContext.Response.Headers.Append("X-Total-Count",
            dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.EmailAddress == emailAddress)
                .ToString());
        HttpContext.Response.Headers.Append("X-Total-Pages",
            ((dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.EmailAddress == emailAddress) +
                pageSize - 1) / pageSize).ToString());

        // Return all events with the type "Lighthouse Notes" ordered by date in descending order
        return dbContext.Event.Where(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.EmailAddress == emailAddress)
            .OrderByDescending(e => e.Created).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new API.UserAudit
            {
                Action = x.Data.RootElement.GetProperty("Action").GetString()!,
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(x.Data.RootElement.GetProperty("StartDate").GetDateTime(),
                    timeZone)
            }).ToList();
    }

    // GET: /audit/all
    // Will return a paginated list of events with the type "Lighthouse Notes" ordered by date in descending order
    [HttpGet("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<IEnumerable<API.UserAudit>>> AllAudit([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
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
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Set pagination headers
        HttpContext.Response.Headers.Append("X-Page", page.ToString());
        HttpContext.Response.Headers.Append("X-Per-Page", pageSize.ToString());
        HttpContext.Response.Headers.Append("X-Total-Count",
            dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes")
                .ToString());
        HttpContext.Response.Headers.Append("X-Total-Pages",
            ((dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes") +
                pageSize - 1) / pageSize).ToString());

        // Return all events with the type "Lighthouse Notes" ordered by date in descending order
        return dbContext.Event.Where(e => e.User != null && e.EventType == "Lighthouse Notes")
            .OrderByDescending(e => e.Created).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new API.UserAudit
            {
                Action = x.Data.RootElement.GetProperty("Action").GetString()!,
                DateTime = TimeZoneInfo.ConvertTimeFromUtc(x.Data.RootElement.GetProperty("StartDate").GetDateTime(),
                    timeZone)
            }).ToList();
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user email from JWT claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return new PreflightResponse { Error = BadRequest("Email address cannot be found in the JSON Web Token (JWT)!") };

        // Query user details including roles and application settings
        PreflightResponseDetails? preflightData = await dbContext.User
            .Where(u => u.EmailAddress == emailAddress)
            .Include(u => u.Settings)
            .Select(u => new PreflightResponseDetails { EmailAddress = u.EmailAddress, UserSettings = u.Settings })
            .SingleOrDefaultAsync();

        // If query result is null then the user does not exist
        if (preflightData == null)
            return new PreflightResponse { Error = NotFound($"A user with the email address: `{emailAddress}` was not found!") };

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