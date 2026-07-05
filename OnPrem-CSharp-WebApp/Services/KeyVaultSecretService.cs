using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using OnPrem_CSharp_WebApp.Configuration;

namespace OnPrem_CSharp_WebApp.Services;

/// <summary>
/// Reads a secret from Azure Key Vault by authenticating with a client certificate.
/// </summary>
/// <remarks>
/// This is intentionally kept simple so it can serve as a reference example.
/// Written by Paul McKee.
/// </remarks>
public sealed class KeyVaultSecretService
{
    private readonly KeyVaultOptions _settings;
    private readonly string _contentRootPath;
    private readonly string _baseDirectory;

    public KeyVaultSecretService(KeyVaultOptions settings, string contentRootPath, string baseDirectory)
    {
        _settings = settings;
        _contentRootPath = contentRootPath;
        _baseDirectory = baseDirectory;
    }

    public IReadOnlyList<string> SecretNames => _settings.SecretNames ?? new List<string>();

    /// <summary>
    /// Loads the configured secrets from Azure Key Vault.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetSecretValuesAsync()
    {
        string? configurationError = ValidateConfiguration();
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        string? resolvedPemFilePath = ResolveCertificatePath(_settings.PemFilePath, _contentRootPath);
        if (string.IsNullOrWhiteSpace(resolvedPemFilePath) || !File.Exists(resolvedPemFilePath))
        {
            throw new InvalidOperationException($"Certificate file was not found. Checked: {_settings.PemFilePath}");
        }

        string? resolvedPublicCertificateFilePath = ResolveCertificatePath(_settings.PublicCertificateFilePath, _contentRootPath);
        X509Certificate2? certificate = LoadCertificate(resolvedPemFilePath, resolvedPublicCertificateFilePath);
        if (certificate is null)
        {
            throw new InvalidOperationException($"Certificate could not be loaded from {resolvedPemFilePath}.");
        }

        var credential = new ClientCertificateCredential(_settings.TenantId!, _settings.ClientId!, certificate);
        var secretClient = new SecretClient(new Uri(_settings.VaultUri!), credential);

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string secretName in SecretNames)
        {
            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
            secrets[secretName] = secret.Value;
        }

        return secrets;
    }

    private string? ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.VaultUri))
        {
            return "AzureKeyVault:VaultUri is missing.";
        }

        if (string.IsNullOrWhiteSpace(_settings.TenantId))
        {
            return "AzureKeyVault:TenantId is missing.";
        }

        if (string.IsNullOrWhiteSpace(_settings.ClientId))
        {
            return "AzureKeyVault:ClientId is missing.";
        }

        if (string.IsNullOrWhiteSpace(_settings.PemFilePath))
        {
            return "AzureKeyVault:PemFilePath is missing.";
        }

        if (_settings.SecretNames is null || _settings.SecretNames.Count == 0 || _settings.SecretNames.Any(string.IsNullOrWhiteSpace))
        {
            return "AzureKeyVault:SecretNames is missing or contains an empty value.";
        }

        return null;
    }

    /// <summary>
    /// Resolves a configured file path against the application content root and runtime base directory.
    /// </summary>
    private string? ResolveCertificatePath(string? pemFilePath, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(pemFilePath))
        {
            return null;
        }

        var candidates = new List<string>();

        if (Path.IsPathRooted(pemFilePath))
        {
            candidates.Add(pemFilePath);
        }
        else
        {
            candidates.Add(Path.GetFullPath(Path.Combine(contentRootPath, pemFilePath)));
            candidates.Add(Path.GetFullPath(Path.Combine(contentRootPath, "certs", Path.GetFileName(pemFilePath))));
            candidates.Add(Path.GetFullPath(Path.Combine(contentRootPath, "..", "certs", Path.GetFileName(pemFilePath))));
            candidates.Add(Path.GetFullPath(Path.Combine(_baseDirectory, pemFilePath)));
            candidates.Add(Path.GetFullPath(Path.Combine(_baseDirectory, Path.GetFileName(pemFilePath))));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Creates an X509 certificate from the configured private key and, when needed, the public certificate.
    /// </summary>
    /// <remarks>
    /// The public certificate is required here because the current example uses separate PEM files for the private key and certificate.
    /// </remarks>
    private X509Certificate2? LoadCertificate(string resolvedPemFilePath, string? resolvedPublicCertificateFilePath)
    {
        try
        {
            if (resolvedPemFilePath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase) ||
                resolvedPemFilePath.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
            {
                return new X509Certificate2(resolvedPemFilePath);
            }

            if (!string.IsNullOrWhiteSpace(resolvedPublicCertificateFilePath))
            {
                return X509Certificate2.CreateFromPemFile(resolvedPublicCertificateFilePath, resolvedPemFilePath);
            }

            return X509Certificate2.CreateFromPemFile(resolvedPemFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Certificate load failed: {ex}");
            return null;
        }
    }
}
