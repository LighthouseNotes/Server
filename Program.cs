using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Audit.PostgreSql.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;

// Create builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ILoggerFactory loggerFactory = LoggerFactory.Create(loggingBuilder => { loggingBuilder.AddSimpleConsole(); });

// Use lower case URLs
builder.Services.AddRouting(options =>
{
    options.LowercaseUrls = true;
    options.LowercaseQueryStrings = true;
});

// Add database
builder.Services.AddDbContext<DatabaseContext>(options =>
{
    options.UseLazyLoadingProxies();
    options.UseNpgsql(builder.Configuration.GetConnectionString("DatabaseContext") ??
                      throw new InvalidOperationException("Connection string 'DatabaseContext' not found."));
    options.EnableSensitiveDataLogging();
});

// Add cross origin
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        corsPolicyBuilder =>
        {
            corsPolicyBuilder.WithOrigins("https://localhost:5001")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
});


// Add authentication 
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Get authority and audience from appsettings.json
        options.Authority = $"https://{builder.Configuration["Auth0:Domain"]}/";
        options.Audience = builder.Configuration["Auth0:Audience"];

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
        options.IncludeErrorDetails = true;
    });


// Add authorization 
builder.Services.AddAuthorization();

// Add controllers
builder.Services.AddControllers(options =>
    {
        // Remove all input and output formatters
        options.OutputFormatters.Clear();
        //options.InputFormatters.Clear();

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
    .AddJsonOptions(options => { options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles; });

// Add Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
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
            Name = "CC BY-NC 3.0",
            Url = new Uri("https://example.com/license")
        }
    });

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

    options.AddSecurityDefinition("Bearer", securitySchema);

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { securitySchema, new[] { "Bearer" } }
    });

    string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

// Configure Audit Logging
Configuration.Setup()
    .UsePostgreSql(config => config
        .ConnectionString(builder.Configuration.GetConnectionString("DatabaseContext") ??
                          throw new InvalidOperationException("Connection string 'DatabaseContext' not found."))
        .TableName("event")
        .IdColumnName("id")
        .DataColumn("data", DataType.JSONB)
        .LastUpdatedColumnName("updated_date")
        .CustomColumn("event_type", ev => ev.EventType));

// Build the app
WebApplication app = builder.Build();

// If app is development 
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty;
    });
    app.UseDeveloperExceptionPage();
}

// Audit Logging Middleware 
app.UseAuditMiddleware(auditMiddleware => auditMiddleware 
    .WithEventType("HTTP")
    .IncludeHeaders()
    .IncludeResponseHeaders()
    .IncludeRequestBody()
    .IncludeResponseBody());

// HTTPS Redirection
app.UseHttpsRedirection();

// Routing and cross origin
app.UseRouting();
app.UseCors();

// Use Authentication and Authorization 
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Run 
app.Run();