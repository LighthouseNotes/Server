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
    public async Task<ActionResult<IEnumerable<API.UserAudit>>> SimpleAudit()
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

        // Get the user including their events
        Database.User user = await _dbContext.User.Where(u => u.Id == userId).Include(u => u.Events).SingleAsync();

        // Return all events with the type "Lighthouse Notes" ordered by date in descending order 
        return user.Events.Where(e => e.EventType == "Lighthouse Notes").Select(x => new API.UserAudit
        {
            Action = x.Data.RootElement.GetProperty("Action").GetString()!,
            DateTime = TimeZoneInfo.ConvertTimeFromUtc(x.Data.RootElement.GetProperty("StartDate").GetDateTime(),
                timeZone)
        }).OrderByDescending(e => e.DateTime).ToList();
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