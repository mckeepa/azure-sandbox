using OnPrem_CSharp_WebApp.Configuration;
using OnPrem_CSharp_WebApp.Controllers;
using OnPrem_CSharp_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<KeyVaultOptions>(builder.Configuration.GetSection("AzureKeyVault"));

var keyVaultOptions = builder.Configuration.GetSection("AzureKeyVault").Get<KeyVaultOptions>() ?? new KeyVaultOptions();

builder.Services.AddSingleton(keyVaultOptions);
builder.Services.AddSingleton<IKeyVaultSecretService>(sp =>
{
    IHostEnvironment environment = sp.GetRequiredService<IHostEnvironment>();
    return new KeyVaultSecretService(keyVaultOptions, environment.ContentRootPath, AppContext.BaseDirectory);
});
builder.Services.AddSingleton<KeyVaultPageView>();
builder.Services.AddSingleton<KeyVaultController>();

var app = builder.Build();

app.MapGet("/", async (KeyVaultController controller) => await controller.Home());
app.MapPost("/secrets/read", async (HttpContext context, KeyVaultController controller) => await controller.ReadSecret(context));
app.MapPost("/secrets/list-all", async (KeyVaultController controller) => await controller.ListAllSecrets());
app.MapPost("/secrets/create", async (HttpContext context, KeyVaultController controller) => await controller.CreateSecret(context));

app.Run();
