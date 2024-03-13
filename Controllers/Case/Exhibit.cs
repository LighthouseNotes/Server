namespace Server.Controllers.Case;

[Route("/case/{caseId}")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class ExhibitController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public ExhibitController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // GET: /case/?/exhibits
    [HttpGet("exhibits")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.Exhibit>>> GetExhibits(string caseId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery(Name = "sort")] string rawSort = "")
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
        long rawCaseId = _sqids.Decode(caseId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.Exhibits)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Create sort list
        List<string>? sortList = null;

        // If raw sort is specified set sort list
        if (!string.IsNullOrWhiteSpace(rawSort)) sortList = rawSort.Split(',').ToList();

        // Set pagination headers
        HttpContext.Response.Headers.Add("X-Page", page.ToString());
        HttpContext.Response.Headers.Add("X-Per-Page", pageSize.ToString());
        HttpContext.Response.Headers.Add("X-Total-Count", sCase.Exhibits.Count.ToString());


        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        if (pageSize == 0)
        {
            HttpContext.Response.Headers.Add("X-Total-Pages", "0");
            // Return the cases exhibits 
            return sCase.Exhibits.Select(e => new API.Exhibit
            {
                Id = _sqids.Encode(e.Id),
                Reference = e.Reference,
                Description = e.Description,
                DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(e.DateTimeSeizedProduced, timeZone),
                WhereSeizedProduced = e.WhereSeizedProduced,
                SeizedBy = e.SeizedBy
            }).ToList();
        }

        HttpContext.Response.Headers.Add("X-Total-Pages",
            ((sCase.Exhibits.Count + pageSize - 1) / pageSize).ToString());

        // If no sort is provided or two many sort parameters are provided, sort by accessed time
        if (sortList == null || sortList.Count != 1)
            // Return the cases exhibits 
            return sCase.Exhibits.Select(e => new API.Exhibit
            {
                Id = _sqids.Encode(e.Id),
                Reference = e.Reference,
                Description = e.Description,
                DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(e.DateTimeSeizedProduced, timeZone),
                WhereSeizedProduced = e.WhereSeizedProduced,
                SeizedBy = e.SeizedBy
            }).Skip((page - 1) * pageSize).Take(pageSize).ToList();

        string[] sort = sortList[0].Split(" ");
        if (sort[1] == "asc")
            // Return the cases exhibits 
            return sCase.Exhibits.Select(e => new API.Exhibit
                {
                    Id = _sqids.Encode(e.Id),
                    Reference = e.Reference,
                    Description = e.Description,
                    DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(e.DateTimeSeizedProduced, timeZone),
                    WhereSeizedProduced = e.WhereSeizedProduced,
                    SeizedBy = e.SeizedBy
                }).OrderBy(e => e.GetType().GetProperty(sort[0])?.GetValue(e)).Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

        if (sort[1] == "desc")
            // Return the cases exhibits 
            return sCase.Exhibits.Select(e => new API.Exhibit
                {
                    Id = _sqids.Encode(e.Id),
                    Reference = e.Reference,
                    Description = e.Description,
                    DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(e.DateTimeSeizedProduced, timeZone),
                    WhereSeizedProduced = e.WhereSeizedProduced,
                    SeizedBy = e.SeizedBy
                }).OrderByDescending(e => e.GetType().GetProperty(sort[0])?.GetValue(e)).Skip((page - 1) * pageSize)
                .Take(pageSize).ToList();

        return BadRequest(
            $"Did you understand if you want to sort {sort[0]} ascending or descending. Use asc or desc to sort!");
    }

    // GET: /case/?/exhibit/?
    [HttpGet("exhibit/{exhibitId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Exhibit>> GetExhibit(string caseId, string exhibitId)
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
        long rawCaseId = _sqids.Decode(caseId)[0];
        long rawExhibitId = _sqids.Decode(exhibitId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.Exhibits)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Fetch exhibit from the database
        Database.Exhibit? exhibit = sCase.Exhibits.SingleOrDefault(e => e.Id == rawExhibitId);

        // If exhibit is null return HTTP 404 error
        if (exhibit == null)
            return NotFound($"A exhibit with the ID `{exhibitId}` was not found in the case with the ID `{caseId}`!");

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return the exhibit details
        return new API.Exhibit
        {
            Id = _sqids.Encode(exhibit.Id),
            Reference = exhibit.Reference,
            Description = exhibit.Description,
            DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(exhibit.DateTimeSeizedProduced, timeZone),
            WhereSeizedProduced = exhibit.WhereSeizedProduced,
            SeizedBy = exhibit.SeizedBy
        };
    }

    // POST: /case/?/exhibit
    [HttpPost("exhibit")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "sio,organization-administrator")]
    public async Task<ActionResult<API.Exhibit>> CreateExhibit(string caseId, API.AddExhibit exhibit)
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
        long rawCaseId = _sqids.Decode(caseId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // As datetime are provided in UTC, make sure kind is set to UTC so stored in the database as UTC
        exhibit.DateTimeSeizedProduced = DateTime.SpecifyKind(exhibit.DateTimeSeizedProduced, DateTimeKind.Utc);

        // Create the exhibit entity
        Database.Exhibit newExhibit = new()
        {
            Reference = exhibit.Reference,
            Description = exhibit.Description,
            DateTimeSeizedProduced = exhibit.DateTimeSeizedProduced,
            WhereSeizedProduced = exhibit.WhereSeizedProduced,
            SeizedBy = exhibit.SeizedBy
        };

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.Exhibits)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Add the exhibit to the case
        sCase.Exhibits.Add(newExhibit);

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Log the creation of a exhibit
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` created the exhibit: `{newExhibit.Reference}` in the case: `{sCase.DisplayName}`.",
                UserID = userId, OrganizationID = organizationId
            });

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Create the exhibit details response 
        API.Exhibit createdExhibit = new()
        {
            Id = _sqids.Encode(newExhibit.Id),
            Reference = newExhibit.Reference,
            Description = newExhibit.Description,
            DateTimeSeizedProduced = TimeZoneInfo.ConvertTimeFromUtc(newExhibit.DateTimeSeizedProduced, timeZone),
            WhereSeizedProduced = newExhibit.WhereSeizedProduced,
            SeizedBy = newExhibit.SeizedBy
        };

        // Return the newly created exhibit
        return CreatedAtAction(nameof(GetExhibit),
            new { caseId, exhibitId = newExhibit.Id }, createdExhibit);
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
        public long UserId { get; init; }
        public required string UserNameJob { get; init; }
        public required Database.UserSettings UserSettings { get; init; }
    }
}