namespace LighthouseNotesServer.Controllers;

[Route("/organization/config")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class OrganizationConfigController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public OrganizationConfigController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }
    
     // GET: /organization/config
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<ActionResult<API.OrganizationConfig>> GetSettings()
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

        return new API.OrganizationConfig()
        {
            S3Endpoint = organization.Configuration.S3Endpoint,
            S3AccessKey = organization.Configuration.S3AccessKey,
            S3BucketName = organization.Configuration.S3BucketName,
            S3NetworkEncryption = organization.Configuration.S3NetworkEncryption,
            S3SecretKey = organization.Configuration.S3SecretKey
        };
    }
    
      // PUT: /organization/config
     [HttpPut]
     [ProducesResponseType(StatusCodes.Status204NoContent)]
     [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
     [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
     [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
     [Authorize(Roles = "organization-administrator")]
     public async Task<IActionResult> UpdateSettings(API.OrganizationConfig organizationConfig)
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

         if (!string.IsNullOrWhiteSpace(organizationConfig.S3Endpoint) &&
             organizationConfig.S3Endpoint != organization.Configuration.S3Endpoint)
         {
             organization.Configuration.S3Endpoint = organizationConfig.S3Endpoint;
         }
         
         if (organizationConfig.S3NetworkEncryption != organization.Configuration.S3NetworkEncryption)
         {
             organization.Configuration.S3NetworkEncryption = organizationConfig.S3NetworkEncryption;
         }
         
         if (!string.IsNullOrWhiteSpace(organizationConfig.S3BucketName) &&
             organizationConfig.S3BucketName != organization.Configuration.S3BucketName)
         {
             organization.Configuration.S3BucketName = organizationConfig.S3BucketName;
         }
         
         if (!string.IsNullOrWhiteSpace(organizationConfig.S3AccessKey) &&
             organizationConfig.S3AccessKey != organization.Configuration.S3AccessKey)
         {
             organization.Configuration.S3AccessKey = organizationConfig.S3AccessKey;
         }
         
         if (!string.IsNullOrWhiteSpace(organizationConfig.S3SecretKey) &&
             organizationConfig.S3SecretKey != organization.Configuration.S3SecretKey)
         {
             organization.Configuration.S3SecretKey = organizationConfig.S3SecretKey;
         }

         await _dbContext.SaveChangesAsync();
         
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
