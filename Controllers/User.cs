namespace LighthouseNotesServer.Controllers;

[Route("/user")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UsersController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public UsersController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /users
    [Route("/users")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.User>>> GetUsers()
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
        Database.User requestingUser = preflightResponse.User;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", requestingUser.Id);

        // Return all users in a organization
        return organization.Users.Select(
            u => new API.User
            {
                Id = u.Id,
                JobTitle = u.JobTitle,
                GivenName = u.GivenName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                EmailAddress = u.EmailAddress,
                ProfilePicture = u.ProfilePicture,
                Organization = new API.Organization
                    { Name = u.Organization.DisplayName, DisplayName = u.Organization.DisplayName },
                Roles = u.Roles.Select(r => r.Name).ToList()
            }
        ).ToList();
    }


    // GET: /user/5
    [HttpGet("{userId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.User>> GetUser(string userId)
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
        Database.User requestingUser = preflightResponse.User;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", requestingUser.Id);

        // If the user is trying to fetch themselves then set user to requesting user if not fetch the user
        Database.User? user = requestingUser.Id == userId
            ? organization.Users.FirstOrDefault(u => u.Id == userId)
            : requestingUser;

        // If user does not exist in organization return a HTTP 403 error
        if (user == null)
            return Unauthorized(
                $"A user with the ID `{userId}` was not found in the organization with the ID `{organization.Id}`!");

        // Return the user
        return new API.User
        {
            Id = user.Id,
            JobTitle = user.JobTitle,
            GivenName = user.GivenName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            EmailAddress = user.EmailAddress,
            ProfilePicture = user.ProfilePicture,
            Organization = new API.Organization
                { Name = user.Organization.DisplayName, DisplayName = user.Organization.DisplayName },
            Roles = user.Roles.Select(r => r.Name).ToList()
        };
    }

    // PUT: /User/5
    [HttpPut("{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user,organization-administrator")]
    public async Task<IActionResult> UpdateUser(string userId, API.UpdateUser updatedUser)
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
        Database.User requestingUser = preflightResponse.User;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", requestingUser.Id);

        // If the user is trying to update themselves then set user to requesting user if not fetch the user
        Database.User? user = requestingUser.Id == userId
            ? organization.Users.FirstOrDefault(u => u.Id == userId)
            : requestingUser;

        // If user does not exist in organization return a HTTP 403 error
        if (user == null)
            return Unauthorized(
                $"A user with the ID `{userId}` was not found in the organization with the ID `{organization.Id}`!");


        // If requesting user id is not equal to the provided id or requesting users is not an organization administrator return HTTP 401
        if (requestingUser.Id != userId && !User.IsInRole("organization-administrator"))
            return Unauthorized(
                "You do not have permissions to update the details of a user that is not yourself");

        // If job title is provided updated it
        if (updatedUser.JobTitle != null)
        {
            // If the user is updating themselves display a slightly different log message 
            if (userId == requestingUser.Id)
                // Log user job title change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User job title was updated from `{user.JobTitle}` to `{updatedUser.JobTitle}` for the user `{user.DisplayName} ({updatedUser.JobTitle})` by `{requestingUser.DisplayName} ({updatedUser.JobTitle})`.",
                        UserID = requestingUser.Id, OrganizationID = organization.Id
                    });
            else
                // Log user job title change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User job title was updated from `{user.JobTitle}` to `{updatedUser.JobTitle}` for the user `{user.DisplayName} ({updatedUser.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                        UserID = requestingUser.Id, OrganizationID = organization.Id
                    });

            user.JobTitle = updatedUser.JobTitle;
        }

        // If given name is provided updated it
        if (updatedUser.GivenName != null)
        {
            // Log user given name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User given name was updated from `{user.GivenName}` to `{updatedUser.GivenName}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                    UserID = requestingUser.Id, OrganizationID = organization.Id
                });

            user.GivenName = updatedUser.GivenName;
        }

        // If last name is provided updated it
        if (updatedUser.LastName != null)
        {
            // Log user last name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User last name was updated from `{user.LastName}` to `{updatedUser.LastName}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                    UserID = requestingUser.Id, OrganizationID = organization.Id
                });

            user.LastName = updatedUser.LastName;
        }

        // If display name is provided updated it
        if (updatedUser.DisplayName != null)
        {
            // If the user is updating themselves display a slightly different log message 
            if (userId == requestingUser.Id)
                // Log user display name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User display name was updated from `{user.DisplayName}` to `{updatedUser.DisplayName}` for the user `{updatedUser.DisplayName} ({user.JobTitle})` by `{updatedUser.DisplayName} ({requestingUser.JobTitle})`.",
                        UserID = requestingUser.Id, OrganizationID = organization.Id
                    });
            else
                // Log user display name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User display name was updated from `{user.DisplayName}` to `{updatedUser.DisplayName}` for the user `{updatedUser.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                        UserID = requestingUser.Id, OrganizationID = organization.Id
                    });


            user.DisplayName = updatedUser.DisplayName;
        }

        // If email address is provided updated it
        if (updatedUser.EmailAddress != null)
        {
            // Log user email address change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User email address was updated from `{user.EmailAddress}` to `{updatedUser.EmailAddress}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                    UserID = requestingUser.Id, OrganizationID = organization.Id
                });

            user.EmailAddress = updatedUser.EmailAddress;
        }

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Return no content
        return NoContent();
    }

    // POST: /user
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.User>> CreateUser(API.AddUser userToAdd)
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
        
        if (organization == null)
            return NotFound($"A organization with the ID `{organizationId}` can not be found!");
       

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Create the user based on the provided values
        Database.User userModel = new()
        {
            Id = userToAdd.Id,
            JobTitle = userToAdd.JobTitle,
            GivenName = userToAdd.GivenName,
            LastName = userToAdd.LastName,
            DisplayName = userToAdd.DisplayName,
            EmailAddress = userToAdd.EmailAddress,
            ProfilePicture = userToAdd.ProfilePicture,
            Organization = organization
        };

        // Add the user
        await _dbContext.User.AddAsync(userModel);
        
        // Add the users roles
        foreach (string role in userToAdd.Roles)
        {
            await _dbContext.Role.AddAsync(new Database.Role
            {
                Name = role,
                User = userModel
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (_dbContext.User.Any(e => e.Id == userModel.Id))
                return Conflict($"A user with the ID `{userModel.Id}` already exists!");
            throw;
        }

        // Create response object
        API.User userResponseObject = new()
        {
            Id = userModel.Id,
            JobTitle = userModel.JobTitle,
            GivenName = userModel.GivenName,
            LastName = userModel.LastName,
            DisplayName = userModel.DisplayName,
            EmailAddress = userModel.EmailAddress,
            ProfilePicture = userModel.ProfilePicture,
            Organization = new API.Organization
                { Name = userModel.Organization.DisplayName, DisplayName = userModel.Organization.DisplayName },
            Roles = userModel.Roles.Select(r => r.Name).ToList()
        };


        // Log the successful creation of a user
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"User `{userModel.DisplayName} ({userModel.JobTitle})` added themselves to {organization.DisplayName}`.",
                UserID = userId, OrganizationID = organizationId
            });


        return CreatedAtAction(nameof(GetUser), new { userId = userModel.Id }, userResponseObject);
    }

    // DELETE: /User/5
    [HttpDelete("{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<IActionResult> DeleteUser(string userId)
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
        Database.User requestingUser = preflightResponse.User;

        // Log OrganizationID and UserID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organization.Id);
        auditScope.SetCustomField("UserID", requestingUser.Id);

        // Fetch user from the database and include organization 
        Database.User user = await _dbContext.User.Where(u => u.Id == userId).Include(u => u.Organization).FirstAsync();

        // If the requesting user is not part of the requesting organization return a HTTP 401 error
        if (requestingUser.Organization.Id != organization.Id)
            return Unauthorized(
                $"A user with the ID `{userId}` was not found in the organization with the ID `{organization.Id}`!");


        // Log removal of user
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"User `{user.DisplayName} ({user.JobTitle})` was deleted by `{requestingUser.DisplayName} ({requestingUser.JobTitle})`.",
                UserID = requestingUser.Id, OrganizationID = organization.Id
            });

        // Remove the user
        _dbContext.User.Remove(user);

        // Save changes
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