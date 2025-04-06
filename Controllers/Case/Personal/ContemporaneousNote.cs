using System.Security.Cryptography;
using HtmlAgilityPack;
using Meilisearch;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Index = Meilisearch.Index;

namespace Server.Controllers.Case.Personal;

[ApiController]
[Route("/case/{caseId}")]
[AuditApi(EventTypeName = "HTTP")]
public class ContemporaneousNotesController(DatabaseContext dbContext, SqidsEncoder<long> sqids, IConfiguration configuration)
    : ControllerBase
{
    private readonly AuditScopeFactory _auditContext = new();

    // GET:  /case/?/contemporaneous-notes
    [HttpGet("contemporaneous-notes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<List<API.ContemporaneousNotes>>> GetContemporaneousNotes(string caseId)
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
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.ContemporaneousNotes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return a list of the user's contemporaneous notes
        return caseUser.ContemporaneousNotes.Select(cn => new API.ContemporaneousNotes
        {
            Id = sqids.Encode(cn.Id), Created = TimeZoneInfo.ConvertTimeFromUtc(cn.Created, timeZone)
        }).ToList();
    }

    // GET: /case/?/contemporaneous-note/?
    [HttpGet("contemporaneous-note/{noteEmailAddress}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult> GetContemporaneousNote(string caseId, string noteEmailAddress)
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
        long rawCaseId = sqids.Decode(caseId)[0];

        // Log the user's ID
        IAuditScope auditScope = this.GetCurrentAuditScope();

        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.ContemporaneousNotes)
            .Include(cu => cu.Hashes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Convert note ID squid to ID
        long rawNoteId = sqids.Decode(noteEmailAddress)[0];

        // Fetch the contemporaneous note from the database
        Database.ContemporaneousNote? contemporaneousNote =
            caseUser.ContemporaneousNotes.SingleOrDefault(cn => cn.Id == rawNoteId);

        // If contemporaneous note is null then return an HTTP 404 error as it does not exist
        if (contemporaneousNote == null) return NotFound($"Can not find the note with the ID `{noteEmailAddress}`");

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create a variable for object path
        string objectPath =
            $"cases/{caseId}/{emailAddress}/contemporaneous-notes/{noteEmailAddress}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP a 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        // Create variable to store object metadata
        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return Problem(
                $"Can not find the S3 object for contemporaneous note: `{noteEmailAddress}` at the following path: `{objectPath}`.");
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = caseUser.Hashes.SingleOrDefault(h =>
            h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return an HTTP 500 error
        if (objectHashes == null)
            return Problem(
                $"Unable to find hash value for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                title: "Could not find hash value for contemporaneous note!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
        memoryStream.Position = 0;

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check generated MD5 hash matches the hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return Problem(
                $"MD5 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                title: "MD5 hash verification failed!");

        // Check generated SHA256 hash matches the hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem(
                $"SHA256 hash verification failed for contemporaneous note with the ID `{sqids.Encode(contemporaneousNote.Id)}` at the path `{objectPath}`!",
                title: "SHA256 hash verification failed!");

        // Return file
        return File(memoryStream.ToArray(), "application/octet-stream", "");
    }

    // POST: /case/?/contemporaneous-notes
    [HttpPost("contemporaneous-note")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult> PostContemporaneousNote(string caseId, IFormFile file)
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

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.ContemporaneousNotes)
            .Include(cu => cu.Hashes)
            .Include(cu => cu.Case)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Create minio client
        IMinioClient minio = new MinioClient()
            .WithEndpoint(configuration.GetValue<string>("Minio:Endpoint"))
            .WithCredentials(configuration.GetValue<string>("Minio:AccessKey"), configuration.GetValue<string>("Minio:SecretKey"))
            .WithSSL(configuration.GetValue<bool>("Minio:NetworkEncryption"))
            .Build();

        // Create contemporaneous note record in the database
        Database.ContemporaneousNote contemporaneousNote = new();

        // Add the note to the collection
        caseUser.ContemporaneousNotes.Add(contemporaneousNote);

        // Save changes to the database so a GUID is generated
        await dbContext.SaveChangesAsync();

        // Create a variable for object path
        string objectPath =
            $"cases/{caseId}/{emailAddress}/contemporaneous-notes/{sqids.Encode(contemporaneousNote.Id)}/note.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
        ).ConfigureAwait(false);

        // If bucket does not exist return an HTTP 500 error
        if (!bucketExists)
            return Problem($"An S3 Bucket with the name `{configuration.GetValue<string>("Minio:BucketName")}` does not exist!");

        try
        {
            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Read memory stream to text
            StreamReader reader = new(memoryStream);
            string text = await reader.ReadToEndAsync();

            // Create html document from text
            HtmlDocument htmlDoc = new();
            htmlDoc.LoadHtml(text);

            // Create a space separated list of all the text content in the HTML note
            string content = htmlDoc.DocumentNode.SelectNodes("//text()")
                .Aggregate("", (current, node) => current + $" {node.InnerText}");

            // Create Meilisearch Client
            MeilisearchClient meiliClient =
                new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

            // Try getting the contemporaneous-notes index
            try
            {
                await meiliClient.GetIndexAsync("contemporaneous-notes");
            }
            // Catch Meilisearch exceptions
            catch (MeilisearchApiError e)
            {
                // If error code is index_not_found create the index
                if (e.Code == "index_not_found")
                {
                    await meiliClient.CreateIndexAsync("contemporaneous-notes", "id");
                    await meiliClient.Index("contemporaneous-notes")
                        .UpdateFilterableAttributesAsync(["caseId", "emailAddress"]);
                }
            }

            // Get the contemporaneous-notes index
            Index index = meiliClient.Index("contemporaneous-notes");

            await index.AddDocumentsAsync([
                new Search.ContemporaneousNote
                {
                    Id = contemporaneousNote.Id, EmailAddress = emailAddress, CaseId = sqids.Decode(caseId)[0], Content = content
                }
            ]);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket(configuration.GetValue<string>("Minio:BucketName"))
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            caseUser.Hashes.Add(new Database.Hash
            {
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await dbContext.SaveChangesAsync();

            // Log the creation
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"`{userNameJob}` created contemporaneous note for `{caseUser.Case.DisplayName}` with the ID `{sqids.Encode(contemporaneousNote.Id)}`.",
                    EmailAddress = emailAddress
                });

            // Return Ok
            return Ok();
        }
        catch (MinioException e)
        {
            return Problem(
                $"An unknown error occured while adding a contemporaneous note. For more information see the following error message: `{e.Message}`");
        }
    }

    // POST:  /case/?/contemporaneous-notes/search
    [HttpPost("contemporaneous-notes/search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    [Authorize]
    public async Task<ActionResult<List<API.ContemporaneousNotes>>> GetSearchContemporaneousNotes(string caseId,
        API.Search search)
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
        long rawCaseId = sqids.Decode(caseId)[0];
        Database.UserSettings userSettings = preflightResponse.Details.UserSettings;

        // Log the user's email address
        IAuditScope auditScope = this.GetCurrentAuditScope();
        auditScope.SetCustomField("emailAddress", emailAddress);

        // Get case user from the database including the required entities
        Database.CaseUser? caseUser = await dbContext.CaseUser
            .Where(cu => cu.Case.Id == rawCaseId && cu.User.EmailAddress == emailAddress)
            .Include(cu => cu.ContemporaneousNotes)
            .SingleOrDefaultAsync();

        // If case user does not exist then return an HTTP 404 error
        if (caseUser == null) return NotFound($"The case `{caseId}` does not exist!");
        // The case might not exist or the user does not have access to the case

        // Create Meilisearch Client
        MeilisearchClient meiliClient =
            new(configuration.GetValue<string>("Meilisearch:Url"), configuration.GetValue<string>("Meilisearch:Key"));

        // Get the contemporaneous-notes index
        Index index = meiliClient.Index("contemporaneous-notes");

        // Search the index for the string filtering by case id and user id
        ISearchable<Search.ContemporaneousNote> searchResult = await index.SearchAsync<Search.ContemporaneousNote>(
            search.Query,
            new SearchQuery
            {
                AttributesToSearchOn = ["content"],
                AttributesToRetrieve = ["id"],
                Filter = $"caseId = {rawCaseId} AND emailAddress = {emailAddress}"
            }
        );

        // Create a list of contemporaneous notes ids that contain a match
        List<long> contemporaneousNotesIds = searchResult.Hits.Select(cn => cn.Id).ToList();

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(userSettings.TimeZone);

        // Return a list of the contemporaneous notes containing the search query
        return caseUser.ContemporaneousNotes.Where(cn => contemporaneousNotesIds.Contains(cn.Id))
            .Select(cn => new API.ContemporaneousNotes
            {
                Id = sqids.Encode(cn.Id), Created = TimeZoneInfo.ConvertTimeFromUtc(cn.Created, timeZone)
            }).ToList();
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