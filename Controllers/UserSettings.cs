namespace LighthouseNotesServer.Controllers;

[Route("/user/settings")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UsersSettingsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public UsersSettingsController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /user/settings
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.UserSettings>> GetUserSettings()
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        return new API.UserSettings
        {
            TimeZone = user.Settings.TimeZone,
            DateFormat = user.Settings.DateFormat,
            TimeFormat = user.Settings.TimeFormat,
            Locale = user.Settings.Locale
        };

    }
    
      // PUT: /user/settings
     [HttpPut]
     [ProducesResponseType(StatusCodes.Status204NoContent)]
     [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
     [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
     [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
     [Authorize(Roles = "user")]
     public async Task<IActionResult> UpdateUserSettings(string? userId, API.UserSettings userSettings)
     {
         // Get user id from claim
         string? requestingUserId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

         // If user id is null then it does not exist in JWT so return a HTTP 400 error
         if (requestingUserId == null)
             return BadRequest("User ID can not be found in the JSON Web Token (JWT)!");

         // Get organization id from claim
         string? organizationId = User.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;

         // If organization id  is null then it does not exist in JWT so return a HTTP 400 error
         if (organizationId == null)
             return BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!");

         // Fetch organization from the database by primary key
         Database.Organization? organization = await _dbContext.Organization.FindAsync(organizationId);

         // If organization is null then it does not exist so return a HTTP 404 error
         if (organization == null)
             return NotFound($"A organization with the ID `{organizationId}` can not be found!");

         // If user does not exist in organization return a HTTP 403 error
         if (organization.Users.All(u => u.Id != requestingUserId))
             return Unauthorized(
                 $"A user with the ID `{requestingUserId}` was not found in the organization with the ID `{organizationId}`!");

         // Fetch user from database
         Database.User requestingUser = organization.Users.Single(u => u.Id == requestingUserId);

         // Log OrganizationID and UserID
         IAuditScope auditScope = this.GetCurrentAuditScope();
         auditScope.SetCustomField("OrganizationID", organization.Id);
         auditScope.SetCustomField("UserID", requestingUser.Id);

         Database.User? user;
         // If provided user Id is not set, or user is trying to fetch themselves set user to requestingUser
         if (string.IsNullOrEmpty(userId) || requestingUser.Id != userId)
         {
             user = requestingUser;
         }
         // Fetch the user based on the provide user Id
         else
         {
             user = organization.Users.FirstOrDefault(u => u.Id == userId);

             // If user does not exist in organization return a HTTP 403 error
             if (user == null)
                 return Unauthorized(
                     $"A user with the ID `{userId}` was not found in the organization with the ID `{organization.Id}`!");
         }

         if (user.Settings.TimeZone != userSettings.TimeZone)
         {
             // Log user time zone change
             await _auditContext.LogAsync("Lighthouse Notes",
                 new
                 {
                     Action =
                         $"User setting `time zone` was updated from `{user.Settings.TimeZone}` to `{userSettings.TimeZone}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                     UserID = requestingUser.Id, OrganizationID = organization.Id
                 });

             user.Settings.TimeZone = userSettings.TimeZone;
         }
         
         if (user.Settings.DateFormat != userSettings.DateFormat)
         {
             // Log user date format change
             await _auditContext.LogAsync("Lighthouse Notes",
                 new
                 {
                     Action =
                         $"User setting `date format` was updated from `{user.Settings.DateFormat}` to `{userSettings.DateFormat}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                     UserID = requestingUser.Id, OrganizationID = organization.Id
                 });
             
             user.Settings.DateFormat = userSettings.DateFormat;
         }
         
         if (user.Settings.TimeFormat != userSettings.TimeFormat)
         {
             // Log user time format change
             await _auditContext.LogAsync("Lighthouse Notes",
                 new
                 {
                     Action =
                         $"User setting `time format` was updated from `{user.Settings.TimeFormat}` to `{userSettings.TimeFormat}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                     UserID = requestingUser.Id, OrganizationID = organization.Id
                 });
             
             user.Settings.TimeFormat = userSettings.TimeFormat;
         }
         
         if (user.Settings.Locale != userSettings.Locale)
         {
             // Log user locale change
             await _auditContext.LogAsync("Lighthouse Notes",
                 new
                 {
                     Action =
                         $"User setting `locale` was updated from `{user.Settings.Locale}` to `{userSettings.Locale}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                     UserID = requestingUser.Id, OrganizationID = organization.Id
                 });
             
             user.Settings.Locale = userSettings.Locale;
         }
         
         return NoContent();
     }
     
     private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user id from claim
        string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        // If user id is null then it does not exist in JWT so return a HTTP 400 error
        if (userId == null)
            return new PreflightResponse
                { Error = BadRequest("User ID can not be found in the JSON Web Token (JWT)!") };

        // Get organization id from claim
        string? organizationId = User.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;

        // If organization id  is null then it does not exist in JWT so return a HTTP 400 error
        if (organizationId == null)
            return new PreflightResponse
                { Error = BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!") };

        // Fetch organization from the database by primary key
        Database.Organization? organization = await _dbContext.Organization.FindAsync(organizationId);

        // If organization is null then it does not exist so return a HTTP 404 error
        if (organization == null)
            return new PreflightResponse
                { Error = NotFound($"A organization with the ID `{organizationId}` can not be found!") };

        // If user does not exist in organization return a HTTP 403 error
        if (organization.Users.All(u => u.Id != userId))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"A user with the ID `{userId}` was not found in the organization with the ID `{organizationId}`!")
            };

        // Fetch user from database
        Database.User user = organization.Users.Single(u => u.Id == userId);

        // Return organization, user and case entity 
        return new PreflightResponse { Organization = organization, User = user };
    }


    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }
    }

}