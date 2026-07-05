using OnPrem_CSharp_WebApp.Configuration;
using OnPrem_CSharp_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<KeyVaultOptions>(builder.Configuration.GetSection("AzureKeyVault"));

var keyVaultOptions = builder.Configuration.GetSection("AzureKeyVault").Get<KeyVaultOptions>() ?? new KeyVaultOptions();
// Register the Key Vault service so the endpoint can stay focused on HTTP concerns.
builder.Services.AddSingleton(new KeyVaultSecretService(keyVaultOptions, builder.Environment.ContentRootPath, AppContext.BaseDirectory));

var app = builder.Build();

app.MapGet("/", async (HttpContext context, KeyVaultSecretService secretService) =>
{
    try
    {
        IReadOnlyDictionary<string, string> secrets = await secretService.GetSecretValuesAsync();
        var payload = new
        {
            status = "ok",
            secrets
        };

        return Results.Json(payload);
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return Results.Json(new
        {
            status = "error",
            message = $"Unable to read the secrets from Key Vault. {ex.Message}"
        });
    }
});

app.Run();
