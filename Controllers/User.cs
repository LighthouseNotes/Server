using Meilisearch;
using Index = Meilisearch.Index;

namespace Server.Controllers;

[Route("/user")]
[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class UserController(DatabaseContext dbContext, IConfiguration configuration) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET: /users
    // Will return a list of all users & handles sort and search queries too
    [Route("/users")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<List<API.User>>> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery(Name = "sort")] string rawSort = "", string search = "")
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Create sort list
        List<string>? sortList = null;

        // If raw sort is specified then set sort list
        if (!string.IsNullOrWhiteSpace(rawSort)) sortList = rawSort.Split(',').ToList();

        // Set pagination headers
        HttpContext.Response.Headers.Append("X-Page", page.ToString());
        HttpContext.Response.Headers.Append("X-Per-Page", pageSize.ToString());

        // If a search query is provided then use Meilisearch to find all users that match the search query
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Create Meilisearch Client
            MeilisearchClient meiliClient =
                new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

            // Try getting the users index
            try
            {
                await meiliClient.GetIndexAsync("users");
            }
            // Catch Meilisearch exceptions
            catch (MeilisearchApiError e)
            {
                // Don't have an index to search so returning all users
                if (e.Code == "index_not_found")
                    return dbContext.User
                        .Skip((page - 1) * pageSize).Take(pageSize)
                        .Select(u => new API.User
                        {
                            EmailAddress = u.EmailAddress,
                            JobTitle = u.JobTitle,
                            GivenName = u.GivenName,
                            LastName = u.LastName,
                            DisplayName = u.DisplayName
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
                    AttributesToSearchOn = ["jobTitle", "displayName", "givenName", "lastName"], AttributesToRetrieve = ["emailAddress"]
                }
            );

            // Create a list of user email addresses that contain a match
            List<string> emailAddresses = searchResult.Hits.Select(u => u.EmailAddress).ToList()!;

            // Calculate the total number of results and pages
            HttpContext.Response.Headers.Append("X-Total-Count",
                dbContext.User.Count(u => emailAddresses.Contains(u.EmailAddress)).ToString());
            HttpContext.Response.Headers.Append("X-Total-Pages",
                ((dbContext.User.Count(u => emailAddresses.Contains(u.EmailAddress)) + pageSize - 1) / pageSize).ToString());

            return dbContext.User
                .Where(u => emailAddresses.Contains(u.EmailAddress))
                .Select(u => new API.User
                {
                    EmailAddress = u.EmailAddress,
                    JobTitle = u.JobTitle,
                    GivenName = u.GivenName,
                    LastName = u.LastName,
                    DisplayName = u.DisplayName
                })
                .ToList();
        }

        // Calculate the total number of results and pages - at this point we are either returning all users or sorting
        HttpContext.Response.Headers.Append("X-Total-Count", dbContext.User.Count().ToString());

        // If page size is 0 then list all users
        if (pageSize == 0)
        {
            HttpContext.Response.Headers.Append("X-Total-Pages", "1");

            // If no sort is provided or too many sort parameters are provided
            if (sortList == null || sortList.Count != 1)
                // Return all users
                return dbContext.User
                    .OrderBy(u => u.LastName)
                    .Select(u => new API.User
                    {
                        EmailAddress = u.EmailAddress,
                        JobTitle = u.JobTitle,
                        GivenName = u.GivenName,
                        LastName = u.LastName,
                        DisplayName = u.DisplayName
                    })
                    .ToList();

            string[] sortNoLimit = sortList[0].Split(" ");

            return sortNoLimit[1] switch
            {
                // Return all users
                "asc" => dbContext.User.OrderBy(u => EF.Property<object>(u, sortNoLimit[0]))
                    .Select(u => new API.User
                    {
                        EmailAddress = u.EmailAddress,
                        JobTitle = u.JobTitle,
                        GivenName = u.GivenName,
                        LastName = u.LastName,
                        DisplayName = u.DisplayName
                    })
                    .ToList(),
                // Return all users
                "desc" => dbContext.User.OrderBy(u => EF.Property<object>(u, sortNoLimit[0]))
                    .Select(u => new API.User
                    {
                        EmailAddress = u.EmailAddress,
                        JobTitle = u.JobTitle,
                        GivenName = u.GivenName,
                        LastName = u.LastName,
                        DisplayName = u.DisplayName
                    })
                    .ToList(),
                _ => BadRequest(
                    $"Did you understand if you want to sort {sortNoLimit[0]} ascending or descending. Use asc or desc to sort!")
            };
        }


        // If no sort is provided or too many sort parameters are provided
        if (sortList == null || sortList.Count != 1)
            // Return all users
            return dbContext.User
                .OrderBy(u => u.LastName)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(u => new API.User
                {
                    EmailAddress = u.EmailAddress,
                    JobTitle = u.JobTitle,
                    GivenName = u.GivenName,
                    LastName = u.LastName,
                    DisplayName = u.DisplayName
                })
                .ToList();

        string[] sort = sortList[0].Split(" ");

        switch (sort[1])
        {
            // Return ascending
            // Return all users
            case "asc":
                return dbContext.User
                    .OrderBy(u => EF.Property<object>(u, sort[0]))
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(u => new API.User
                    {
                        EmailAddress = u.EmailAddress,
                        JobTitle = u.JobTitle,
                        GivenName = u.GivenName,
                        LastName = u.LastName,
                        DisplayName = u.DisplayName
                    })
                    .ToList();
            // Return descending
            // Return all users
            case "desc":
                return dbContext.User
                    .OrderByDescending(u => EF.Property<object>(u, sort[0]))
                    .Skip((page - 1) * pageSize).Take(pageSize)
                    .Select(u => new API.User
                    {
                        EmailAddress = u.EmailAddress,
                        JobTitle = u.JobTitle,
                        GivenName = u.GivenName,
                        LastName = u.LastName,
                        DisplayName = u.DisplayName
                    })
                    .ToList();
            default:
                return BadRequest($"Did not understand if you want to sort {sort[0]} ascending or descending. Use asc or desc to sort!");
        }
    }

    // GET: /user/?
    // Will return details about the user using the email address provided in the path or if no email address is provided then the user making the request
    [HttpGet("{emailAddress?}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<API.User>> GetUser(string? emailAddress)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string requestingEmailAddress = preflightResponse.Details.EmailAddress;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", requestingEmailAddress);

        // If no user email address is specified then use the requesting user email address else use the email address provided
        emailAddress = string.IsNullOrEmpty(emailAddress) ? requestingEmailAddress : emailAddress;

        // Fetch the user based on the provided email address
        Database.User? user = dbContext.User.FirstOrDefault(u => u.EmailAddress == emailAddress);

        // If user does not exist return an HTTP 403 error
        if (user == null)
            return NotFound($"A user with the email address `{emailAddress}` was not found!");

        // Return the user
        return new API.User
        {
            EmailAddress = user.EmailAddress,
            JobTitle = user.JobTitle,
            GivenName = user.GivenName,
            LastName = user.LastName,
            DisplayName = user.DisplayName
        };
    }

    // PUT: /user
    // Will update the user making the request
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<IActionResult> UpdateUser(API.UpdateUser updatedUser)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null)
            return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);
        // Fetch the user
        Database.User? user = dbContext.User.FirstOrDefault(u => u.EmailAddress == emailAddress);

        // If user does not exist return an HTTP 403 error
        if (user == null) return NotFound($"A user with the email address `{emailAddress}` was not found!");

        // If job title is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.JobTitle) && updatedUser.JobTitle != user.JobTitle)
        {
            // Log user job title change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{user.DisplayName} ({updatedUser.JobTitle})` updated their job title from `{user.JobTitle}` to `{updatedUser.JobTitle}`.",
                    EmailAddress = emailAddress
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
                        $"`{user.DisplayName} ({user.JobTitle})` updated their given name from `{user.GivenName}` to `{updatedUser.GivenName}`.",
                    EmailAddress = emailAddress
                });


            user.GivenName = updatedUser.GivenName;
        }

        // If last name is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.LastName) && updatedUser.LastName != user.LastName)
        {
            // Log user given name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{user.DisplayName} ({user.JobTitle})` updated their last name from `{user.LastName}` to `{updatedUser.LastName}`.",
                    EmailAddress = emailAddress
                });


            user.LastName = updatedUser.LastName;
        }

        // If display name is provided updated it
        if (!string.IsNullOrWhiteSpace(updatedUser.DisplayName) && updatedUser.DisplayName != user.DisplayName)
        {
            // Log user display name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{updatedUser.DisplayName} ({user.JobTitle})` updated their display name from `{user.DisplayName}` to `{updatedUser.DisplayName}`.",
                    EmailAddress = emailAddress
                });

            user.DisplayName = updatedUser.DisplayName;
        }

        // Save changes to the database
        await dbContext.SaveChangesAsync();

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Try getting the users index
        try
        {
            await meiliClient.GetIndexAsync("users");
        }
        // Catch Meilisearch exceptions
        catch (MeilisearchApiError e)
        {
            // If error code is index_not_found create the index
            if (e.Code == "index_not_found") await meiliClient.CreateIndexAsync("users", "emailAddress");
        }

        // Get the users index
        Index index = meiliClient.Index("users");

        // Update Meilisearch document
        await index.UpdateDocumentsAsync([
            new Search.User
            {
                DisplayName = user.DisplayName, GivenName = user.GivenName, LastName = user.LastName, JobTitle = user.JobTitle
            }
        ], emailAddress);

        // Return no content
        return NoContent();
    }

    // POST: /user
    // Will create a user
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize]
    public async Task<ActionResult<API.User>> CreateUser(API.AddUser userDetails)
    {
        // Get user email address from claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email address is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return BadRequest("Email address can not be found in the JSON Web Token (JWT)!");

        // Create the user based on the provided values with default settings
        Database.User userModel = new()
        {
            JobTitle = userDetails.JobTitle,
            GivenName = userDetails.GivenName,
            LastName = userDetails.LastName,
            DisplayName = userDetails.DisplayName,
            EmailAddress = emailAddress,
            Settings = new Database.UserSettings
            {
                Locale = "en-GB", DateFormat = "dddd dd MMMM yyyy", TimeFormat = "HH:mm", TimeZone = "GMT"
            }
        };

        // Add the user
        await dbContext.User.AddAsync(userModel);

        // Attempt to save changes but handle errors gracefully
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            if (dbContext.User.Any(e => e.EmailAddress == userModel.EmailAddress))
                return Conflict($"A user with the email address `{userModel.EmailAddress}` already exists!");
            throw;
        }

        // Create response object
        API.User userResponseObject = new()
        {
            JobTitle = userModel.JobTitle,
            GivenName = userModel.GivenName,
            LastName = userModel.LastName,
            DisplayName = userModel.DisplayName,
            EmailAddress = userModel.EmailAddress
        };

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", userModel.EmailAddress);

        // Log the successful creation of a user
        await _auditContext.LogAsync("Lighthouse Notes",
            new { Action = $"User `{userModel.DisplayName} ({userModel.JobTitle})` added themselves`.", userModel.EmailAddress });

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Try getting the users index
        try
        {
            await meiliClient.GetIndexAsync("users");
        }
        // Catch Meilisearch exceptions
        catch (MeilisearchApiError e)
        {
            // If error code is index_not_found create the index
            if (e.Code == "index_not_found") await meiliClient.CreateIndexAsync("users", "emailAddress");
        }

        // Get the users index
        Index index = meiliClient.Index("users");

        // Add the new user to Meilisearch
        await index.AddDocumentsAsync([
            new Search.User
            {
                DisplayName = userModel.DisplayName,
                GivenName = userModel.GivenName,
                LastName = userModel.LastName,
                JobTitle = userModel.JobTitle
            }
        ], emailAddress);

        return CreatedAtAction(nameof(GetUser), new { userModel.EmailAddress }, userResponseObject);
    }

    private async Task<PreflightResponse> PreflightChecks()
    {
        // Get user email from JWT claim
        string? emailAddress = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;

        // If user email is null then it does not exist in JWT so return an HTTP 400 error
        if (string.IsNullOrEmpty(emailAddress))
            return new PreflightResponse { Error = BadRequest("Email address cannot be found in the JSON Web Token (JWT)!") };

        // Query user details including roles and application settings
        PreflightResponseDetails? preflightData = await dbContext.User
            .Where(u => u.EmailAddress == emailAddress)
            .Select(u => new PreflightResponseDetails { EmailAddress = u.EmailAddress })
            .SingleOrDefaultAsync();

        // If query result is null then the user does not exist
        if (preflightData == null)
            return new PreflightResponse { Error = NotFound($"A user with the email address: `{emailAddress}` was not found!") };

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
    }
}