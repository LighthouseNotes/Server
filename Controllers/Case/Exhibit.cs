namespace Server.Controllers.Case;

[Route("/case")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ExhibitController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public ExhibitController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /case/5/exhibits
    [Route("/case/{caseId:guid}/exhibits")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.Exhibit>>> GetExhibits(Guid caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Settings.TimeZone);
        
        return sCase.Exhibits.Select(e => new API.Exhibit
        {
            Id = e.Id,
            Reference = e.Reference,
            Description = e.Description,
            DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(e.DateTimeSeizedProduced, timeZone),
            WhereSeizedProduced = e.WhereSeizedProduced,
            SeizedBy = e.SeizedBy,
            Users = e.Users.Select(u => new API.User
            {
                Id = u.Id, JobTitle = u.JobTitle, DisplayName = u.DisplayName,
                GivenName = u.GivenName, LastName = u.LastName, EmailAddress = u.EmailAddress,
                ProfilePicture = u.ProfilePicture,
                Organization = new API.Organization
                    { Name = u.Organization.DisplayName, DisplayName = u.Organization.DisplayName },
                Roles = u.Roles.Select(r => r.Name).ToList()
            }).ToList()
        }).Where(e => e.Users.Any(u => u.Id == user.Id)).ToList();
    }

    // GET: /case/5/exhibit/5
    [Route("/case/{caseId:guid}/exhibit/{exhibitId:guid}")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Exhibit>> GetExhibit(Guid caseId, Guid exhibitId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // Fetch exhibit from the database
        Database.Exhibit? exhibit = sCase.Exhibits.SingleOrDefault(e => e.Id == exhibitId);

        // If exhibit is null return HTTP 404 error
        if (exhibit == null)
            return NotFound($"A exhibit with the ID `{exhibitId}` was not found in the case with the ID `{caseId}`!");

        // If user does not have access to the exhibit then return HTTP 401 error
        if (!exhibit.Users.Contains(user))
            return Unauthorized($"You do not have permission to access the exhibit with the ID `{exhibitId}`!");

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Settings.TimeZone);
        
        return new API.Exhibit
        {
            Id = exhibit.Id,
            Reference = exhibit.Reference,
            Description = exhibit.Description,
            DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(exhibit.DateTimeSeizedProduced, timeZone),
            WhereSeizedProduced = exhibit.WhereSeizedProduced,
            SeizedBy = exhibit.SeizedBy,
            Users = exhibit.Users.Select(u => new API.User
            {
                Id = u.Id, JobTitle = u.JobTitle, DisplayName = u.DisplayName,
                GivenName = u.GivenName, LastName = u.LastName, EmailAddress = u.EmailAddress,
                ProfilePicture = u.ProfilePicture,
                Organization = new API.Organization
                    { Name = u.Organization.DisplayName, DisplayName = u.Organization.DisplayName },
                Roles = u.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };
    }

    // POST: /case/5/exhibit
    [Route("/case/{caseId:guid}/exhibit")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "sio,organization-administrator")]
    public async Task<ActionResult<API.Exhibit>> CreateExhibit(Guid caseId, API.AddExhibit exhibit)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or case are null return HTTP 500 error
        if (preflightResponse.Organization == null || preflightResponse.User == null ||
            preflightResponse.SCase == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        Database.Organization organization = preflightResponse.Organization;
        Database.User user = preflightResponse.User;
        Database.Case sCase = preflightResponse.SCase;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // As datetime are provided in UTC, make sure kind is set to UTC so stored in the database as UTC
        exhibit.DateTimeSeizedProduced = DateTime.SpecifyKind(exhibit.DateTimeSeizedProduced, DateTimeKind.Utc);

        Database.Exhibit newExhibit = new()
        {
            Reference = exhibit.Reference,
            Description = exhibit.Description,
            DateTimeSeizedProduced = exhibit.DateTimeSeizedProduced,
            WhereSeizedProduced = exhibit.WhereSeizedProduced,
            SeizedBy = exhibit.SeizedBy
        };

        sCase.Exhibits.Add(newExhibit);

        // If no userIds are provided automatically add the SIO
        exhibit.UserIds ??= new List<string> { user.Id };

        // Add users to exhibit 
        foreach (string userToAddId in exhibit.UserIds)
        {
            // Fetch user to add from the database
            Database.User? userToAdd = organization.Users.FirstOrDefault(u => u.Id == userToAddId);

            // If user can not be found then
            if (userToAdd == null)
                return
                    Unauthorized(
                        $"A user with the ID `{userToAddId}` was not found in the organization with the ID `{organization.Id}`!");

            // Add user to case
            newExhibit.Users.Add(userToAdd);

            // Log addition of user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `{userToAdd.DisplayName} ({userToAdd.JobTitle})` was added to the exhibit `{newExhibit.Reference} ` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });
        }

        // Save changes to the database
        await _dbContext.SaveChangesAsync();
        
        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Settings.TimeZone);

        API.Exhibit createdExhibit = new()
        {
            Id = newExhibit.Id,
            Reference = newExhibit.Reference,
            Description = newExhibit.Description,
            DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(newExhibit.DateTimeSeizedProduced, timeZone),
            WhereSeizedProduced = newExhibit.WhereSeizedProduced,
            SeizedBy = newExhibit.SeizedBy,
            Users = newExhibit.Users.Select(u => new API.User
            {
                Id = u.Id, JobTitle = u.JobTitle, DisplayName = u.DisplayName,
                GivenName = u.GivenName, LastName = u.LastName, EmailAddress = u.EmailAddress,
                ProfilePicture = u.ProfilePicture,
                Organization = new API.Organization
                    { Name = u.Organization.DisplayName, DisplayName = u.Organization.DisplayName },
                Roles = u.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };

        return CreatedAtAction(nameof(GetExhibit),
            new { caseId, exhibitId = newExhibit.Id }, createdExhibit);
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

        // If organization id  is null then it does not exist in JWT so return a HTTP 400 error
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
        if (sCase.Users.All(u => u.User.Id != userId))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"The user with the ID `{userId}` does not have access to the case with the ID `{caseId}`!")
            };

        // Return organization, user and case entity 
        return new PreflightResponse { Organization = user.Organization, User = user, SCase = sCase };
    }
    
    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }

        public Database.Case? SCase { get; init; }
    }
}