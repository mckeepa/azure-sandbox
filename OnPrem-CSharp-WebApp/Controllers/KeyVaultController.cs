using System.Security.Cryptography.X509Certificates;
using OnPrem_CSharp_WebApp.Configuration;
using OnPrem_CSharp_WebApp.Services;

namespace OnPrem_CSharp_WebApp.Controllers;

public sealed class KeyVaultController
{
    private readonly IKeyVaultSecretService _secretService;
    private readonly KeyVaultOptions _options;
    private readonly KeyVaultPageView _view;

    public KeyVaultController(IKeyVaultSecretService secretService, KeyVaultOptions options, KeyVaultPageView view)
    {
        _secretService = secretService;
        _options = options;
        _view = view;
    }

    public async Task<IResult> Home()
    {
        string defaultSecretName = GetDefaultSecretName(_secretService.SecretNames);
        return Results.Content(_view.Render(defaultSecretName, _secretService.SecretNames, "Ready to read a secret."), "text/html");
    }

    public async Task<IResult> ReadSecret(HttpContext context)
    {
        try
        {
            var form = await context.Request.ReadFormAsync();
            string secretName = form["secretName"].ToString();
            string resolvedSecretName = string.IsNullOrWhiteSpace(secretName) ? GetDefaultSecretName(_secretService.SecretNames) : secretName;
            string secretValue = await _secretService.GetSecretValueAsync(resolvedSecretName);

            return Results.Content(
                _view.Render(resolvedSecretName, _secretService.SecretNames, $"Loaded secret '{resolvedSecretName}'.", secretValue),
                "text/html");
        }
        catch (Exception ex)
        {
            return Results.Content(
                _view.Render(GetDefaultSecretName(_secretService.SecretNames), _secretService.SecretNames, $"Unable to read the secret. {ex.Message}"),
                "text/html");
        }
    }

    public async Task<IResult> ListAllSecrets()
    {
        try
        {
            IReadOnlyDictionary<string, string> allSecrets = await _secretService.ListAccessibleSecretsAsync();
            return Results.Content(
                _view.Render(GetDefaultSecretName(_secretService.SecretNames), _secretService.SecretNames, "Listed the secrets available to this identity.", listedSecrets: allSecrets),
                "text/html");
        }
        catch (Exception ex)
        {
            return Results.Content(
                _view.Render(GetDefaultSecretName(_secretService.SecretNames), _secretService.SecretNames, $"Unable to list secrets. {ex.Message}"),
                "text/html");
        }
    }

    public async Task<IResult> CreateSecret(HttpContext context)
    {
        var form = await context.Request.ReadFormAsync();
        string secretName = form["secretName"].ToString() ?? string.Empty;
        string secretValue = form["secretValue"].ToString() ?? string.Empty;
        string readerName = form["readerName"].ToString() ?? string.Empty;
        string writerName = form["writerName"].ToString() ?? string.Empty;
        string defaultSecretName = GetDefaultSecretName(_secretService.SecretNames);

        if (string.IsNullOrWhiteSpace(secretName) || string.IsNullOrWhiteSpace(secretValue) || string.IsNullOrWhiteSpace(readerName) || string.IsNullOrWhiteSpace(writerName))
        {
            return Results.Content(_view.Render(defaultSecretName, _secretService.SecretNames, "Please complete all create-secret fields."), "text/html");
        }

        try
        {
            IKeyVaultAdminService? adminService = CreateAdminService(_options, _secretService);
            if (adminService is null)
            {
                return Results.Content(
                    _view.Render(defaultSecretName, _secretService.SecretNames, "Create secret is not configured. Add AzureKeyVault:SubscriptionId and AzureKeyVault:VaultResourceId to appsettings.json."),
                    "text/html");
            }

            await adminService.CreateSecretWithGranularAccessAsync(secretName, secretValue, readerName, writerName);
            string createdSecretValue = await _secretService.GetSecretValueAsync(secretName);

            return Results.Content(
                _view.Render(secretName, _secretService.SecretNames, $"Created and granted access for '{secretName}'.", createdSecretValue),
                "text/html");
        }
        catch (Exception ex)
        {
            return Results.Content(
                _view.Render(defaultSecretName, _secretService.SecretNames, $"Unable to create the secret. {ex.Message}"),
                "text/html");
        }
    }

    private static string GetDefaultSecretName(IReadOnlyList<string> secretNames)
    {
        return secretNames.FirstOrDefault(secretName => !string.IsNullOrWhiteSpace(secretName)) ?? "MySecretName";
    }

    private static IKeyVaultAdminService? CreateAdminService(KeyVaultOptions options, IKeyVaultSecretService secretService)
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
}
