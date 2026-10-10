using Microsoft.AspNetCore.Authentication.Cookies;
using RaceDay.Web.Filters;
using RaceDay.Web.Services;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

var apiProjectDirectory = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, "..", "RaceDay.API"));
var apiJwtConfigurationBuilder = new ConfigurationBuilder();
if (Directory.Exists(apiProjectDirectory))
{
    apiJwtConfigurationBuilder
        .SetBasePath(apiProjectDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile(
            $"appsettings.{builder.Environment.EnvironmentName}.json",
            optional: true,
            reloadOnChange: false);
}

var apiJwtConfiguration = apiJwtConfigurationBuilder
    .AddEnvironmentVariables()
    .Build();

var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseAddress)
    || (apiBaseAddress.Scheme != Uri.UriSchemeHttp
        && apiBaseAddress.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        "Configure Api:BaseUrl with an absolute HTTP or HTTPS URL before starting RaceDay.Web.");
}

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<ApiAuthenticationRedirectFilter>();
});
builder.Services.AddScoped<ApiAuthenticationRedirectFilter>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".RaceDay.Web.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.IdleTimeout = TimeSpan.FromMinutes(55);
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".RaceDay.Web.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = false;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<ApiAuthenticationSession>();
builder.Services.AddSingleton(new ApiJwtTokenValidator(apiJwtConfiguration));
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".RaceDay.Web.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddHttpClient<RaceDayApiClient>(client =>
{
    client.BaseAddress = apiBaseAddress;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
