// ReSharper disable InconsistentNaming

namespace Server.Controllers.Case;

[ApiController]
[Route("/case")]
[AuditApi(EventTypeName = "HTTP")]
public class CaseController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly SqidsEncoder<long> _sqids;

    public CaseController(DatabaseContext dbContext, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _sqids = sqids;
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
    public async Task<ActionResult<IEnumerable<API.Case>>> GetCases([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery (Name = "sort")] string rawSort = "")
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
        long userId = preflightResponse.Details.UserId;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);
        
        // Create sort list
        List<string>? sortList = null;
        
        // If raw sort is specified set sort list
        if (!string.IsNullOrWhiteSpace(rawSort))
        {
            sortList = rawSort.Split(',').ToList();
        }

        // Set pagination headers
        HttpContext.Response.Headers.Add("X-Page", page.ToString());
        HttpContext.Response.Headers.Add("X-Per-Page", pageSize.ToString());
        HttpContext.Response.Headers.Add("X-Total-Count", _dbContext.Case.Count(c => c.Users.Any(cu => cu.User.Id == userId)).ToString());
        HttpContext.Response.Headers.Add("X-Total-Pages", ((_dbContext.Case.Count(c => c.Users.Any(cu => cu.User.Id == userId)) + pageSize - 1 ) / pageSize ).ToString());
        
        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);
        
        // If no sort is provided or two many sort parameters are provided, sort by accessed time
        if (sortList == null || sortList.Count != 1)
            return _dbContext.Case.Where(c => c.Users.Any(cu => cu.User.Id == userId))
                .OrderByDescending(c => c.Accessed)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).ThenInclude(u => u.Roles).Select(c => new API.Case
                {
                    Id = _sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    SIO = c.Users.Where(cu => cu.IsSIO).Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).ToList()
                }).ToList();
        
        string[] sort = sortList[0].Split(" ");
        
        if (sort[1] == "asc")
        {
            return _dbContext.Case.Where(c => c.Users.Any(cu => cu.User.Id == userId))
                .OrderBy(c =>  EF.Property<object>(c, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).ThenInclude(u => u.Roles).Select(c => new API.Case
                {
                    Id = _sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    SIO = c.Users.Where(cu => cu.IsSIO).Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).ToList()
                }).ToList();
        }
        
        if(sort[1] == "desc")
        {
            return _dbContext.Case.Where(c => c.Users.Any(cu => cu.User.Id == userId))
                .OrderByDescending(c =>  EF.Property<object>(c, sort[0]))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Include(c => c.Users).ThenInclude(cu => cu.User).ThenInclude(u => u.Roles).Select(c => new API.Case
                {
                    Id = _sqids.Encode(c.Id),
                    DisplayId = c.DisplayId,
                    Name = c.Name,
                    DisplayName = c.DisplayName,
                    SIO = c.Users.Where(cu => cu.IsSIO).Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName,
                        LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).Single(),
                    Modified = TimeZoneInfo.ConvertTimeFromUtc(c.Modified, timeZone),
                    Accessed = TimeZoneInfo.ConvertTimeFromUtc(c.Accessed, timeZone),
                    Created = TimeZoneInfo.ConvertTimeFromUtc(c.Created, timeZone),
                    Status = c.Status,
                    Users = c.Users.Select(cu => new API.User
                    {
                        Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                        JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                        GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                        ProfilePicture = cu.User.ProfilePicture,
                        Organization = new API.Organization
                            { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                        Roles = cu.User.Roles.Select(r => r.Name).ToList()
                    }).ToList()
                }).ToList();
        }

        return BadRequest($"Did you understand if you want to sort {sort[0]} ascending or descending. Use asc or desc to sort!");
        
    }

    // GET: /case/?
    [HttpGet("{caseId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Case>> GetCase(string caseId)
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
        long userId = preflightResponse.Details.UserId;
        long rawCaseId = _sqids.Decode(caseId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Organization)
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Roles)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Return requested case details
        
        // Update the access time
        sCase.Accessed = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        
        return new API.Case
        {
            Id = _sqids.Encode(sCase.Id),
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            SIO = sCase.Users.Where(cu => cu.IsSIO).Select(cu => new API.User
            {
                Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName,
                LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).Single(),
            Modified = TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Accessed = TimeZoneInfo.ConvertTimeFromUtc(sCase.Accessed, timeZone),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
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
    [Authorize(Roles = "organization-administrator")]
    public async Task<ActionResult> UpdateCase(string caseId, API.UpdateCase updatedCase)
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
        long userId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = _sqids.Decode(caseId)[0];

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == userId))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        // Update values in the database if the provided values are not null
        if (updatedCase.Name != null && updatedCase.Name != sCase.Name)
        {
            // Log case name change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case name was updated from `{sCase.Name}` to `{updatedCase.Name}` for the case with the ID `{sCase.DisplayId}` by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            // Update case name in the database
            sCase.Name = updatedCase.Name;
        }

        if (updatedCase.DisplayId != null && updatedCase.DisplayId != sCase.DisplayId)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case ID was updated from `{sCase.DisplayId}` to `{updatedCase.DisplayId}` for the case with the name `{sCase.Name}` by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            // Update case ID in the database
            sCase.DisplayId = updatedCase.DisplayId;
        }

        if (updatedCase.Status != null && updatedCase.Status != sCase.Status)
        {
            // Log case ID change
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"Case status was updated from `{sCase.Status}` to `{updatedCase.Status}`  for the case `{sCase.DisplayName}` by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            // Update case status in the database
            sCase.Status = updatedCase.Status;
        }

        // If a new SIO user is provided then remove the current SIO and update
        if (updatedCase.SIOUserId != null && _sqids.Decode(updatedCase.SIOUserId)[0] !=
            sCase.Users.SingleOrDefault(cu => cu.IsSIO)!.User.Id)
        {
            // Get SIO user ID from squid 
            long rawSIOUserId = _sqids.Decode(updatedCase.SIOUserId)[0];

            // Check new SIO user exists 
            Database.User? newSIO =
                await _dbContext.User.Where(u => u.Id == rawSIOUserId).FirstOrDefaultAsync();

            if (newSIO == null)
                return Unauthorized(
                    $"A user with the ID `{updatedCase.SIOUserId}` was not found in the organization with the ID `{organizationId}`!");

            // Remove current SIO users access
            Database.CaseUser oldSIO = sCase.Users.Single(cu => cu.IsSIO);

            // Log removal of SIO user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {oldSIO.User.DisplayName}  ({oldSIO.User.JobTitle})` was removed from the case `{sCase.DisplayName}` by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            // Set old SIO user to false
            oldSIO.IsSIO = false;

            // Log new SIO user
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {newSIO.DisplayName} {newSIO.JobTitle}` is now the SIO for the case `{sCase.DisplayName}` this change was made by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
                });

            // Set new SIO user to true
            sCase.Users.Single(cu => cu.User.Id == newSIO.Id).IsSIO = true;

            // Log the SIO user given access
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $" `SIO {newSIO.DisplayName} ({newSIO.JobTitle})` was given access to the case `{sCase.DisplayName}` by `{userNameJob}`.",
                    UserID = userId, OrganizationID = organizationId
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
        long userId = preflightResponse.Details.UserId;
        string userNameJob = preflightResponse.Details.UserNameJob;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's organization ID and the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("OrganizationID", organizationId);
        auditScope.SetCustomField("UserID", userId);


        long rawSIOUserId;
        // If no SIO is provided then the user making the request is the SIO
        if (caseAddObject.SIOUserId == null)
        {
            rawSIOUserId = userId;
        }
        else
        {
            // Get SIO user ID from squid
            rawSIOUserId = _sqids.Decode(caseAddObject.SIOUserId)[0];

            // If the user is an SIO and is trying to create an case for another user return a HTTP 401 error
            if (!User.IsInRole("organization-administrator") && rawSIOUserId != userId)
                return Unauthorized("As you hold an SIO role you can only create cases where you are the SIO!");
        }

        // Get SIO user from the database
        Database.User? SIOUser = await _dbContext.User
            .Include(u => u.Roles)
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == rawSIOUserId && u.Organization.Id == organizationId);

        if (SIOUser == null)
            return Unauthorized(
                $"A user with the ID `{caseAddObject.SIOUserId}` was not found in the organization with the ID `{organizationId}`!");

        // Create the case
        Database.Case sCase = new()
        {
            DisplayId = caseAddObject.DisplayId,
            Name = caseAddObject.Name,
            DisplayName = $"{caseAddObject.DisplayId} {caseAddObject.Name}",
            Status = "Open",
            Accessed = DateTime.MinValue
        };

        // Give the SIO user access to the case and make them SIO
        sCase.Users.Add(new Database.CaseUser
        {
            User = SIOUser,
            IsSIO = true
        });

        // Log creation of case
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"The case `{sCase.DisplayName}` was created by `{userNameJob}`.",
                UserID = userId, OrganizationID = organizationId
            });

        // Add the case to the database
        _dbContext.Case.Add(sCase);

        // Add users to case
        if (caseAddObject.UserIds != null)
            foreach (string userToAddId in caseAddObject.UserIds)
            {
                // Get user ID from squid 
                long rawUserId = _sqids.Decode(userToAddId)[0];

                // If the user is trying to add the SIO user to the case - skip as the SIO user has already been added
                if (rawUserId == SIOUser.Id) continue;

                // Fetch user to add from the database
                Database.User? userToAdd = await _dbContext.User
                    .FirstOrDefaultAsync(u => u.Id == rawUserId && u.Organization.Id == organizationId);

                // If user can not be found then return a HTTP 404
                if (userToAdd == null)
                    return
                        NotFound(
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
                            $"`{userToAdd.DisplayName} ({userToAdd.JobTitle})` was added to the case `{sCase.DisplayName}` by `{userNameJob}`.",
                        UserID = userId, OrganizationID = organizationId
                    });
            }

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        // Explicitly load roles for each user
        foreach (Database.CaseUser caseUser in sCase.Users)
            await _dbContext.Entry(caseUser.User)
                .Collection(u => u.Roles)
                .LoadAsync();

        // Return the created case
        return new API.Case
        {
            Id = _sqids.Encode(sCase.Id),
            DisplayId = sCase.DisplayId,
            Name = sCase.Name,
            DisplayName = sCase.DisplayName,
            SIO = new API.User
            {
                Id = _sqids.Encode(SIOUser.Id), Auth0Id = SIOUser.Auth0Id,
                JobTitle = SIOUser.JobTitle, DisplayName = SIOUser.DisplayName,
                GivenName = SIOUser.GivenName,
                LastName = SIOUser.LastName, EmailAddress = SIOUser.EmailAddress,
                ProfilePicture = SIOUser.ProfilePicture,
                Organization = new API.Organization
                    { Name = SIOUser.Organization.DisplayName, DisplayName = SIOUser.Organization.DisplayName },
                Roles = SIOUser.Roles.Select(r => r.Name).ToList()
            },
            Modified = TimeZoneInfo.ConvertTimeFromUtc(sCase.Modified, timeZone),
            Accessed = TimeZoneInfo.ConvertTimeFromUtc(sCase.Accessed, timeZone),
            Created = TimeZoneInfo.ConvertTimeFromUtc(sCase.Created, timeZone),
            Status = sCase.Status,
            Users = sCase.Users.Select(cu => new API.User
            {
                Id = _sqids.Encode(cu.User.Id), Auth0Id = cu.User.Auth0Id,
                JobTitle = cu.User.JobTitle, DisplayName = cu.User.DisplayName,
                GivenName = cu.User.GivenName, LastName = cu.User.LastName, EmailAddress = cu.User.EmailAddress,
                ProfilePicture = cu.User.ProfilePicture,
                Organization = new API.Organization
                    { Name = cu.User.Organization.DisplayName, DisplayName = cu.User.Organization.DisplayName },
                Roles = cu.User.Roles.Select(r => r.Name).ToList()
            }).ToList()
        };
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
                UserNameJob = $"{u.DisplayName} ({u.JobTitle})",
                UserSettings = u.Settings
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
        public required Database.UserSettings UserSettings { get; init; }
    }
}