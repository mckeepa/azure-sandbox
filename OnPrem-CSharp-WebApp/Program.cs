using System.Security.Cryptography.X509Certificates;
using OnPrem_CSharp_WebApp.Configuration;
using OnPrem_CSharp_WebApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.Configure<KeyVaultOptions>(builder.Configuration.GetSection("AzureKeyVault"));

var keyVaultOptions = builder.Configuration.GetSection("AzureKeyVault").Get<KeyVaultOptions>() ?? new KeyVaultOptions();
var contentRootPath = builder.Environment.ContentRootPath;
var baseDirectory = AppContext.BaseDirectory;

builder.Services.AddSingleton(new KeyVaultSecretService(keyVaultOptions, contentRootPath, baseDirectory));

var app = builder.Build();

app.MapGet("/", async (KeyVaultSecretService secretService) =>
{
    string defaultSecretName = GetDefaultSecretName(secretService.SecretNames);
    return Results.Content(BuildHtmlPage(defaultSecretName, secretService.SecretNames, "Ready to read a secret."), "text/html");
});

app.MapPost("/secrets/read", async (HttpContext context, KeyVaultSecretService secretService) =>
{
    try
    {
        var form = await context.Request.ReadFormAsync();
        string secretName = form["secretName"].ToString();
        string resolvedSecretName = string.IsNullOrWhiteSpace(secretName) ? GetDefaultSecretName(secretService.SecretNames) : secretName;
        string secretValue = await secretService.GetSecretValueAsync(resolvedSecretName);

        return Results.Content(
            BuildHtmlPage(resolvedSecretName, secretService.SecretNames, $"Loaded secret '{resolvedSecretName}'.", secretValue),
            "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            BuildHtmlPage(GetDefaultSecretName(secretService.SecretNames), secretService.SecretNames, $"Unable to read the secret. {ex.Message}"),
            "text/html");
    }
});

app.MapPost("/secrets/list-all", async (KeyVaultSecretService secretService) =>
{
    try
    {
        IReadOnlyDictionary<string, string> allSecrets = await secretService.ListAccessibleSecretsAsync();
        return Results.Content(
            BuildHtmlPage(GetDefaultSecretName(secretService.SecretNames), secretService.SecretNames, "Listed the secrets available to this identity.", listedSecrets: allSecrets),
            "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            BuildHtmlPage(GetDefaultSecretName(secretService.SecretNames), secretService.SecretNames, $"Unable to list secrets. {ex.Message}"),
            "text/html");
    }
});

app.MapPost("/secrets/create", async (HttpContext context, KeyVaultSecretService secretService) =>
{
    var form = await context.Request.ReadFormAsync();
    string secretName = form["secretName"].ToString() ?? string.Empty;
    string secretValue = form["secretValue"].ToString() ?? string.Empty;
    string readerName = form["readerName"].ToString() ?? string.Empty;
    string writerName = form["writerName"].ToString() ?? string.Empty;
    string defaultSecretName = GetDefaultSecretName(secretService.SecretNames);

    if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretValue) || string.IsNullOrWhiteSpace(readerName) || string.IsNullOrWhiteSpace(writerName))
    {
        return Results.Content(BuildHtmlPage(defaultSecretName, secretService.SecretNames, "Please complete all create-secret fields."), "text/html");
    }

    try
    {
        KeyVaultAdminService? adminService = CreateAdminService(keyVaultOptions, secretService);
        if (adminService is null)
        {
            return Results.Content(
                BuildHtmlPage(defaultSecretName, secretService.SecretNames, "Create secret is not configured. Add AzureKeyVault:SubscriptionId and AzureKeyVault:VaultResourceId to appsettings.json."),
                "text/html");
        }

        await adminService.CreateSecretWithGranularAccessAsync(secretName, secretValue, readerName, writerName);
        string createdSecretValue = await secretService.GetSecretValueAsync(secretName);

        return Results.Content(
            BuildHtmlPage(secretName, secretService.SecretNames, $"Created and granted access for '{secretName}'.", createdSecretValue),
            "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            BuildHtmlPage(defaultSecretName, secretService.SecretNames, $"Unable to create the secret. {ex.Message}"),
            "text/html");
    }
});

app.Run();

static string GetDefaultSecretName(IReadOnlyList<string> secretNames)
{
    return secretNames.FirstOrDefault(secretName => !string.IsNullOrWhiteSpace(secretName)) ?? "MySecretName";
}

