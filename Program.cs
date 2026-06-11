using EtisalatSaasCallback.Authentication;
using EtisalatSaasCallback.Configuration;
using EtisalatSaasCallback.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.OpenApi.Models;
using Polly;
using Polly.Extensions.Http;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/etisalat-saas-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Configuration
builder.Services.Configure<EtisalatSettings>(
    builder.Configuration.GetSection(EtisalatSettings.SectionName));
builder.Services.Configure<IsvSettings>(
    builder.Configuration.GetSection(IsvSettings.SectionName));
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection(MongoDbSettings.SectionName));
builder.Services.Configure<TicketMonitorSettings>(
    builder.Configuration.GetSection(TicketMonitorSettings.SectionName));
builder.Services.Configure<SlaSettings>(
    builder.Configuration.GetSection(SlaSettings.SectionName));
builder.Services.Configure<UiAuthSettings>(
    builder.Configuration.GetSection(UiAuthSettings.SectionName));

// Authentication - Cookie for UI, Basic for API
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(
        builder.Configuration.GetValue<int>("UiAuth:SessionTimeoutMinutes", 60));
    options.SlidingExpiration = true;
})
.AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>("BasicAuth", null);

builder.Services.AddAuthorization();

// Services
builder.Services.AddSingleton<IMongoDbService, MongoDbService>();
builder.Services.AddScoped<IProvisioningService, ProvisioningService>();
builder.Services.AddSingleton<IUserService, UserService>();

// Background Service - Ticket Monitor
builder.Services.AddHostedService<TicketMonitorService>();

// HTTP Client for Etisalat with retry policy
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, retryAttempt =>
        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

builder.Services.AddHttpClient<IEtisalatCallbackClient, EtisalatCallbackClient>()
    .AddPolicyHandler(retryPolicy);

// MVC and Controllers
builder.Services.AddControllersWithViews();

// CORS configuration from appsettings
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
if (corsSettings.Enabled)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("CorsPolicy", policy =>
        {
            if (corsSettings.AllowedOrigins.Contains("*"))
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(corsSettings.AllowedOrigins);

            if (corsSettings.AllowedMethods.Contains("*"))
                policy.AllowAnyMethod();
            else
                policy.WithMethods(corsSettings.AllowedMethods);

            if (corsSettings.AllowedHeaders.Contains("*"))
                policy.AllowAnyHeader();
            else
                policy.WithHeaders(corsSettings.AllowedHeaders);
        });
    });
}

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Etisalat SaaS Callback API",
        Version = "v1",
        Description = "API for handling ISV Provisioning Status callbacks with Etisalat XaaS platform",
        Contact = new OpenApiContact
        {
            Name = "imarouf",
            Email = "support@example.com"
        }
    });

    c.AddSecurityDefinition("Basic", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        Description = "Basic Authentication with Base64 encoded credentials"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Basic"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Etisalat SaaS Callback API v1");
    });
}

app.UseSerilogRequestLogging();

// Enable CORS if configured
var corsEnabled = app.Configuration.GetSection("Cors:Enabled").Get<bool>();
if (corsEnabled)
{
    app.UseCors("CorsPolicy");
}

app.UseStaticFiles();
app.UseRouting();

app.UseIpWhitelist();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapControllers();

Log.Information("Starting Etisalat SaaS Callback Service...");

// Ensure default admin user exists
var userService = app.Services.GetRequiredService<IUserService>();
await userService.EnsureDefaultAdminAsync();

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
