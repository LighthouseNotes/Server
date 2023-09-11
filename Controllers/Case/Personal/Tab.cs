using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;
using Minio;
using Minio.DataModel;
using Minio.Exceptions;

namespace LighthouseNotesServer.Controllers.Case.Personal;

[ApiController]
[AuditApi(EventTypeName = "HTTP")]
public class TabsController : ControllerBase
{
    private readonly AuditScopeFactory _auditContext;
    private readonly DatabaseContext _dbContext;

    public TabsController(DatabaseContext dbContext)
    {
        _dbContext = dbContext;
        _auditContext = new AuditScopeFactory();
    }

    // GET: /case/5/tabs
    [Route("/case/{caseId:guid}/tabs")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<IEnumerable<API.Tab>>> GetTabs(Guid caseId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        return sCase.Tabs.Where(t => t.User == user).Select(t => new API.Tab
        {
            Id = t.Id,
            Name = t.Name
        }).ToList();
    }

    // GET: /tab/5
    [Route("/case/{caseId:guid}/tab/{tabId:guid}")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Tab>> GetTab(Guid caseId, Guid tabId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        // Fetch tab from the database
        Database.Tab? tab = sCase.Tabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error
        if (tab == null)
            return NotFound($"A tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // If the user does not have access to the tab then return HTTP 401 error
        if (tab.User != user)
            return Unauthorized($"You do not have permission to access the tab with the ID `{tabId}`!");


        return new API.Tab
        {
            Id = tab.Id,
            Name = tab.Name
        };
    }

    // POST: /case/5/tab
    [Route("/case/{caseId:guid}/tab")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<API.Tab>> CreateTab(Guid caseId, API.AddTab tabAddObject)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        Database.Tab tabModel = new()
        {
            User = user,
            Name = tabAddObject.Name
        };

        // Add tab to database
        sCase.Tabs.Add(tabModel);

        // Save changes to the database
        await _dbContext.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTabs), new { caseId }, new API.Tab { Id = tabModel.Id, Name = tabModel.Name });
    }

    // GET: /tab/5/content
    [Route("/case/{caseId:guid}/tab/{tabId:guid}/content")]
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> GetTabContent(Guid caseId, Guid tabId)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        // Fetch tab from the database
        Database.Tab? tab = sCase.Tabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error
        if (tab == null)
            return NotFound($"A tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // If the user does not have access to the tab then return HTTP 401 error
        if (tab.User != user)
            return Unauthorized($"You do not have permission to access the tab with the ID `{tabId}`!");

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(false)
            .Build();

        // Create a variable with filepath - note removing auth0| from filepath 
        string objectPath = $"cases/{sCase.Id}/{user.Id.Replace("auth0|", "")}/tabs/{tab.Id}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket("lighthouse-notes")
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        ObjectStat objectMetadata;

        // Try and access object if object does not exist catch exception
        try
        {
            objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
            );
        }
        // Catch object not found exception and return HTTP 500 error
        catch (ObjectNotFoundException)
        {
            return Problem(
                $"Can not find the S3 object for the tab with the ID `{tabId}` at the following path `{objectPath}`. Its likely the object does not exist because you have not created any notes!");
        }

        // Fetch object hash from database
        Database.Hash? objectHashes = sCase.Hashes.SingleOrDefault(h =>
            h.User == user && h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return Problem($"Unable to find hash values for the tab with the ID `{tabId}`!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket("lighthouse-notes")
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return Problem("MD5 hash verification failed!");

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem("SHA256 hash verification failed!");

        // Return file
        return File(memoryStream.ToArray(), "application/octet-stream", "");
    }

    // POST: /tab/5/content
    [Route("/case/{caseId:guid}/tab/{tabId:guid}/content")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostTabContent(Guid caseId, Guid tabId, [FromForm] IFormFile file)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        // Fetch tab from the database
        Database.Tab? tab = sCase.Tabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error
        if (tab == null)
            return NotFound($"A tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // If the user does not have access to the tab then return HTTP 401 error
        if (tab.User != user)
            return Unauthorized($"You do not have permission to access the tab with the ID `{tabId}`!");

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(false)
            .Build();

        // Create a variable with filepath - note removing auth0| from filepath 
        string objectPath = $"cases/{sCase.Id}/{user.Id.Replace("auth0|", "")}/tabs/{tab.Id}/content.txt";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket("lighthouse-notes")
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Check if object exists
        try
        {
            await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
            );

            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save the updated file to the s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );


            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the addition of content
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` changed the content in the tab `{tab.Name}` for `{sCase.DisplayName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        // Object does not exist so create it
        catch (ObjectNotFoundException)
        {
            // Create memory stream to store file contents
            MemoryStream memoryStream = new();

            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the creation
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` created content in the tab `{tab.Name}` for `{sCase.DisplayName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });

            // Return Ok
            return Ok();
        }
        catch (MinioException e)
        {
            return Problem(
                $"An unknown error occured while adding to or creating the tab. For more information see the following error message: `{e.Message}`");
        }
    }

    // GET: /tab/5/content/image/100.jpg
    [HttpGet("/case/{caseId:guid}/tab/{tabId:guid}/content/image/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult<string>> GetTabContentImage(Guid caseId, Guid tabId, string fileName)
    {
        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        // Fetch tab from the database
        Database.Tab? tab = sCase.Tabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error
        if (tab == null)
            return NotFound($"A tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // If the user does not have access to the tab then return HTTP 401 error
        if (tab.User != user)
            return Unauthorized("You do not have permission to access the tab with the ID `{tabId}`!");

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(false)
            .Build();

        // Create a variable for object path NOTE: removing auth0| from objectPath 
        string objectPath = $"cases/{tab.Case.Id}/{user.Id.Replace("auth0|", "")}/tabs/{tabId}/images/{fileName}";

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket("lighthouse-notes")
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Fetch object metadata
        ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
            .WithBucket("lighthouse-notes")
            .WithObject(objectPath)
        );

        // Fetch object hash from database
        Database.Hash? objectHashes = sCase.Hashes.SingleOrDefault(h =>
            h.User == user && h.ObjectName == objectMetadata.ObjectName && h.VersionId == objectMetadata.VersionId);

        // If object hash is null then a hash does not exist so return HTTP 500 error
        if (objectHashes == null)
            return Problem("Unable to find hash values for the requested image!");

        // Create memory stream to store file contents
        MemoryStream memoryStream = new();

        // Get object and copy file contents to stream
        await minio.GetObjectAsync(new GetObjectArgs()
            .WithBucket("lighthouse-notes")
            .WithObject(objectPath)
            .WithCallbackStream(stream => { stream.CopyTo(memoryStream); })
        );

        // Create MD5 and SHA256
        using MD5 md5 = MD5.Create();
        using SHA256 sha256 = SHA256.Create();

        // Generate MD5 and SHA256 hash
        byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
        byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

        // Check MD5 hash generated matches hash in the database
        if (BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant() != objectHashes.Md5Hash)
            return Problem("MD5 hash verification failed!");

        // Check SHA256 hash generated matches hash in the database
        if (BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant() != objectHashes.ShaHash)
            return Problem("SHA256 hash verification failed!");

        // Fetch presigned url for object  
        string url = await minio.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket("lighthouse-notes")
            .WithObject(objectPath)
            .WithExpiry(3600)
        );

        // Return url
        return url;
    }

    // POST  /case/5/contemporaneous-notes/image
    [HttpPost("/case/{caseId:guid}/tab/{tabId:guid}/content/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(string), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(string), StatusCodes.Status404NotFound)]
    [Authorize(Roles = "user")]
    public async Task<ActionResult> PostTabContentImage(Guid caseId, Guid tabId, IList<IFormFile> uploadFiles)
    {
        // Get file size
        long size = uploadFiles.Sum(f => f.Length);

        // Preflight checks
        PreflightResponse preflightResponse = await PreflightChecks(caseId);

        // If preflight checks returned an error return it here
        if (preflightResponse.Error != null) return preflightResponse.Error;

        // If organization, user or case are null return HTTP 500 error
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

        // Fetch tab from the database
        Database.Tab? tab = sCase.Tabs.SingleOrDefault(t => t.Id == tabId);

        // If tab is null then return HTTP 404 error
        if (tab == null)
            return NotFound($"A tab with the ID `{tabId}` was not found in the case with the ID`{caseId}`!");

        // If the user does not have access to the tab then return HTTP 401 error
        if (tab.User != user)
            return Unauthorized($"You do not have permission to access the tab with the ID `{tabId}`!");

        // Check if organization has configuration
        if (organization.Configuration == null)
            return Problem(
                "Your organization has not configured S3 connection settings. Please configure these settings and try again!");

        // Create minio client
        MinioClient minio = new MinioClient()
            .WithEndpoint(organization.Configuration.S3Endpoint)
            .WithCredentials(organization.Configuration.S3AccessKey, organization.Configuration.S3SecretKey)
            .WithSSL(false)
            .Build();

        // Check if bucket exists
        bool bucketExists = await minio.BucketExistsAsync(new BucketExistsArgs()
            .WithBucket("lighthouse-notes")
        ).ConfigureAwait(false);

        // If bucket does not exist return HTTP 500 error
        if (!bucketExists)
            return Problem("An S3 Bucket with the name `lighthouse-notes` does not exist!");

        // Loop through each file
        foreach (IFormFile file in uploadFiles)
        {
            // Create variable for file name
            string fileName = ContentDispositionHeaderValue.Parse(file.ContentDisposition).FileName.Trim().ToString();

            string objectPath =
                $"cases/{tab.Case.Id}/{user.Id.Replace("auth0|", "")}/tabs/{tabId}/images/{fileName}";

            // Create memory stream to hold file contents 
            using MemoryStream memoryStream = new();

            // Copy file to memory stream
            await file.CopyToAsync(memoryStream);

            // Set memory stream position to 0 as per github.com/minio/minio/issues/6274
            memoryStream.Position = 0;

            // Save file to s3 bucket
            await minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
                .WithStreamData(memoryStream)
                .WithObjectSize(memoryStream.Length)
                .WithContentType("application/octet-stream")
            );

            // Fetch object metadata
            ObjectStat objectMetadata = await minio.StatObjectAsync(new StatObjectArgs()
                .WithBucket("lighthouse-notes")
                .WithObject(objectPath)
            );

            // Create MD5 and SHA256
            using MD5 md5 = MD5.Create();
            using SHA256 sha256 = SHA256.Create();

            // Generate MD5 and SHA256 hash
            byte[] md5Hash = await md5.ComputeHashAsync(memoryStream);
            byte[] sha256Hash = await sha256.ComputeHashAsync(memoryStream);

            // Save hash to the database
            sCase.Hashes.Add(new Database.Hash
            {
                User = user,
                ObjectName = objectMetadata.ObjectName,
                VersionId = objectMetadata.VersionId,
                Md5Hash = BitConverter.ToString(md5Hash).Replace("-", "").ToLowerInvariant(),
                ShaHash = BitConverter.ToString(sha256Hash).Replace("-", "").ToLowerInvariant()
            });

            // Save changes to the database
            await _dbContext.SaveChangesAsync();

            // Log the creation of the image
            await _auditContext.LogAsync("Lighthouse Notes",
                new
                {
                    Action =
                        $"User `{user.DisplayName} ({user.JobTitle})` uploaded an image to the tab `{tab.Name}` for case `{sCase.DisplayName}` with name `{fileName}`.",
                    UserID = user.Id, OrganizationID = organization.Id
                });
        }

        // Return Ok
        return Ok(new { count = uploadFiles.Count, size });
    }


    private async Task<PreflightResponse> PreflightChecks(Guid caseId)
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

        // If case does not exist in organization return a HTTP 404 error
        if (organization.Cases.All(c => c.Id != caseId))
            return new PreflightResponse { Error = NotFound($"A case with the ID `{caseId}` could not be found!") };

        // Fetch case from database and include case users and then include user details
        Database.Case sCase = organization.Cases.Single(c => c.Id == caseId);


        // If user does not have access to the requested case return a HTTP 403 error
        if (sCase.Users.All(u => u.User.Id != userId))
            return new PreflightResponse
            {
                Error = Unauthorized(
                    $"The user with the ID `{userId}` does not have access to the case with the ID `{caseId}`!")
            };

        // Fetch user from database
        Database.User user = organization.Users.Single(u => u.Id == userId);

        // Return organization, user and case entity 
        return new PreflightResponse { Organization = organization, User = user, SCase = sCase };
    }


    private class PreflightResponse
    {
        public ObjectResult? Error { get; init; }
        public Database.Organization? Organization { get; init; }
        public Database.User? User { get; init; }
        public Database.Case? SCase { get; init; }
    }
}