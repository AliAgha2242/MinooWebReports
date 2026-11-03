using MinooWebReports.Services;
using MinooWebReports.Persistence.StaticConfiguration;
using MinooWebReports.Security;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IAuthenticateService, AuthenticateService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".MinooWebReports.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = async context =>
        {
            var parentLogin = builder.Configuration["Sso:Parent:LoginUrl"];
            var launchPath = builder.Configuration["Sso:Parent:LaunchPath"] ?? "/Account/LaunchReports";

            // Prefer the parent SSO only when its login endpoint is reachable.
            // If Core is stopped or unreachable, fall back to the local Reports login.
            if (Uri.TryCreate(parentLogin, UriKind.Absolute, out var parentUri) &&
                (parentUri.Scheme == Uri.UriSchemeHttp || parentUri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                        context.HttpContext.RequestAborted);
                    timeout.CancelAfter(TimeSpan.FromSeconds(2));

                    var clients = context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
                    using var probe = await clients.CreateClient("MinooWebCore")
                        .GetAsync(parentUri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                    if (probe.IsSuccessStatusCode)
                    {
                        var separator = parentLogin!.Contains('?') ? "&" : "?";
                        context.Response.Redirect(
                            $"{parentLogin}{separator}returnUrl={Uri.EscapeDataString(launchPath)}");
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                    // Parent is unavailable; continue to local login.
                }
                catch (TaskCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    // Parent health probe timed out; continue to local login.
                }
            }

            context.Response.Redirect(context.RedirectUri);
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient("MinooWebCore", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpContextAccessor();

var minooConfigFileName = builder.Configuration["MinooConfiguration:FileName"] ?? "AppConfig.json";
var minooConfigFilePath = builder.Configuration["MinooConfiguration:FilePath"];
builder.Services.AddSingleton<IMinooConfiguration>(_ =>
    new MinooConfiguration(minooConfigFilePath, minooConfigFileName));

var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? "rabbit-srv:6379,abortConnect=false";

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var options = ConfigurationOptions.Parse(redisConnection);
    options.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
    options.InstanceName = "MinooWebReports:";
});

builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".MinooWebReports.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddSingleton<IServerDirectoryService, ServerDirectoryService>();
builder.Services.AddScoped<ICompanyAppLocationService, CompanyAppLocationService>();
builder.Services.AddScoped<ISelectedConnectionService, SelectedConnectionService>();
builder.Services.AddScoped<IConnectionStringFactory, SqlConnectionStringFactory>();
builder.Services.AddScoped<IUserReportSettingsService, UserReportSettingsService>();
builder.Services.AddScoped<IReportRepository, SqlReportRepository>();
builder.Services.AddScoped<IReportExecutionService, SqlReportExecutionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
