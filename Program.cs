using MinooWebReports.Services;
using MinooWebReports.Persistence.StaticConfiguration;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".MinooWebReports.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            var parentLogin = builder.Configuration["Sso:Parent:LoginUrl"];
            var launchPath = builder.Configuration["Sso:Parent:LaunchPath"] ?? "/Account/LaunchReports";
            if (string.IsNullOrWhiteSpace(parentLogin))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            var separator = parentLogin.Contains('?') ? "&" : "?";
            context.Response.Redirect($"{parentLogin}{separator}returnUrl={Uri.EscapeDataString(launchPath)}");
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("MinooWebCore");
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
