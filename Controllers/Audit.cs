namespace Server.Controllers;

[Route("/audit")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class AuditController : ControllerBase
{
    private readonly DatabaseContext _dbContext;

    public AuditController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
    }

    // GET: /audit/user
    [HttpGet("user")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.UserAudit>>> SimpleAudit([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
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

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Set pagination headers
        HttpContext.Response.Headers.Add("X-Page", page.ToString());
        HttpContext.Response.Headers.Add("X-Per-Page", pageSize.ToString());
        HttpContext.Response.Headers.Add("X-Total-Count", _dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.Id == userId).ToString());
        HttpContext.Response.Headers.Add("X-Total-Pages", ((_dbContext.Event.Count(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.Id == userId) + pageSize - 1 ) / pageSize ).ToString());

        // Return all events with the type "Lighthouse Notes" ordered by date in descending order 
        return _dbContext.Event.Where(e => e.User != null && e.EventType == "Lighthouse Notes" && e.User.Id == userId).OrderByDescending(e => e.Created).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new API.UserAudit
        {
            Action = x.Data.RootElement.GetProperty("Action").GetString()!,
            DateTime = TimeZoneInfo.ConvertTimeFromUtc(x.Data.RootElement.GetProperty("StartDate").GetDateTime(),
                timeZone)
        }).ToList();
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
        public long UserId { get; init; }
        public required Database.UserSettings UserSettings { get; init; }
    }
}