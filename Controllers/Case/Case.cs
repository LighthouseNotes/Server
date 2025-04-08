// ReSharper disable InconsistentNaming

using Meilisearch;
using Index = Meilisearch.Index;

namespace Server.Controllers.Case;

[ApiController]
[Route("/case")]
[AuditApi(EventTypeName = "HTTP")]
public class CaseController(DatabaseContext dbContext, SqidsEncoder<long> sqids, IConfiguration configuration) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET: /cases
    [Route("/cases")]
    [HttpGet]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<API.Case>>> GetCases([FromQuery] int page = 1,
        [FromQuery] int pageSize = 10, [FromQuery(Name = "sort")] string rawSort = "", string search = "")
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null) return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Create sort list
        List<string>? sortList = null;

        // If raw sort is specified set sort list
        if (!string.IsNullOrWhiteSpace(rawSort)) sortList = rawSort.Split(',').ToList();

        // Set pagination headers
        HttpContext.Response.Headers.Append("X-Page", page.ToString());
        HttpContext.Response.Headers.Append("X-Per-Page", pageSize.ToString());

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Create Meilisearch Client
            MeilisearchClient meiliClient =
                new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

            // Get the cases index
            Index index = meiliClient.Index("cases");

            // Search the index for the string filtering by email address (so we know the user has access to the case)
            ISearchable<Search.Case> searchResult = await index.SearchAsync<Search.Case>(
                search,
                new SearchQuery
                {
                    AttributesToSearchOn =
                        ["DisplayId", "name", "displayName", "leadInvestigatorDisplayName", "leadInvestigatorGivenName", "leadInvestigatorLastName"],
                    AttributesToRetrieve = ["id"],
                    Filter = $"emailAddresses = '{emailAddress}'"
                }
            );

            // Create a list of case ids hat contain a match
            List<long> caseIds = searchResult.Hits.Select(c => c.Id).ToList();

            HttpContext.Response.Headers.Append("X-Total-Count",
                dbContext.Case.Count(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress) && caseIds.Contains(c.Id))
                    .ToString());
            HttpContext.Response.Headers.Append("X-Total-Pages",
                ((dbContext.Case.Count(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress) && caseIds.Contains(c.Id)) +
                    pageSize - 1) / pageSize).ToString());

            return dbContext.Case.Where(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress) && caseIds.Contains(c.Id))
                .OrderByDescending(c => c.Accessed)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).Select(c => new API.Case
                {
                    Id = sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    LeadInvestigator =
                        c.Users.Where(cu => cu.IsLeadInvestigator).Select(cu => new API.User
                        {
                            EmailAddress = cu.User.EmailAddress,
                            JobTitle = cu.User.JobTitle,
                            DisplayName = cu.User.DisplayName,
                            GivenName = cu.User.GivenName,
                            LastName = cu.User.LastName
                        }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        EmailAddress = cu.User.EmailAddress,
                        JobTitle = cu.User.JobTitle,
                        DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName
                    }).ToList()
                }).ToList();
        }

        HttpContext.Response.Headers.Append("X-Total-Count",
            dbContext.Case.Count(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress)).ToString());
        HttpContext.Response.Headers.Append("X-Total-Pages",
            ((dbContext.Case.Count(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress)) + pageSize - 1) / pageSize)
            .ToString());

        // If no sort is provided or too many sort parameters are provided, sort by accessed time
        if (sortList == null || sortList.Count != 1)
            return dbContext.Case.Where(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
                .OrderByDescending(c => c.Accessed)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).Select(c => new API.Case
                {
                    Id = sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    LeadInvestigator =
                        c.Users.Where(cu => cu.IsLeadInvestigator).Select(cu => new API.User
                        {
                            EmailAddress = cu.User.EmailAddress,
                            JobTitle = cu.User.JobTitle,
                            DisplayName = cu.User.DisplayName,
                            GivenName = cu.User.GivenName,
                            LastName = cu.User.LastName
                        }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        EmailAddress = cu.User.EmailAddress,
                        JobTitle = cu.User.JobTitle,
                        DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName
                    }).ToList()
                }).ToList();

        string[] sort = sortList[0].Split(" ");

        if (sort[1] == "asc")
            return dbContext.Case.Where(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
                .OrderBy(c => EF.Property<object>(c, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).Select(c => new API.Case
                {
                    Id = sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    LeadInvestigator =
                        c.Users.Where(cu => cu.IsLeadInvestigator).Select(cu => new API.User
                        {
                            EmailAddress = cu.User.EmailAddress,
                            JobTitle = cu.User.JobTitle,
                            DisplayName = cu.User.DisplayName,
                            GivenName = cu.User.GivenName,
                            LastName = cu.User.LastName
                        }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        EmailAddress = cu.User.EmailAddress,
                        JobTitle = cu.User.JobTitle,
                        DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName
                    }).ToList()
                }).ToList();

        if (sort[1] == "desc")
            return dbContext.Case.Where(c => c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
                .OrderByDescending(c => EF.Property<object>(c, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).Select(c => new API.Case
                {
                    Id = sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    LeadInvestigator =
                        c.Users.Where(cu => cu.IsLeadInvestigator).Select(cu => new API.User
                        {
                            EmailAddress = cu.User.EmailAddress,
                            JobTitle = cu.User.JobTitle,
                            DisplayName = cu.User.DisplayName,
                            GivenName = cu.User.GivenName,
                            LastName = cu.User.LastName
                        }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        EmailAddress = cu.User.EmailAddress,
                        JobTitle = cu.User.JobTitle,
                        DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName
                    }).ToList()
                }).ToList();

        return BadRequest(
            $"Did you understand if you want to sort {sort[0]} ascending or descending. Use asc or desc to sort!");
    }

    // GET: /case/?
    [HttpGet("{caseId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<API.Case>> GetCase(string caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks();

        // If preflight checks returned an HTTP error raise it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If preflight checks details are null return an HTTP 500 error
        if (preflightResponse.Details == null) return Problem("Preflight checks failed with an unknown error!");

        // Set variables from preflight response
        string emailAddress = preflightResponse.Details.EmailAddress;
        long rawCaseId = sqids.Decode(caseId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");

        // Update the access time
        sCase.Accessed = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();

        return new API.Case
        {
            Id = sqids.Encode(sCase.Id),
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            LeadInvestigator =
                sCase.Users.Where(cu => cu.IsLeadInvestigator).Select(cu => new API.User
                {
                    EmailAddress = cu.User.EmailAddress,
                    JobTitle = cu.User.JobTitle,
                    DisplayName = cu.User.DisplayName,
                    GivenName = cu.User.GivenName,
                    LastName = cu.User.LastName
                }).Single(),
            Modified = TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Accessed = TimeZoneInfo.ConvertTimeFromUtc(sCase.Accessed, timeZone),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                EmailAddress = cu.User.EmailAddress,
                JobTitle = cu.User.JobTitle,
                DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName,
                LastName = cu.User.LastName
            }).ToList()
        };
    }

    // PUT: /case/?
    [HttpPut("{caseId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult> UpdateCase(string caseId, API.UpdateCase updatedCase)
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
        string emailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case from the database including the required entities
        Database.Case? sCase = await dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.EmailAddress == emailAddress))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");

        if (updatedCase.Name != null && updatedCase.Name != sCase.Name)
        {
            // Log case name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case name was updated from `{sCase.Name}` to `{updatedCase.Name}` for the case with the ID `{sCase.DisplayId}` by `{userNameJob}`.",
                    EmailAddress = emailAddress
                });

            // Update case name in the database
            sCase.Name = updatedCase.Name;

            // Update display name
            sCase.DisplayName = $"{sCase.DisplayId} {sCase.Name}";
        }

        if (updatedCase.DisplayId != null && updatedCase.DisplayId != sCase.DisplayId)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case ID was updated from `{sCase.DisplayId}` to `{updatedCase.DisplayId}` for the case with the name `{sCase.Name}` by `{userNameJob}`.",
                    EmailAddress = emailAddress
                });

            // Update case ID in the database
            sCase.DisplayId = updatedCase.DisplayId;

            // Update display name
            sCase.DisplayName = $"{sCase.DisplayId} {sCase.Name}";
        }

        if (updatedCase.Status != null && updatedCase.Status != sCase.Status)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case status was updated from `{sCase.Status}` to `{updatedCase.Status}`  for the case `{sCase.DisplayName}` by `{userNameJob}`.",
                    EmailAddress = emailAddress
                });

            // Update case status in the database
            sCase.Status = updatedCase.Status;
        }

        // If a new LeadInvestigator user is provided then remove the current LeadInvestigator and update
        if (updatedCase.LeadInvestigatorEmailAddress != null && updatedCase.LeadInvestigatorEmailAddress !=
            sCase.Users.SingleOrDefault(cu => cu.IsLeadInvestigator)!.User.EmailAddress)
        {
            // Check new LeadInvestigator user exists
            Database.User? newLeadInvestigator =
                await dbContext.User.Where(u => u.EmailAddress == updatedCase.LeadInvestigatorEmailAddress).FirstOrDefaultAsync();

            if (newLeadInvestigator == null)
                return Unauthorized(
                    $"A user with the ID `{updatedCase.LeadInvestigatorEmailAddress}` was not found!");

            // Remove current LeadInvestigator users access
            Database.CaseUser oldLeadInvestigator = sCase.Users.Single(cu => cu.IsLeadInvestigator);

            // Log removal of LeadInvestigator user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `LeadInvestigator {oldLeadInvestigator.User.DisplayName}  ({oldLeadInvestigator.User.JobTitle})` was removed from the case `{sCase.DisplayName}` by `{userNameJob}`.",
                    EmailAddress = emailAddress
                });

            // Set old LeadInvestigator user to false
            oldLeadInvestigator.IsLeadInvestigator = false;

            // Log new LeadInvestigator user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `LeadInvestigator {newLeadInvestigator.DisplayName} {newLeadInvestigator.JobTitle}` is now the LeadInvestigator for the case `{sCase.DisplayName}` this change was made by `{userNameJob}`.",
                    EmailAddress = emailAddress
                });

            // If new sio does not already have access to the case, add them as the LeadInvestigator
            if (sCase.Users.Any(cu => cu.User.EmailAddress == newLeadInvestigator.EmailAddress) == false)
            {
                await dbContext.CaseUser.AddAsync(new Database.CaseUser
                {
                    Case = sCase, User = newLeadInvestigator, IsLeadInvestigator = true
                });

                // Log the LeadInvestigator user given access
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $" `LeadInvestigator {newLeadInvestigator.DisplayName} ({newLeadInvestigator.JobTitle})` was given access to the case `{sCase.DisplayName}` by `{userNameJob}`.",
                        EmailAddress = emailAddress
                    });
            }
            // Else the user already has access to the case so just set IsLeadInvestigator to true
            else
            {
                // Set new LeadInvestigator user to true
                sCase.Users.Single(cu => cu.User.EmailAddress == newLeadInvestigator.EmailAddress).IsLeadInvestigator = true;
            }
        }

        // Save changes to the database
        await dbContext.SaveChangesAsync();

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
    [Authorize]
    public async Task<ActionResult<API.Case>> CreateCase(API.AddCase caseAddObject)
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
        string emailAddress = preflightResponse.Details.EmailAddress;
        string userNameJob = preflightResponse.Details.UserNameJob;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // If no lead investigator is provided then the user making the request is the lead investigator
        string leadInvestigatorEmailAddress = caseAddObject.LeadInvestigatorEmailAddress ?? emailAddress;

        // Get LeadInvestigator user from the database
        Database.User? LeadInvestigatorUser = await dbContext.User
            .FirstOrDefaultAsync(u => u.EmailAddress == leadInvestigatorEmailAddress);

        if (LeadInvestigatorUser == null)
            return Unauthorized(
                $"A user with the ID `{caseAddObject.LeadInvestigatorEmailAddress}` was not found!");

        // Create the case
        Database.Case sCase = new()
        {
            DisplayId = caseAddObject.DisplayId,
            Name = caseAddObject.Name,
            DisplayName = $"{caseAddObject.DisplayId} {caseAddObject.Name}",
            Status = "Open",
            Accessed = DateTime.MinValue
        };

        // Give the LeadInvestigator user access to the case and make them LeadInvestigator
        sCase.Users.Add(new Database.CaseUser { User = LeadInvestigatorUser, IsLeadInvestigator = true });

        // Log creation of case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"The case `{sCase.DisplayName}` was created by `{userNameJob}`.",
                EmailAddress = emailAddress
            });

        // Add the case to the database
        dbContext.Case.Add(sCase);

        // Add users to case
        if (caseAddObject.EmailAddresses != null)
            foreach (string userToAddEmailAddress in caseAddObject.EmailAddresses)
            {
                // If the user is trying to add the LeadInvestigator user to the case - skip as the LeadInvestigator user has already been added
                if (userToAddEmailAddress == LeadInvestigatorUser.EmailAddress) continue;

                // Fetch user to add from the database
                Database.User? userToAdd = await dbContext.User
                    .FirstOrDefaultAsync(u => u.EmailAddress == userToAddEmailAddress);

                // If user can not be found then return an HTTP 404
                if (userToAdd == null)
                    return
                        NotFound($"A user with the ID `{userToAddEmailAddress}` was not found!");

                // Add user to case
                sCase.Users.Add(new Database.CaseUser { User = userToAdd });

                // Log addition of user
                await _auditContext.LogAsync("Lighthouse Notes",
                    new
                    {
                        Action =
                            $"`{userToAdd.DisplayName} ({userToAdd.JobTitle})` was added to the case `{sCase.DisplayName}` by `{userNameJob}`.",
                        EmailAddress = emailAddress
                    });
            }

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Save changes to the database
        await dbContext.SaveChangesAsync();

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Try getting the contemporaneous-notes index
        try
        {
            await meiliClient.GetIndexAsync("cases");
        }
        // Catch Meilisearch exceptions
        catch (MeilisearchApiError e)
        {
            // If error code is index_not_found create the index
            if (e.Code == "index_not_found")
            {
                await meiliClient.CreateIndexAsync("cases", "id");
                await meiliClient.Index("cases").UpdateFilterableAttributesAsync(["emailAddresses"]);
            }
        }

        // Get the cases index
        Index index = meiliClient.Index("cases");

        await index.AddDocumentsAsync([
            new Search.Case
            {
                Id = sCase.Id,
                EmailAddresses = sCase.Users.Select(u => u.User.EmailAddress).ToList(),
                DisplayId = sCase.DisplayId,
                DisplayName = sCase.DisplayName,
                Name = sCase.Name,
                LeadInvestigatorDisplayName = LeadInvestigatorUser.DisplayName,
                LeadInvestigatorGivenName = LeadInvestigatorUser.GivenName,
                LeadInvestigatorLastName = LeadInvestigatorUser.LastName
            }
        ]);

        // Return the created case
        return new API.Case
        {
            Id = sqids.Encode(sCase.Id),
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            LeadInvestigator =
                new API.User
                {
                    EmailAddress = LeadInvestigatorUser.EmailAddress,
                    JobTitle = LeadInvestigatorUser.JobTitle,
                    DisplayName = LeadInvestigatorUser.DisplayName,
                    GivenName = LeadInvestigatorUser.GivenName,
                    LastName = LeadInvestigatorUser.LastName
                },
            Modified = TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Accessed = TimeZoneInfo.ConvertTimeFromUtc(sCase.Accessed, timeZone),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                EmailAddress = LeadInvestigatorUser.EmailAddress,
                JobTitle = cu.User.JobTitle,
                DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName,
                LastName = cu.User.LastName
            }).ToList()
        };
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
            .Include(u => u.Settings)
            .Select(u => new PreflightResponseDetails
            {
                EmailAddress = u.EmailAddress, UserNameJob = $"{u.DisplayName} ({u.JobTitle})", UserSettings = u.Settings
            })
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
        public required Database.UserSettings UserSettings { get; init; }
    }
}