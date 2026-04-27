using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Extensions.Configuration;
using MudBlazor;
using MudBlazor.Services;
using web.Components;
using web.Services.Auth;

var builder = WebApplication.CreateBuilder(args);

StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);

var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
{
    var appBaseConfiguration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
        .Build();

    apiBaseUrl = appBaseConfiguration["ApiBaseUrl"];
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(options =>
{
    options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
});

var configuredKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtectionKeysPath = string.IsNullOrWhiteSpace(configuredKeysPath)
    ? GetDefaultDataProtectionKeysPath(builder.Environment)
    : configuredKeysPath;

Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("MediaCloud.Web")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<ProtectedSessionStorage>();
builder.Services.AddScoped<AuthTokenStore>();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());
builder.Services.AddScoped<ApiAuthClient>();
builder.Services.AddScoped<AuthorizedApiRequestFactory>();
builder.Services.AddTransient<BearerTokenHandler>();

builder.Services.AddHttpClient("MediaCloudApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl ?? "http://127.0.0.1:5199");
}).AddHttpMessageHandler<BearerTokenHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("MediaCloudApi"));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

static string GetDefaultDataProtectionKeysPath(IHostEnvironment environment)
{
    var contentRootPath = Path.GetFullPath(environment.ContentRootPath);
    if (environment.IsDevelopment())
    {
        return Path.Combine(contentRootPath, "DataProtectionKeys");
    }

    return IsContainerContentRoot(contentRootPath)
        ? "/app/data/dpkeys"
        : Path.Combine(contentRootPath, "DataProtectionKeys");
}

static bool IsContainerContentRoot(string contentRootPath)
    => string.Equals(contentRootPath, "/app", StringComparison.OrdinalIgnoreCase)
       || contentRootPath.StartsWith("/app/", StringComparison.OrdinalIgnoreCase);

app.Run();
