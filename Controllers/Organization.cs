namespace LighthouseNotesServer.Controllers;

[Route("[controller]")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class OrganizationController : ControllerBase
{
    private readonly DatabaseContext _dbContext;

    public OrganizationController(DatabaseContext context)
    {
        _dbContext = context;
    }

    // GET: /organization/search
    [HttpGet("/organization/search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SearchOrganization(string organizationQuery)
    {
        // Search both Name and DisplayName for the provided organization query Note: both Name and DisplayName columns are case insensitive
        Database.Organization? organization = await _dbContext.Organization
            .Where(o => EF.Functions.TrigramsSimilarity(o.Name, organizationQuery) > 0.6 || EF.Functions.TrigramsSimilarity(o.DisplayName, organizationQuery) > 0.6).FirstOrDefaultAsync();

        // If the organization could not be found then return a HTTP 404 error
        if (organization == null)
            return NotFound($"An organization with the name `{organizationQuery}` could not be found!");

        // Return the organization ID
        return Ok(organization.Id);
    }

    // GET: /organization
    [HttpGet("/organization")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Organization>> GetOrganization()
    {
        // Get user id from claim
        string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        // If user id is null then it does not exist in JWT so return a HTTP 400 error
        if (userId == null)
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
        if (organization.Users.All(u => u.Id != userId))
            Unauthorized(
                $"A user with the ID `{userId}` was not found in the organization with the ID `{organizationId}`!");

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Return organization details
        return new API.Organization
        {
            Name = organization.Name,
            DisplayName = organization.DisplayName
        };
    }
}