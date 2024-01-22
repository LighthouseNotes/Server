namespace Server.Controllers.Case;

[ApiController]
[Route("/case/{caseId}/user/{userId}")]
[AuditApi(EventTypeName = "HTTP")]
public class CaseUserController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public CaseUserController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // POST: /case/?/user/?
    [HttpPut]
    [Authorize(Roles = "sio, organization-administrator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> PutCaseUser(string caseId, string userId)
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
        long requestingUserId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == requestingUserId))
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // If user is not in role and the user is not the SIO of the case then return HTTP 401 unauthorized
        if (!User.IsInRole("organization-administrator") &&
            sCase.Users.SingleOrDefault(cu => cu.IsSIO && cu.User.Id == requestingUserId) == null)
            return Unauthorized("You do not have permission to edit this case as you did not create it!");

        // Get the user form the database
        Database.User? user = _dbContext.User.FirstOrDefault(u =>
            u.Id == _sqids.Decode(userId)[0] && u.Organization.Id == organizationId);

        // If user is null then return HTTP 404 not found 
        if (user == null) return NotFound($"A user with the ID `{userId}` does not exist in your organization!");

        // Add user to the case
        await _dbContext.CaseUser.AddAsync(new Database.CaseUser
        {
            Case = sCase,
            User = user
        });

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Log the addition of the user to the case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` added the user: `{user.DisplayName} {user.JobTitle}` to the case: `{sCase.DisplayName}`.",
                UserID = requestingUserId, OrganizationID = organizationId
            });

        // Return HTTP 204 No Content 
        return NoContent();
    }

    // DELETE: /case/?/user/?
    [HttpDelete]
    [Authorize(Roles = "sio, organization-administrator")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteCaseUser(string caseId, string userId)
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
        long requestingUserId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == _sqids.Decode(caseId)[0] && c.Users.Any(cu => cu.User.Id == requestingUserId))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        if (!User.IsInRole("organization-administrator") &&
            sCase.Users.SingleOrDefault(cu => cu.IsSIO && cu.User.Id == requestingUserId) == null)
            return Unauthorized("You do not have permission to edit this case as you did not create it!");

        Database.CaseUser? caseUser = sCase.Users.FirstOrDefault(cu => cu.User.Id == _sqids.Decode(userId)[0]);

        if (caseUser == null)
            return NotFound(
                $"The user `{userId}` does not have access to the case `{caseId}` therefore can not be removed from said case.");

        if (caseUser.IsSIO) return UnprocessableEntity("You can not delete the SIO from the case!");

        _dbContext.CaseUser.Remove(caseUser);

        await _dbContext.SaveChangesAsync();

        // Log the removal of the user from the case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` removed the user: `{caseUser.User.DisplayName} {caseUser.User.JobTitle}` from the case: `{sCase.DisplayName}`.",
                UserID = requestingUserId, OrganizationID = organizationId
            });

        return Ok();
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
        public long UserId { get; init; }
        public required string UserNameJob { get; init; }
    }
}