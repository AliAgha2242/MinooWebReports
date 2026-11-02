using MinooWebReports.Services;
using MinooWebReports.Persistence.StaticConfiguration;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
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
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.AddSingleton<IServerDirectoryService, ServerDirectoryService>();
builder.Services.AddScoped<ISelectedConnectionService, SelectedConnectionService>();
builder.Services.AddScoped<IConnectionStringFactory, SqlConnectionStringFactory>();
builder.Services.AddScoped<IUserReportSettingsService, UserReportSettingsService>();
builder.Services.AddScoped<IReportRepository, SqlReportRepository>();
builder.Services.AddScoped<IReportExecutionService, SqlReportExecutionService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
