using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

// Create builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Create logger factory
ILoggerFactory loggerFactory = LoggerFactory.Create(loggingBuilder => { loggingBuilder.AddSimpleConsole(); });

// Add routing 
builder.Services.AddRouting(options =>
{
    // Use lower case URLs and lower case query strings
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

// Add Certificate forwarding - for Nginx reverse proxy 
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = "X-SSL-CERT";
    options.HeaderConverter = headerValue =>
    {
        X509Certificate2 clientCertificate = new(System.Web.HttpUtility.UrlDecodeToBytes(headerValue));
        return clientCertificate;
    };
});

// Add forward headers for reverse proxy 
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add cross origin for local development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins(builder.Configuration["WebApp"] ?? throw new InvalidOperationException(
                    "Connection string 'DatabaseContext' not found in appssettings.json"))
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});

// Add database connection
builder.Services.AddDbContext<DatabaseContext>(options =>
{
    // Get connection string for database from appsettings.json, if it is null throw invalid operation exception 
    options.UseNpgsql(builder.Configuration.GetConnectionString("DatabaseContext") ??
                      throw new InvalidOperationException(
                          "Connection string 'DatabaseContext' not found in appssettings.json"));

    // If environment is development enable sensitive data logging
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// Add squids encoder service
builder.Services.AddSingleton(new SqidsEncoder<long>(new SqidsOptions
{
    // Get alphabet from appsettings.json, if it is null throw  invalid operation exception 
    Alphabet = builder.Configuration["Sqids:Alphabet"] ??
               throw new InvalidOperationException("Squid alphabet `Sqids:Alphabet` not found in appssettings.json"),

    // Get min length
    MinLength = Convert.ToInt32(builder.Configuration["Sqids:MinLength"])
}));

// Add JWT authentication 
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Get authority, audience and issuer from appsettings.json
        options.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
        options.TokenValidationParameters =
            new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidAudience = builder.Configuration["Auth0:Audience"],
                ValidIssuer = $"{builder.Configuration["Auth0:Domain"]}",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

        // If token is not provided return you are not authorized
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                context.Response.OnStarting(async () =>
                {
                    await context.Response.WriteAsync(
                        JsonSerializer.Serialize(new API.ApiResponse("You are not authorized!"))
                    );
                });

                return Task.CompletedTask;
            }
        };
    });

// Add authorization 
builder.Services.AddAuthorization();

// Add controllers
builder.Services.AddControllers(options =>
    {
        // Remove all input and output formatters
        options.OutputFormatters.Clear();
        options.InputFormatters.Clear();

        // Create logger 
        ILogger<SystemTextJsonInputFormatter> jsonLogger = loggerFactory.CreateLogger<SystemTextJsonInputFormatter>();

        // Create a JSON Output and Input Formatter
        SystemTextJsonOutputFormatter systemTextJsonOutputFormatter = new(new JsonSerializerOptions());
        SystemTextJsonInputFormatter systemTextJsonInputFormatter = new(new JsonOptions(), jsonLogger);

        // Remove all media types
        systemTextJsonOutputFormatter.SupportedMediaTypes.Clear();
        systemTextJsonInputFormatter.SupportedMediaTypes.Clear();

        // Add application/json media type
        systemTextJsonOutputFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/json"));
        systemTextJsonOutputFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/problem+json"));
        systemTextJsonInputFormatter.SupportedMediaTypes.Add(new MediaTypeHeaderValue("application/json"));

        // Add JSON Output and Input Formatter to Output and Input Formatters 
        options.OutputFormatters.Add(systemTextJsonOutputFormatter);
        options.InputFormatters.Add(systemTextJsonInputFormatter);
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swagger documentation
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "Lighthouse Notes Server API",
        Description = "An ASP.NET Core Web API for managing Lighthouse Notes",
        TermsOfService = new Uri("https://example.com/terms"),
        Contact = new OpenApiContact
        {
            Name = "Ben Davies",
            Url = new Uri("https://example.com/contact")
        },
        License = new OpenApiLicense
        {
            Name = "CC BY-NC 4.0",
            Url = new Uri("https://raw.githubusercontent.com/LighthouseNotes/Server/main/LICENSEe")
        }
    });

    // Create JWT authentication option in swagger
    OpenApiSecurityScheme securitySchema = new()
    {
        Description = "Using the Authorization header with the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
        }
    };

    // Add JWT authentication option to swagger
    options.AddSecurityDefinition("Bearer", securitySchema);

    // Set security requirements 
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securitySchema, new[] { "Bearer" } }
    });
});

// Configure Audit Logging
Configuration.Setup()
    .UsePostgreSql(config => config
        .ConnectionString(builder.Configuration.GetConnectionString("DatabaseContext") ??
                          throw new InvalidOperationException("Connection string 'DatabaseContext' not found."))
        .TableName("Event")
        .IdColumnName("Id")
        .DataColumn("Data")
        .LastUpdatedColumnName("Updated")
        .CustomColumn("EventType", ev => ev.EventType)
        .CustomColumn("UserId", ev => ev.CustomFields.FirstOrDefault(a => a.Key == "UserID").Value));

// Register syncfusion licence 
Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(builder.Configuration["Syncfusion:LicenseKey"]);

// Build the app
WebApplication app = builder.Build();

// If environment is development
if (app.Environment.IsDevelopment())
{
    // Use swagger and swagger UI
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty;
    });

    // Developer exception page for detailed exception 
    app.UseDeveloperExceptionPage();
}

// If environment is production
if (app.Environment.IsProduction())
{
    // Use certificate forwarding and header forwarding as production environment runs behind a reverse proxy
    app.UseCertificateForwarding();
    app.UseForwardedHeaders();
}

// CORS
app.UseCors();

// Audit Logging Middleware 
app.UseAuditMiddleware(auditMiddleware => auditMiddleware
    .WithEventType("HTTP")
    .IncludeHeaders()
    .IncludeResponseHeaders()
    .IncludeRequestBody()
    .IncludeResponseBody());

// HTTPS Redirection
app.UseHttpsRedirection();

// Routing
app.UseRouting();

// Use Authentication and Authorization 
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Run app
app.Run();