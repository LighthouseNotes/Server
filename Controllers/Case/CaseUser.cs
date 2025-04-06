using Meilisearch;
using Index = Meilisearch.Index;

namespace Server.Controllers.Case;

[ApiController]
[Route("/case/{caseId}/user/{emailAddress}")]
[AuditApi(EventTypeName = "HTTP")]
public class CaseUserController(DatabaseContext dbContext, SqidsEncoder<long> sqids, IConfiguration configuration) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // POST: /case/?/user/?
    [HttpPut]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> PutCaseUser(string caseId, string emailAddress)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        string requestingEmailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", requestingEmailAddress);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId &&
                        c.Users.Any(cu => cu.User.EmailAddress == requestingEmailAddress && cu.IsLeadInvestigator))
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Get the user form the database
        Database.User? user = dbContext.User.FirstOrDefault(u =>
            u.EmailAddress == emailAddress);

        // If user is null then return HTTP 404 not found
        if (user == null) return NotFound($"A user with the ID `{emailAddress}` does not exist in your organization!");

        // Add user to the case
        await dbContext.CaseUser.AddAsync(new Database.CaseUser { Case = sCase, User = user });

        // Save changes to the database
        await dbContext.SaveChangesAsync();

        // Log the addition of the user to the case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` added the user: `{user.DisplayName} {user.JobTitle}` to the case: `{sCase.DisplayName}`.",
                EmailAddress = requestingEmailAddress
            });

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Get the cases index
        Index index = meiliClient.Index("cases");

        // Update Meilisearch document
        await index.UpdateDocumentsAsync([
            new Search.Case
            {
                Id = sCase.Id,
                EmailAddresses = sCase.Users.Select(u => u.User.EmailAddress).ToList(),
                DisplayName = sCase.DisplayName,
                Name = sCase.Name,
                LeadInvestigatorDisplayName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.DisplayName,
                LeadInvestigatorGivenName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.GivenName,
                LeadInvestigatorLastName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.LastName
            }
        ]);

        // Return HTTP 204 No Content
        return NoContent();
    }

    // DELETE: /case/?/user/?
    [HttpDelete]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> DeleteCaseUser(string caseId, string emailAddress)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem(
                "Preflight checks failed with an unknown error!"
            );

        // Set variables from preflight response
        string requestingEmailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email adress
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", requestingEmailAddress);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId &&
                        c.Users.Any(cu => cu.User.EmailAddress == requestingEmailAddress && cu.IsLeadInvestigator))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        Database.CaseUser? caseUser = sCase.Users.FirstOrDefault(cu => cu.User.EmailAddress == emailAddress);

        if (caseUser == null)
            return NotFound(
                $"The user `{emailAddress}` does not have access to the case `{caseId}` therefore can not be removed from said case.");

        if (caseUser.IsLeadInvestigator) return UnprocessableEntity("You can not delete the LeadInvestigator from the case!");

        dbContext.CaseUser.Remove(caseUser);

        await dbContext.SaveChangesAsync();

        // Log the removal of the user from the case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` removed the user: `{caseUser.User.DisplayName} {caseUser.User.JobTitle}` from the case: `{sCase.DisplayName}`.",
                EmailAddress = requestingEmailAddress
            });


        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Get the cases index
        Index index = meiliClient.Index("cases");

        // Update Meilisearch document
        await index.UpdateDocumentsAsync([
            new Search.Case
            {
                Id = sCase.Id,
                EmailAddresses = sCase.Users.Select(u => u.User.EmailAddress).ToList(),
                DisplayId = sCase.DisplayId,
                DisplayName = sCase.DisplayName,
                Name = sCase.Name,
                LeadInvestigatorDisplayName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.DisplayName,
                LeadInvestigatorGivenName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.GivenName,
                LeadInvestigatorLastName = sCase.Users.Single(cu => cu.IsLeadInvestigator).User.LastName
            }
        ]);
        return Ok();
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user email from JWT claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return new PreflightResponse { Error = BadRequest("Email Address cannot be found in the JSON Web Token (JWT)!") };

        // Query user details including roles and application settings
        PreflightResponseDetails? preflightData = await dbContext.User
            .Where(u => u.EmailAddress == emailAddress)
            .Select(u => new PreflightResponseDetails { EmailAddress = u.EmailAddress, UserNameJob = $"{u.DisplayName} ({u.JobTitle})" })
            .SingleOrDefaultAsync();

        // If query result is null then the user does not exist
        if (preflightData == null)
            return new PreflightResponse { Error = NotFound($"A user with the user email: `{emailAddress}` was not found!") }
                ;

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
        public required string UserNameJob { get; init; }
    }
}