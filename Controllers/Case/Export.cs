using BlazorTemplater;
using Minio;
using Minio.DataModel.Args;
using Server.Export;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;

// ReSharper disable AccessToModifiedClosure

namespace Server.Controllers.Case;

[ApiController]
[Route("/case/{caseId}/export")]
[AuditApi(EventTypeName = "HTTP")]
public class ExportController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;
    private readonly ILoggerFactory _logger;
    private readonly SqidsEncoder<long> _sqids;

    public ExportController(DatabaseContext dbContext, ILoggerFactory logger, SqidsEncoder<long> sqids)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
        _logger = logger;
        _sqids = sqids;
    }

    // GET:  /case/5/export
    [HttpGet]
    [AuditIgnore]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> ExportContemporaneousNotes(string caseId)
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
        long rawUserId = preflightResponse.Details.UserId;
        string userId = _sqids.Encode(rawUserId);
        string userNameJob = preflightResponse.Details.UserNameJob;
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;
        long rawCaseId = _sqids.Decode(caseId)[0];
        
        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Get case from the database including the required entities 
        Database.Case? sCase = await _dbContext.Case
            .Where(c => c.Id == rawCaseId && c.Users.Any(cu => cu.User.Id == rawUserId))
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Organization)
            .Include(c => c.Users)
            .ThenInclude(cu => cu.User)
            .ThenInclude(u => u.Roles)
            .Include(c => c.SharedHashes)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .Include(c => c.SharedTabs)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Organization)
            .Include(c => c.SharedContemporaneousNotes)
            .ThenInclude(st => st.Creator)
            .ThenInclude(u => u.Roles)
            .SingleOrDefaultAsync();

        // If case does not exist then return a HTTP 404 error 
        if (sCase == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Get case user from the database including the required entities 
        Database.CaseUser? caseUser = await _dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.Id == rawUserId)
            .Include(cu => cu.ContemporaneousNotes)
            .Include(cu => cu.Tabs)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return a HTTP 404 error 
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case
        
        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(organizationSettings.S3Endpoint)
            .WithCredentials(organizationSettings.S3AccessKey, organizationSettings.S3SecretKey)
            .WithSSL(organizationSettings.S3NetworkEncryption)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(organizationSettings.S3BucketName)
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        //////////////////
        // Case details //
        //////////////////
        Database.User sioUser = sCase.Users.First(u => u.IsSIO).User;
        API.Export model = new()
        {
            DisplayName = sCase.DisplayName,
            SIO = new API.User
            {
                Id = _sqids.Encode(sioUser.Id),  Auth0Id = sioUser.Auth0Id, 
                JobTitle = sioUser.JobTitle, DisplayName = sioUser.DisplayName,
                GivenName = sioUser.GivenName, LastName = sioUser.LastName, EmailAddress = sioUser.EmailAddress,
                ProfilePicture = sioUser.ProfilePicture,
                Organization = new API.Organization
                    { Name = sioUser.Organization.DisplayName, DisplayName = sioUser.Organization.DisplayName },
                Roles = sioUser.Roles.Select(r => r.Name).ToList()
            },
            Modified = sCase.Modified,
            Created = sCase.Created,
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

        // Create functions class 
        Functions functions = new(_sqids, caseId, userId, sCase, caseUser, organizationSettings, timeZone);
        
        /////////////////////////
        // Personal Comp Notes //
        /////////////////////////
        foreach (Database.ContemporaneousNote contemporaneousNote in caseUser.ContemporaneousNotes)
        {

            // Get contemporaneous note
            API.ContemporaneousNotesExport contemporaneousNoteObject = await functions.GetContemporaneousNote(contemporaneousNote);
            
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
            API.SharedContemporaneousNotesExport contemporaneousNoteObject = await functions.GetSharedContemporaneousNote(contemporaneousNote);
            
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

        /////////////////////////////
        // HTML to PDF conversion //
        ///////////////////////////
        // Convert export cover template blazor page to HTML
        string coverHTML = new ComponentRenderer<ExportCoverPageTemplate>()
            .Set(c => c.Model, model)
            .AddService(_logger)
            .Render();
        
        // Convert export template blazor page to HTML
        string contentHTML = new ComponentRenderer<ExportTemplate>()
            .Set(c => c.Model, model)
            .AddService(_logger)
            .Render();
        
        // Use export helpers
        PDFFuctions pdfFuctions = new();
        
        // Create cover page PDF and load it
        MemoryStream coverPDFStream = await pdfFuctions.GeneratePDFCoverPage(coverHTML, model.DisplayName);
        PdfLoadedDocument coverPDF = new(coverPDFStream);
        
        // Create content PDF and load it
        MemoryStream contentPDFStream = await  pdfFuctions.GeneratePDF(contentHTML, model.DisplayName, timeZone.DisplayName);
        PdfLoadedDocument contentPDF = new(contentPDFStream);
        
        // Create final PDF document
        PdfDocument finalDocument = new();
        
        // Add cover page
        finalDocument.ImportPage(coverPDF, 0);
        
        // Add all content pages
        finalDocument.ImportPageRange(contentPDF, 0, contentPDF.PageCount - 1);
        
        // Create memory stream to store the final pdf in
        MemoryStream finalMemoryStream = new();
        
        // Save the final pdf to the memory stream
        finalDocument.Save(finalMemoryStream);
        finalDocument.Close(true);
        
        // Flush the memory stream and set position to 0
        finalMemoryStream.Flush(); 
        finalMemoryStream.Position = 0;
        
          ////////////////////
         // Save PDF to s3 //
        ////////////////////
        // Create a variable for object path with auth0| removed 
        string pdfObjectPath = $"cases/{caseId}/{userId}/export/Lighthouse Notes Export {sCase.DisplayName}.pdf";

        // Log PDF export creation
        await _auditContext.LogAsync("Lighthouse Notes",
            new
            {
                Action =
                    $"`{userNameJob}` created a PDF export for the case `{caseId}`.",
                UserID = rawUserId, OrganizationID = organizationId
            });

        // Save file to s3 bucket
        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
            .WithObject(pdfObjectPath)
            .WithStreamData(finalMemoryStream)
            .WithObjectSize(finalMemoryStream.Length)
            .WithContentType("application/octet-stream")
        );

        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(organizationSettings.S3BucketName)
            .WithObject(pdfObjectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
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
        public required Database.OrganizationSettings OrganizationSettings { get; init; }
        public long UserId { get; init; }
        public required string UserNameJob { get; init; }
        public required Database.UserSettings UserSettings { get; init; }
    }
}