static string BuildHtmlPage(string? selectedSecretName, IReadOnlyList<string> secretNames, string? message, string? secretValue = null, IReadOnlyDictionary<string, string>? listedSecrets = null)
{
    string resultMarkup = string.IsNullOrWhiteSpace(secretValue)
        ? string.Empty
        : $"<div class=\"result\"><h3>Secret value</h3><pre>{Html(secretValue)}</pre></div>";

    string listMarkup = listedSecrets is null || listedSecrets.Count == 0
        ? string.Empty
        : $"<div class=\"result\"><h3>Accessible secrets</h3><ul>{string.Join(Environment.NewLine, listedSecrets.Select(secret => "<li><strong>" + Html(secret.Key) + "</strong>: " + Html(secret.Value) + "</li>"))}</ul></div>";

    string statusMarkup = string.IsNullOrWhiteSpace(message)
        ? "<p class=\"status\">Ready to read a secret.</p>"
        : $"<p class=\"status\">{Html(message)}</p>";

    string template = """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Azure Key Vault Demo</title>
    <style>
        body { font-family: Arial, sans-serif; margin: 2rem; background: #f7f9fc; color: #1f2937; }
        main { max-width: 900px; margin: 0 auto; background: white; padding: 2rem; border-radius: 12px; box-shadow: 0 12px 30px rgba(0,0,0,0.08); }
        h1, h2 { margin-bottom: 0.25rem; }
        form { display: grid; gap: 0.75rem; margin-bottom: 1.25rem; padding: 1rem; border: 1px solid #e5e7eb; border-radius: 8px; }
        .row { display: flex; gap: 1rem; flex-wrap: wrap; }
        label { font-weight: 600; }
        input, textarea { padding: 0.6rem; border: 1px solid #cbd5e1; border-radius: 6px; font-size: 1rem; }
        button { padding: 0.65rem 1rem; border: none; border-radius: 6px; background: #2563eb; color: white; cursor: pointer; }
        .status { padding: 0.75rem 1rem; background: #eff6ff; border-left: 4px solid #2563eb; border-radius: 6px; }
        .result { margin-top: 1rem; padding: 1rem; background: #f8fafc; border-radius: 8px; }
        pre { white-space: pre-wrap; word-break: break-word; }
    </style>
</head>
<body>
    <main>
        <h1>Azure Key Vault Demo</h1>
        <p>Read a configured secret or create a new one with scoped access.</p>
        __STATUS__

        <section>
            <h2>Read a secret</h2>
            <form method="post" action="/secrets/read">
                <label for="secretName">Secret name</label>
                <input id="secretName" name="secretName" value="__SELECTED_SECRET__" />
                <div class="row">
                    <button type="submit">Read selected secret</button>
                </div>
            </form>
            <form method="post" action="/secrets/read">
                <input type="hidden" name="secretName" value="__SELECTED_SECRET__" />
                <button type="submit">Read default secret</button>
            </form>
        </section>

        <section>
            <h2>List all accessible secrets</h2>
            <form method="post" action="/secrets/list-all">
                <button type="submit">List all accessible secrets</button>
            </form>
            __LIST_MARKUP__
        </section>

        <section>
            <h2>Create a secret</h2>
            <form method="post" action="/secrets/create">
                <label for="createSecretName">Secret name</label>
                <input id="createSecretName" name="secretName" placeholder="my-new-secret" />

                <label for="createSecretValue">Secret value</label>
                <textarea id="createSecretValue" name="secretValue" rows="4" placeholder="Secret value"></textarea>

                <label for="readerName">Reader name</label>
                <input id="readerName" name="readerName" placeholder="user@contoso.com or app display name" />

                <label for="writerName">Writer name</label>
                <input id="writerName" name="writerName" placeholder="user@contoso.com or app display name" />

                <button type="submit">Create secret</button>
            </form>
        </section>

        __RESULT_MARKUP__
    </main>
</body>
</html>
""";

    return template
        .Replace("__STATUS__", statusMarkup)
        .Replace("__SELECTED_SECRET__", Html(selectedSecretName))
        .Replace("__LIST_MARKUP__", listMarkup)
        .Replace("__RESULT_MARKUP__", resultMarkup);
}

static KeyVaultAdminService? CreateAdminService(KeyVaultOptions options, KeyVaultSecretService secretService)
{
    if (string.IsNullOrWhiteSpace(options.TenantId)
        || string.IsNullOrWhiteSpace(options.ClientId)
        || string.IsNullOrWhiteSpace(options.VaultUri)
        || string.IsNullOrWhiteSpace(options.SubscriptionId)
        || string.IsNullOrWhiteSpace(options.VaultResourceId))
    {
        return null;
    }

    X509Certificate2? certificate = secretService.LoadCertificate();
    if (certificate is null)
    {
        return null;
    }

    return new KeyVaultAdminService(options.TenantId, options.ClientId, certificate, options.VaultUri, options.VaultResourceId, options.SubscriptionId);
}

static string Html(string? value)
{
    return string.IsNullOrEmpty(value) ? string.Empty : System.Net.WebUtility.HtmlEncode(value);
}
