using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;

// Version and copyright message
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("Lighthouse Notes Server");
Console.WriteLine(Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3));
Console.WriteLine();
Console.WriteLine("(C) Copyright 2024 Lighthouse Notes");
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.White;

// Create builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add routing
builder.Services.AddRouting(options =>
{
    // Use lower case URLs and lower case query strings
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

// Use Redis for key storage if running in production
if (builder.Environment.IsProduction())
{
    ConnectionMultiplexer redis = ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis") ??
                                                                throw new InvalidOperationException(
                                                                    "Connection string 'Redis' not found in appssettings.json or environment variable!"));
    builder.Services.AddDataProtection()
        .PersistKeysToStackExchangeRedis(redis, "DataProtection-Keys");
}

// Add Certificate forwarding - for Nginx reverse proxy
builder.Services.AddCertificateForwarding(options =>
{
    options.CertificateHeader = "X-SSL-CERT";
    options.HeaderConverter = headerValue =>
    {
        X509Certificate2 clientCertificate = new(HttpUtility.UrlDecodeToBytes(headerValue));
        return clientCertificate;
    };
});

// Add forward headers for reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add cross-origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins(builder.Configuration["WebApp"] ?? throw new InvalidOperationException(
                    "'WebApp' not found in appssettings.json or environment variable!"))
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
                .WithExposedHeaders("x-page", "x-per-page", "x-total-count", "x-total-pages");
        });
});

// Add database connection
builder.Services.AddDbContext<DatabaseContext>(options =>
{
    // Get connection string for database from appsettings.json, if it is null throw invalid operation exception and use query splitting
    options.UseNpgsql(builder.Configuration.GetConnectionString("Database") ??
                      throw new InvalidOperationException(
                          "Connection string 'Database' not found in appssettings.json or environment variable!"),
        o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

    // If environment is development enable sensitive data logging
    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// Add squids encoder service
builder.Services.AddSingleton(new SqidsEncoder<long>(new SqidsOptions
{
    // Get alphabet from appsettings.json, if it is null throw  invalid operation exception
    Alphabet = builder.Configuration["Sqids:Alphabet"] ??
               throw new InvalidOperationException(
                   "Squid alphabet `Sqids:Alphabet` not found in appssettings.json or environment variable!"),

    // Get min length
    MinLength = Convert.ToInt32(builder.Configuration["Sqids:MinLength"])
}));

// Add JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Get authority, audience and issuer from appsettings.json
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidAudience = builder.Configuration["Authentication:Audience"],
                ValidIssuer = builder.Configuration["Authentication:Authority"],
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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddSingleton(builder.Configuration);

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Swagger documentation
    options.SwaggerDoc("v1",
        new OpenApiInfo
        {
            Version = "v1",
            Title = "Lighthouse Notes Server API",
            Description = "An ASP.NET Core Web API for managing Lighthouse Notes",
            Contact = new OpenApiContact { Name = "Ben Davies" },
            License = new OpenApiLicense
            {
                Name = "CC BY-NC 4.0", Url = new Uri("https://raw.githubusercontent.com/LighthouseNotes/Server/main/LICENSEe")
            }
        });

    // Create OAuth2 authentication option in swagger
    OpenApiSecurityScheme securitySchema = new()
    {
        Description = "Using the Authorization header with the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            Implicit = new OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{builder.Configuration["Authentication:Authority"]}/protocol/openid-connect/auth"),
                Scopes = new Dictionary<string, string> { { "openid", "openid" }, { "profile", "profile" } }
            }
        },
        Scheme = "bearer",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };

    // Add OAuth2 authentication option to swagger
    options.AddSecurityDefinition("Bearer", securitySchema);

    // Set security requirements
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { { securitySchema, ["Bearer"] } });
});

// Configure Audit Logging
Configuration.Setup()
    .UsePostgreSql(config => config
        .ConnectionString(builder.Configuration.GetConnectionString("Database") ??
                          throw new InvalidOperationException(
                              "Connection string 'Database' not found in appssettings.json or environment variable!"))
        .TableName("Event")
        .IdColumnName("Id")
        .DataColumn("Data")
        .LastUpdatedColumnName("Updated")
        .CustomColumn("EventType", ev => ev.EventType)
        .CustomColumn("EmailAddress", ev => ev.CustomFields.FirstOrDefault(a => a.Key == "EmailAddress").Value));

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
        options.OAuthClientId("lighthousenotes");
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