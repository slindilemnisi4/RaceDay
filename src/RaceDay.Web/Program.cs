using RaceDay.Web.Services;

var builder = WebApplication.CreateBuilder(args);

var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiBaseAddress)
    || (apiBaseAddress.Scheme != Uri.UriSchemeHttp
        && apiBaseAddress.Scheme != Uri.UriSchemeHttps))
{
    throw new InvalidOperationException(
        "Configure Api:BaseUrl with an absolute HTTP or HTTPS URL before starting RaceDay.Web.");
}

builder.Services.AddControllersWithViews();
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
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
