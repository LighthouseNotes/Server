using Meilisearch;
using Index = Meilisearch.Index;

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
    public async Task<ActionResult<List<API.User>>> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery(Name = "sort")] string rawSort = "", bool sio = false, string search = "")
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
        Database.OrganizationSettings organizationSettings = preflightResponse.Details.OrganizationSettings;
        long userId = preflightResponse.Details.UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Create sort list
        List<string>? sortList = null;

        // If raw sort is specified set sort list
        if (!string.IsNullOrWhiteSpace(rawSort)) sortList = rawSort.Split(',').ToList();

        // Set pagination headers
        HttpContext.Response.Headers.Add("X-Page", page.ToString());
        HttpContext.Response.Headers.Add("X-Per-Page", pageSize.ToString());

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Create Meilisearch Client
            MeilisearchClient meiliClient =
                new(organizationSettings.MeilisearchUrl, organizationSettings.MeilisearchApiKey);

            // Try getting the users index
            try
            {
                await meiliClient.GetIndexAsync("users");
            }
            // Catch Meilisearch exceptions
            catch (MeilisearchApiError e)
            {
                // Don't have an index to search so returning all users in a organization
                if (e.Code == "index_not_found")
                    return _dbContext.Organization
                        .Where(o => o.Id == organizationId)
                        .SelectMany(org => org.Users)
                        .Skip((page - 1) * pageSize).Take(pageSize)
                        .Include(u => u.Roles)
                        .Select(u => new API.User
                        {
                            Id = _sqids.Encode(u.Id),
                            Auth0Id = u.Auth0Id,
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

            // Get the user index
            Index index = meiliClient.Index("users");

            // Search the index for the string
            ISearchable<Search.User> searchResult = await index.SearchAsync<Search.User>(
                search,
                new SearchQuery
                {
                    AttributesToSearchOn = new[] { "jobTitle", "displayName", "givenName", "lastName" },
                    AttributesToRetrieve = new[] { "id" }
                }
            );

            // Create a list of user Ids that contain a match 
            List<long> userIds = searchResult.Hits.Select(u => u.Id).ToList();

            HttpContext.Response.Headers.Add("X-Total-Count",
                _dbContext.User.Count(u => u.Organization.Id == organizationId && userIds.Contains(u.Id)).ToString());
            HttpContext.Response.Headers.Add("X-Total-Pages",
                ((_dbContext.User.Count(u => u.Organization.Id == organizationId && userIds.Contains(u.Id)) + pageSize -
                  1) / pageSize).ToString());

            return _dbContext.User
                .Where(u => u.Organization.Id == organizationId && userIds.Contains(u.Id))
                .Include(u => u.Roles)
                .Include(u => u.Organization)
                .Select(u => new API.User
                {
                    Id = _sqids.Encode(u.Id),
                    Auth0Id = u.Auth0Id,
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

        HttpContext.Response.Headers.Add("X-Total-Count",
            _dbContext.User.Count(u => u.Organization.Id == organizationId).ToString());

        // If SIO is set to true only return the SIO users
        if (sio)
        {
            HttpContext.Response.Headers.Add("X-Total-Pages", "1");

            return _dbContext.Organization
                .Where(o => o.Id == organizationId)
                .SelectMany(org => org.Users)
                .Include(u => u.Roles)
                .Where(u => u.Roles.Any(r => r.Name == "sio"))
                .Select(u => new API.User
                {
                    Id = _sqids.Encode(u.Id),
                    Auth0Id = u.Auth0Id,
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

        // If page size is 0 then list all users
        if (pageSize == 0)
        {
            HttpContext.Response.Headers.Add("X-Total-Pages", "1");
            // If no sort is provided or two many sort parameters are provided
            if (sortList == null || sortList.Count != 1)
                // Return all users in a organization
                return _dbContext.Organization
                    .Where(o => o.Id == organizationId)
                    .SelectMany(org => org.Users)
                    .OrderBy(u => u.LastName)
                    .Include(u => u.Roles)
                    .Select(u => new API.User
                    {
                        Id = _sqids.Encode(u.Id),
                        Auth0Id = u.Auth0Id,
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

            string[] sortNoLimit = sortList[0].Split(" ");

            if (sortNoLimit[1] == "asc")
                // Return all users in a organization
                return _dbContext.Organization
                    .Where(o => o.Id == organizationId)
                    .SelectMany(org => org.Users)
                    .OrderBy(u => EF.Property<object>(u, sortNoLimit[0]))
                    .Include(u => u.Roles)
                    .Select(u => new API.User
                    {
                        Id = _sqids.Encode(u.Id),
                        Auth0Id = u.Auth0Id,
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

            if (sortNoLimit[1] == "desc")
                // Return all users in a organization
                return _dbContext.Organization
                    .Where(o => o.Id == organizationId)
                    .SelectMany(org => org.Users)
                    .OrderBy(u => EF.Property<object>(u, sortNoLimit[0]))
                    .Include(u => u.Roles)
                    .Select(u => new API.User
                    {
                        Id = _sqids.Encode(u.Id),
                        Auth0Id = u.Auth0Id,
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

            return BadRequest(
                $"Did you understand if you want to sort {sortNoLimit[0]} ascending or descending. Use asc or desc to sort!");
        }

        // Calculate page size and set header
        HttpContext.Response.Headers.Add("X-Total-Pages",
            ((_dbContext.User.Count(u => u.Organization.Id == organizationId) + pageSize - 1) / pageSize).ToString());

        // If no sort is provided or two many sort parameters are provided
        if (sortList == null || sortList.Count != 1)
            // Return all users in a organization
            return _dbContext.Organization
                .Where(o => o.Id == organizationId)
                .SelectMany(org => org.Users)
                .OrderBy(u => u.LastName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(u => u.Roles)
                .Select(u => new API.User
                {
                    Id = _sqids.Encode(u.Id),
                    Auth0Id = u.Auth0Id,
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

        string[] sort = sortList[0].Split(" ");

        if (sort[1] == "asc")
            // Return all users in a organization
            return _dbContext.Organization
                .Where(o => o.Id == organizationId)
                .SelectMany(org => org.Users)
                .OrderBy(u => EF.Property<object>(u, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(u => u.Roles)
                .Select(u => new API.User
                {
                    Id = _sqids.Encode(u.Id),
                    Auth0Id = u.Auth0Id,
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

        if (sort[1] == "desc")
            // Return all users in a organization
            return _dbContext.Organization
                .Where(o => o.Id == organizationId)
                .SelectMany(org => org.Users)
                .OrderByDescending(u => EF.Property<object>(u, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(u => u.Roles)
                .Select(u => new API.User
                {
                    Id = _sqids.Encode(u.Id),
                    Auth0Id = u.Auth0Id,
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

        return BadRequest(
            $"Did you understand if you want to sort {sort[0]} ascending or descending. Use asc or desc to sort!");
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
        long requestingUserId = preflightResponse.Details.UserId;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        // If no user ID is specified then use the requesting user ID else convert the provided user ID squid to the user's ID
        long rawUserId = string.IsNullOrEmpty(userId) ? requestingUserId : _sqids.Decode(userId).Single();

        // Fetch the user based on the provide user ID
        Database.User? user =
            _dbContext.User.FirstOrDefault(u => u.Id == rawUserId && u.Organization.Id == organizationId);

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
            Auth0Id = user.Auth0Id,
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
        Database.OrganizationSettings organizationSettings = preflightResponse.Details.OrganizationSettings;
        long requestingUserId = preflightResponse.Details.UserId;
        string requestingUserNameJob = preflightResponse.Details.UserNameJob;
        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

        bool updateThemselves = false;
        long rawUserId;

        // If user ID is not provided then set userID to requesting user Dd and set update themselves to true
        if (string.IsNullOrEmpty(userId))
        {
            rawUserId = requestingUserId;
            updateThemselves = true;
        }
        // Else user ID is provided so decode squid to raw user Id
        else
        {
            // Convert the provided user ID squid to the raw user ID
            rawUserId = _sqids.Decode(userId).Single();

            // If the provided user ID is equal to requesting users ID then the user is updating themselves
            if (rawUserId == requestingUserId) updateThemselves = true;
        }

        // Fetch the user based on the provide user ID
        Database.User? user = _dbContext.User.Include(u => u.Roles)
            .FirstOrDefault(u => u.Id == rawUserId && u.Organization.Id == organizationId);

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
                            $"`{user.DisplayName} ({updatedUser.JobTitle})` updated their job title from `{user.JobTitle}` to `{updatedUser.JobTitle}`.",
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
            // If the user is updating themselves display a slightly different log message 
            if (updateThemselves)
                // Log user given name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"`{user.DisplayName} ({user.JobTitle})` updated their given name from `{user.GivenName}` to `{updatedUser.GivenName}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });
            else
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
            // If the user is updating themselves display a slightly different log message 
            if (updateThemselves)
                // Log user given name change
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"`{user.DisplayName} ({user.JobTitle})` updated their last name from `{user.LastName}` to `{updatedUser.LastName}`.",
                        UserID = requestingUserId, OrganizationID = organizationId
                    });
            else
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
                            $"`{updatedUser.DisplayName} ({user.JobTitle})` updated their display name from `{user.DisplayName}` to `{updatedUser.DisplayName}`.",
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
                            $"`{user.DisplayName} ({user.JobTitle})` updated their profile picture from `{user.ProfilePicture}` to `{updatedUser.ProfilePicture}`.",
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

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(organizationSettings.MeilisearchUrl, organizationSettings.MeilisearchApiKey);

        // Try getting the users index
        try
        {
            await meiliClient.GetIndexAsync("users");
        }
        // Catch Meilisearch exceptions
        catch (MeilisearchApiError e)
        {
            // If error code is index_not_found create the index
            if (e.Code == "index_not_found") await meiliClient.CreateIndexAsync("users", "id");
        }

        // Get the users index
        Index index = meiliClient.Index("users");

        // Update Meilisearch document
        await index.UpdateDocumentsAsync(new[]
        {
            new Search.User()
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                GivenName = user.GivenName,
                LastName = user.LastName,
                JobTitle = user.JobTitle
            }
        });

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

        // Load organization settings
        await _dbContext.Entry(organization).Reference(o => o.Settings).LoadAsync();

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
                { Locale = "en-GB", DateFormat = "dddd dd MMMM yyyy", TimeFormat = "HH:mm", TimeZone = "GMT" }
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
            Auth0Id = userModel.Auth0Id,
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

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userModel.Id);

        // Log the successful creation of a user
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"User `{userModel.DisplayName} ({userModel.JobTitle})` added themselves to {organization.DisplayName}`.",
                UserID = userModel.Id, OrganizationID = organizationId
            });

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(organization.Settings.MeilisearchUrl, organization.Settings.MeilisearchApiKey);

        // Try getting the users index
        try
        {
            await meiliClient.GetIndexAsync("users");
        }
        // Catch Meilisearch exceptions
        catch (MeilisearchApiError e)
        {
            // If error code is index_not_found create the index
            if (e.Code == "index_not_found") await meiliClient.CreateIndexAsync("users", "id");
        }

        // Get the users index
        Index index = meiliClient.Index("users");

        // Update Meilisearch document
        await index.AddDocumentsAsync(new[]
        {
            new Search.User()
            {
                Id = userModel.Id,
                DisplayName = userModel.DisplayName,
                GivenName = userModel.GivenName,
                LastName = userModel.LastName,
                JobTitle = userModel.JobTitle
            }
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
        long requestingUserId = preflightResponse.Details.UserId;
        string requestingUserNameJob = preflightResponse.Details.UserNameJob;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", requestingUserId);

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
            .Select(u => new PreflightResponseDetails
            {
                OrganizationId = u.Organization.Id,
                OrganizationSettings = u.Organization.Settings,
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
        public required Database.OrganizationSettings OrganizationSettings { get; set; }
        public long UserId { get; init; }
        public required string UserNameJob { get; init; }
    }
}