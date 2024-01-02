// ReSharper disable InconsistentNaming

namespace Server.Controllers.Case;

[ApiController]
[Route("/case")]
[AuditApi(EventTypeName = "HTTP")]
public class CasesController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public CasesController(DatabaseContext context)
    {
        _dbContext = context;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /cases
    [Route("/cases")]
    [HttpGet]
    [Authorize(Roles = "user")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<API.Case>>> GetCases()
    {
      
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization or user are null return HTTP 500 error
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

       // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Settings.TimeZone);

        List<Database.Case> cases = await _dbContext.Case.Where(c => c.Users.All(cu => cu.User == user)).ToListAsync();

        return cases.Select(c => new API.Case
        {
            Id = c.Id,
            DisplayId = c.DisplayId,
            Name = c.Name,
            DisplayName = c.DisplayName,
            SIO = c.Users.Where(cu => cu.IsSIO).Select(cu =>  new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName, GivenName = cu.User.GivenName,
                LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress, ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).Single(),
            Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
            Modified =  TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
            Status = c.Status,
            Users = c.Users.Select(cu => new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).ToList()
        }).ToList();
    }

    // GET: /case/?
    [HttpGet("{caseId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Case>> GetCase(Guid caseId)
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

        // Return requested case details
        return new API.Case
        {
            Id = sCase.Id,
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            SIO = sCase.Users.Where(cu => cu.IsSIO).Select(cu =>  new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName, GivenName = cu.User.GivenName,
                LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress, ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).Single(),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Modified =  TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };
    }

    // PUT: /case/?
    [HttpPut("{caseId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<ActionResult> UpdateCase(Guid caseId, API.UpdateCase updatedCase)
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

        // Update values in the database if the provided values are not null
        if (updatedCase.Name != null)
        {
            // Log case name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case name was updated from `{sCase.Name}` to `{updatedCase.Name}` for the case with the ID `{sCase.DisplayId}` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Update case name in the database
            sCase.Name = updatedCase.Name;
        }

        if (updatedCase.DisplayId != null)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case ID was updated from `{sCase.DisplayId}` to `{updatedCase.DisplayId}` for the case with the name `{sCase.Name}` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Update case ID in the database
            sCase.DisplayId = updatedCase.DisplayId;
        }

        if (updatedCase.Status != null)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case status was updated from `{sCase.Status}` to `{updatedCase.Status}`  for the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Update case status in the database
            sCase.Status = updatedCase.Status;
        }

        // If a new SIO user is provided then remove the current SIO and update
        if (updatedCase.SIOUserId != null)
        {
            // Check new SIO user exists 
            Database.User? newSIO =
                await _dbContext.User.Where(u => u.Id == updatedCase.SIOUserId).FirstOrDefaultAsync();

            if (newSIO == null)
                return Unauthorized(
                    $"A user with the ID `{updatedCase.SIOUserId}` was not found in the organization with the ID `{organization.Id}`!");

            // Remove current SIO users access
            Database.CaseUser oldSIO = sCase.Users.Single(cu => cu.IsSIO);

            // Log removal of SIO user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {oldSIO.User.DisplayName}  ({oldSIO.User.JobTitle})` was removed from the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Set old SIO user to false
            oldSIO.IsSIO = false;

            // Log new SIO user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {newSIO.DisplayName}  ({newSIO.JobTitle}) is now the SIO for the case `{sCase.DisplayName}` this change was made by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Set new SIO user to true
            sCase.Users.Single(cu => cu.User.Id == newSIO.Id).IsSIO = true;

            // Log the SIO user given access
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {newSIO.DisplayName} ({newSIO.JobTitle})` was given access to the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });
        }

        // If userIDs are provided then add / remove them from the case
        if (updatedCase.UserIds != null)

            // Update users
            foreach (string userToChangeId in updatedCase.UserIds)
                // If user already has access to the case remove them
                if (sCase.Users.All(u => u.User.Id == userToChangeId))
                {
                    // Select user
                    Database.CaseUser caseUser = sCase.Users.First(u => u.User.Id == userToChangeId);

                    // Remove user from case
                    sCase.Users.Remove(caseUser);

                    // Log removal of user
                    await _auditContext.LogAsync("Lighthouse Notes",
                        new
                        {
                            Action =
                                $" `{caseUser.User.DisplayName} ({caseUser.User.JobTitle})` was removed from the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                            UserID = user.Id, OrganizationID = organization.Id
                        });
                }
                // Else add user to case
                else
                {
                    // Fetch user to add from the database
                    Database.User? userToAdd = organization.Users.FirstOrDefault(u => u.Id == userToChangeId);

                    // If user can not be found then
                    if (userToAdd == null)
                        return Unauthorized(
                            $"A user with the ID `{userToChangeId}` was not found in the organization with the ID `{organization.Id}`!");

                    // Add user to case
                    sCase.Users.Add(new Database.CaseUser
                    {
                        User = userToAdd
                    });

                    // Log addition of user
                    await _auditContext.LogAsync("Lighthouse Notes",
                        new
                        {
                            Action =
                                $" `{userToAdd.DisplayName} ({userToAdd.JobTitle})` was added to the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                            UserID = user.Id, OrganizationID = organization.Id
                        });
                }

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Return no content
        return NoContent();
    }

    // POST: /case
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "sio,organization-administrator")]
    public async Task<ActionResult<API.Case>> CreateCase(API.AddCase caseAddObject)
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
            return Unauthorized(
                $"A user with the ID `{userId}` was not found in the organization with the ID `{organizationId}`!");

        // Fetch user from database
        Database.User user = organization.Users.Single(u => u.Id == userId);
        
        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", user.Id);

        // If no SIO is provided then the user making the request is the SIO
        caseAddObject.SIOUserId ??= userId;

        // If the user is an SIO and is trying to create an case for another user return a HTTP 401 error
        if (User.IsInRole("sio") && caseAddObject.SIOUserId != userId)
            return Unauthorized("As you hold an SIO role you can only create cases where you are the SIO!");

        // Get SIO user from the database
        Database.User? SIOUser = organization.Users.FirstOrDefault(u => u.Id == caseAddObject.SIOUserId);


        if (SIOUser == null)
            return Unauthorized(
                $"A user with the ID `{caseAddObject.SIOUserId}` was not found in the organization with the ID `{organizationId}`!");


        // Create the case
        Database.Case sCase = new()
        {
            DisplayId = caseAddObject.DisplayId,
            Name = caseAddObject.Name,
            DisplayName = $"{caseAddObject.DisplayId} {caseAddObject.Name}",
            Status = "Open"
        };
        
        // Give the SIO user access to the case and make them SIO
        sCase.Users.Add(new Database.CaseUser()
        {
            User = SIOUser,
            IsSIO = true
        });

        // Log creation of case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"The case `{sCase.DisplayName}` was created by `{user.DisplayName} ({user.JobTitle})`.",
                UserID = userId, OrganizationID = organizationId
            });

        // Add the case to the database
        _dbContext.Case.Add(sCase);
        
        // Add users to case
        if (caseAddObject.UserIds != null)
            foreach (string userToAddId in caseAddObject.UserIds)
            {
                // Fetch user to add from the database
                Database.User? userToAdd = organization.Users.FirstOrDefault(u => u.Id == userToAddId);

                // If user can not be found then
                if (userToAdd == null)
                    return
                        Unauthorized(
                            $"A user with the ID `{userToAddId}` was not found in the organization with the ID `{organizationId}`!");

                // Add user to case
                sCase.Users.Add(new Database.CaseUser
                {
                    User = userToAdd
                });

                // Log addition of user
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"`{userToAdd.DisplayName} ({userToAdd.JobTitle})` was added to the case `{sCase.DisplayName}` by `{user.DisplayName} ({user.JobTitle})`.",
                        UserID = userId, OrganizationID = organizationId
                    });
            }

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Settings.TimeZone);

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Return the created case
        return new API.Case
        {
            Id = sCase.Id,
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            SIO = sCase.Users.Where(cu => cu.IsSIO).Select(cu =>  new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName, GivenName = cu.User.GivenName,
                LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress, ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).Single(),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Modified =  TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                Id = cu.User.Id, JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };
    }

     private async Task<PreflightResponse> PreflightChecks(Guid caseId = new())
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

        // If caseId is not provided then return
        if (caseId == Guid.Empty)
        {
            return new PreflightResponse { Organization = user.Organization, User = user };
        }
        
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