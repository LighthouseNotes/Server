using Minio;
using Minio.DataModel.Args;

namespace Server.Controllers.Case;

[ApiController]
[Route("/case/{caseId}/export")]
[AuditApi(EventTypeName = "HTTP")]
public class ExportController(DatabaseContext dbContext, SqidsEncoder<long> sqids, IConfiguration configuration) : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET: /case/?/export
    // Return a model containing everything
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<API.Export>> ExportContemporaneousNotes(string caseId)
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
        string userNameJob = preflightResponse.Details.UserNameJob;
        long rawCaseId = sqids.Decode(caseId).Single();
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
            .Include(c => c.SharedHashes)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .SingleOrDefaultAsync();

        // If case does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.ContemporaneousNotes)
            .Include(cu => cu.Tabs)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        // The case might not exist or the user does not have access to the case
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        //////////////////
        // Case details //
        //////////////////
        Database.User sioUser = sCase.Users.First(u => u.IsLeadInvestigator).User;
        API.Export model = new()
        {
            DisplayName = sCase.DisplayName,
            LeadInvestigator =
                new API.User
                {
                    EmailAddress = sioUser.EmailAddress,
                    JobTitle = sioUser.JobTitle,
                    DisplayName = sioUser.DisplayName,
                    GivenName = sioUser.GivenName,
                    LastName = sioUser.LastName
                },
            Modified = sCase.Modified,
            Created = sCase.Created,
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

        // Create functions class
        Functions functions = new(sqids, caseId, emailAddress, sCase, caseUser, timeZone, configuration);

        /////////////////////////
        // Personal Comp Notes //
        /////////////////////////
        foreach (Database.ContemporaneousNote contemporaneousNote in caseUser.ContemporaneousNotes)
        {
            // Get contemporaneous note
            API.ContemporaneousNotesExport contemporaneousNoteObject =
                await functions.GetContemporaneousNote(contemporaneousNote);

            // Add contemporaneous note to model
            model.ContemporaneousNotes.Add(contemporaneousNoteObject);
        }

        ///////////
        // Tabs //
        //////////
        // Loop through each tab and add to model
        foreach (Database.Tab tab in caseUser.Tabs)
        {
            // Get tab
            API.TabExport tabObject = await functions.GetTab(tab);

            // Add tab to model
            model.Tabs.Add(tabObject);
        }

        ////////////////////////
        // Shared comp notes //
        //////////////////////
        foreach (Database.SharedContemporaneousNote contemporaneousNote in sCase.SharedContemporaneousNotes)
        {
            // Get shared contemporaneous note
            API.SharedContemporaneousNotesExport contemporaneousNoteObject =
                await functions.GetSharedContemporaneousNote(contemporaneousNote);

            // Add shared contemporaneous note to model
            model.SharedContemporaneousNotes.Add(contemporaneousNoteObject);
        }

        //////////////////
        // Shared Tabs //
        ////////////////
        // Loop through each tab and add to model
        foreach (Database.SharedTab tab in sCase.SharedTabs)
        {
            // Get shared tab
            API.SharedTabExport tabObject = await functions.GetSharedTab(tab);

            // Add shared tab to model
            model.SharedTabs.Add(tabObject);
        }

        // Log user job title change
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` exported the case `{sCase.DisplayName}`.",
                EmailAddress = emailAddress
            });

        return model;
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