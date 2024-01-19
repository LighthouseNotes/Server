namespace Server.Controllers;

[Route("/user")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UserController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public UserController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
    }

    // GET: /users
    [Route("/users")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<List<API.User>>> GetUsers()
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
        long userId = preflightResponse.Details .UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Return all users in a organization
        return _dbContext.Organization
            .SelectMany(org => org.Users)
            .Include(u => u.Roles)
            .Select(u => new API.User
            {
                Id = _sqids.Encode(u.Id),
                JobTitle = u.JobTitle,
                GivenName = u.GivenName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                EmailAddress = u.EmailAddress,
                ProfilePicture = u.ProfilePicture,
                Organization = new API.Organization
                {
                    Name = u.Organization.DisplayName,
                    DisplayName = u.Organization.DisplayName
                },
                Roles = u.Roles.Select(r => r.Name).ToList()
            })
            .ToList();
    }


    // GET: /user/?
    [HttpGet("{userId?}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.User>> GetUser(string? userId)
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
        long requestingUserId = preflightResponse.Details .UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        // If no user ID is specified then use the requesting user ID else convert the provided user ID squid to the user's ID
        long rawUserId = string.IsNullOrEmpty(userId) ? requestingUserId : _sqids.Decode(userId).Single();

        // Fetch the user based on the provide user ID
        Database.User? user = _dbContext.User.FirstOrDefault(u => u.Id == rawUserId && u.Organization.Id == organizationId);

        // If user does not exist in organization return a HTTP 403 error
        if (user == null)
            return Unauthorized(
                $"A user with the Auth0 user ID `{userId}` was not found in the organization with the Auth0 organization ID `{organizationId}`!");

        // Load user roles from the database
        await _dbContext.Entry(user).Collection(u => u.Roles).LoadAsync();
        await _dbContext.Entry(user).Reference(u => u.Organization).LoadAsync();

        // Return the user
        return new API.User
        {
            Id = _sqids.Encode(user.Id),
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

    // PUT: /user/?
    [HttpPut("{userId?}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user,organization-administrator")]
    public async Task<IActionResult> UpdateUser(string? userId, API.UpdateUser updatedUser)
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
        long requestingUserId = preflightResponse.Details .UserId;
        string requestingUserNameJob = preflightResponse.Details.UserNameJob;
        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        bool updateThemselves = false;
        long rawUserId;

        if (string.IsNullOrEmpty(userId))
        {
            rawUserId = requestingUserId;
            updateThemselves = true;
        }
        else
        {
            rawUserId = _sqids.Decode(userId).Single();

            if (rawUserId == requestingUserId) updateThemselves = true;
        }

        // Fetch the user based on the provide user ID
        Database.User? user = _dbContext.User.Include(u => u.Roles).FirstOrDefault(u => u.Id == rawUserId && u.Organization.Id == organizationId);

        // If user does not exist in organization return a HTTP 403 error
        if (user == null)
            return Unauthorized(
                $"A user with the Auth0 user ID `{userId}` was not found in the organization with the Auth0 organization ID `{organizationId}`!");

        // If job title is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.JobTitle) && updatedUser.JobTitle != user.JobTitle)
        {
            // If the user is updating themselves display a slightly different log message 
            if (updateThemselves)
                // Log user job title change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User job title was updated from `{user.JobTitle}` to `{updatedUser.JobTitle}` for the user `{user.DisplayName} ({updatedUser.JobTitle})` by `{requestingUserNameJob}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });
            else
                // Log user job title change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User job title was updated from `{user.JobTitle}` to `{updatedUser.JobTitle}` for the user `{user.DisplayName} ({updatedUser.JobTitle})` by `{requestingUserNameJob}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });

            user.JobTitle = updatedUser.JobTitle;
        }

        // If given name is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.GivenName) && updatedUser.GivenName != user.GivenName)
        {
            // Log user given name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User given name was updated from `{user.GivenName}` to `{updatedUser.GivenName}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                    UserID = requestingUserId, OrganizationID = organizationId
                });

            user.GivenName = updatedUser.GivenName;
        }

        // If last name is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.LastName) && updatedUser.LastName != user.LastName)
        {
            // Log user last name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User last name was updated from `{user.LastName}` to `{updatedUser.LastName}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                    UserID = requestingUserId, OrganizationID = organizationId
                });

            user.LastName = updatedUser.LastName;
        }

        // If display name is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.DisplayName) && updatedUser.DisplayName != user.DisplayName)
        {
            // If the user is updating themselves display a slightly different log message 
            if (updateThemselves)
                // Log user display name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User display name was updated from `{user.DisplayName}` to `{updatedUser.DisplayName}` for the user `{updatedUser.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });
            else
                // Log user display name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User display name was updated from `{user.DisplayName}` to `{updatedUser.DisplayName}` for the user `{updatedUser.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });


            user.DisplayName = updatedUser.DisplayName;
        }

        // If email address is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.EmailAddress) && updatedUser.EmailAddress != user.EmailAddress)
        {
            // Log user email address change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User email address was updated from `{user.EmailAddress}` to `{updatedUser.EmailAddress}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                    UserID = requestingUserId, OrganizationID = organizationId
                });

            user.EmailAddress = updatedUser.EmailAddress;
        }

        // If profile picture is provided update it
        if (!string.IsNullOrWhiteSpace(updatedUser.ProfilePicture) && updatedUser.ProfilePicture != user.ProfilePicture)
        {
            // If requesting user is not the user that's being updated return 403 Forbidden
            if (updateThemselves)
            {
                // Log user profile picture change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"User profile picture was updated from `{user.ProfilePicture}` to `{updatedUser.ProfilePicture}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });

                user.ProfilePicture = updatedUser.ProfilePicture;
            }
            else
            {
                return Forbid(
                    "You are trying to update a user profile picture which is not your own. Only a user can update their own profile picture!");
            }
        }

        // If roles is provided update it
        if (updatedUser.Roles != null && updatedUser.Roles != user.Roles.Select(r => r.Name).ToList())
        {
            // Log roles change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User roles was updated from `{string.Join("", user.Roles.Select(r => r.Name).ToList())}` to `{string.Join("", updatedUser.Roles)}` for the user `{user.DisplayName} ({user.JobTitle})` by `{requestingUserNameJob}`.",
                    UserID = requestingUserId, OrganizationID = organizationId
                });

            user.Roles = updatedUser.Roles.Select(r => new Database.Role { Name = r }).ToList();
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
        // Get user ID from claim
        string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;

        // If user ID is null then it does not exist in JWT so return a HTTP 400 error
        if (userId == null)
            return BadRequest("User ID can not be found in the JSON Web Token (JWT)!");

        // Get organization ID from claim
        string? organizationId = User.Claims.FirstOrDefault(c => c.Type == "org_id")?.Value;

        // If organization ID  is null then it does not exist in JWT so return a HTTP 400 error
        if (organizationId == null)
            return BadRequest("Organization ID can not be found in the JSON Web Token (JWT)!");

        // Fetch organization from the database by primary key
        Database.Organization? organization = await _dbContext.Organization.FindAsync(organizationId);

        if (organization == null)
            return NotFound($"A organization with the Auth0 Organization ID `{organizationId}` can not be found!");


        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Create the user based on the provided values with default settings
        Database.User userModel = new()
        {
            Auth0Id = userToAdd.Id,
            JobTitle = userToAdd.JobTitle,
            GivenName = userToAdd.GivenName,
            LastName = userToAdd.LastName,
            DisplayName = userToAdd.DisplayName,
            EmailAddress = userToAdd.EmailAddress,
            ProfilePicture = userToAdd.ProfilePicture,
            Organization = organization,
            Settings = new Database.UserSettings
                { Locale = "en-GB", DateFormat = "dddd dd MMMM yyyy", TimeFormat = "HH:SS", TimeZone = "GMT" }
        };

        // Add the user
        await _dbContext.User.AddAsync(userModel);

        // Add the users roles
        foreach (string role in userToAdd.Roles) userModel.Roles.Add(new Database.Role { Name = role });

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
            Id = _sqids.Encode(userModel.Id),
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

    // DELETE: /user/?
    [HttpDelete("{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "organization-administrator")]
    public async Task<IActionResult> DeleteUser(string userId)
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
        long requestingUserId= preflightResponse.Details .UserId;
        string requestingUserNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Convert user ID squid to raw user ID
        long rawUserId = _sqids.Decode(userId).Single();

        // Check user exists in organization
        if (_dbContext.User.FirstOrDefault(u => u.Id == rawUserId && u.Organization.Id == organizationId) == null)
            return Unauthorized(
                $"A user with the user ID {userId} does not exist in the organization with the Auth0 Organization ID {organizationId}!");

        // Fetch user from the database and include any related entities to also delete
        Database.User user = await _dbContext.User.Where(u => u.Id == rawUserId)
            .Include(u => u.Organization)
            .Include(u => u.Roles)
            .Include(u => u.Settings)
            .Include(u => u.Events)
            .FirstAsync();

        // Log removal of user
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"User `{user.DisplayName} ({user.JobTitle})` was deleted by `{requestingUserNameJob}`.",
                UserID = requestingUserId, OrganizationID = organizationId
            });

        // Remove the user
        _dbContext.User.Remove(user);

        // Save changes
        await _dbContext.SaveChangesAsync();

        return NoContent();
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
            .Select(u => new PreflightResponseDetails()
            {
                OrganizationId = u.Organization.Id,
                UserId = u.Id,
                UserNameJob = $"{u.DisplayName} {u.JobTitle}"
            }).SingleOrDefaultAsync();

        // If query result is null then the user does not exit in the organization so return a HTTP 404 error
        if (userQueryResult == null)
            return new PreflightResponse
            {
                Error = NotFound(
                    $"A user with the Auth0 user ID `{auth0UserId}` was not found in the organization with the Auth0 organization ID `{organizationId}`!")
            };

        return new PreflightResponse()
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