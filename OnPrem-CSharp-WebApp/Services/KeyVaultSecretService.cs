using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using OnPrem_CSharp_WebApp.Configuration;

namespace OnPrem_CSharp_WebApp.Services;

/// <summary>
/// Reads a secret from Azure Key Vault by authenticating with a client certificate.
/// </summary>
/// <remarks>
/// This is a simple implementation so it can serve as a reference example.
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
        string? configurationError = ValidateConfiguration(requireSecretNames: true);
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string secretName in SecretNames)
        {
            secrets[secretName] = await GetSecretValueAsync(secretName);
        }

        return secrets;
    }

    public async Task<IReadOnlyDictionary<string, string>> ListAccessibleSecretsAsync()
    {
        string? configurationError = ValidateConfiguration(requireSecretNames: false);
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        X509Certificate2? certificate = LoadCertificate();
        if (certificate is null)
        {
            throw new InvalidOperationException("The configured client certificate could not be loaded.");
        }

        var credential = new ClientCertificateCredential(_settings.TenantId!, _settings.ClientId!, certificate);
        var secretClient = new SecretClient(new Uri(_settings.VaultUri!), credential);

        try
        {
            var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await foreach (SecretProperties secretProperties in secretClient.GetPropertiesOfSecretsAsync())
            {
                if (string.IsNullOrWhiteSpace(secretProperties.Name))
                {
                    continue;
                }

                KeyVaultSecret secret = await secretClient.GetSecretAsync(secretProperties.Name);
                secrets[secretProperties.Name] = secret.Value ?? string.Empty;
            }

            return secrets;
        }
        catch (Exception ex) when (IsCertificateAuthenticationFailure(ex))
        {
            throw new InvalidOperationException(
                "Azure Entra rejected the client certificate. Upload the public certificate to the Microsoft Entra application registration and confirm that the private key matches the uploaded certificate.",
                ex);
        }
    }

    public async Task<string> GetSecretValueAsync(string secretName)
    {
        string? configurationError = ValidateConfiguration(requireSecretNames: true);
        if (!string.IsNullOrWhiteSpace(configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        X509Certificate2? certificate = LoadCertificate();
        if (certificate is null)
        {
            throw new InvalidOperationException("The configured client certificate could not be loaded.");
        }

        var credential = new ClientCertificateCredential(_settings.TenantId!, _settings.ClientId!, certificate);
        var secretClient = new SecretClient(new Uri(_settings.VaultUri!), credential);

        try
        {
            KeyVaultSecret secret = await secretClient.GetSecretAsync(secretName);
            return secret.Value;
        }
        catch (Exception ex) when (IsCertificateAuthenticationFailure(ex))
        {
            throw new InvalidOperationException(
                "Azure Entra rejected the client certificate. Upload the public certificate to the Microsoft Entra application registration and confirm that the private key matches the uploaded certificate.",
                ex);
        }
    }

    public X509Certificate2? LoadCertificate()
    {
        if (_settings.UseWindowsCertificateStore && OperatingSystem.IsWindows())
        {
            X509Certificate2? certificate = LoadCertificateFromWindowsStore();
            if (certificate is null)
            {
                throw new InvalidOperationException($"Certificate with thumbprint '{_settings.CertificateThumbprint}' was not found in the Windows certificate store.");
            }

            return certificate;
        }

        string? resolvedPemFilePath = ResolveCertificatePath(_settings.PemFilePath, _contentRootPath);
        if (string.IsNullOrWhiteSpace(resolvedPemFilePath) || !File.Exists(resolvedPemFilePath))
        {
            throw new InvalidOperationException($"Certificate file was not found. Checked: {_settings.PemFilePath}");
        }

        string? resolvedPublicCertificateFilePath = ResolveCertificatePath(_settings.PublicCertificateFilePath, _contentRootPath);
        X509Certificate2? certificateFromFiles = LoadCertificate(resolvedPemFilePath, resolvedPublicCertificateFilePath);
        if (certificateFromFiles is null)
        {
            throw new InvalidOperationException($"Certificate could not be loaded from {resolvedPemFilePath}.");
        }

        return certificateFromFiles;
    }

    private string? ValidateConfiguration(bool requireSecretNames)
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

        if (_settings.UseWindowsCertificateStore && OperatingSystem.IsWindows())
        {
            if (string.IsNullOrWhiteSpace(_settings.CertificateThumbprint))
            {
                return "AzureKeyVault:CertificateThumbprint is missing when UseWindowsCertificateStore is enabled.";
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_settings.PemFilePath))
            {
                return "AzureKeyVault:PemFilePath is missing.";
            }
        }

        if (requireSecretNames && (_settings.SecretNames is null || _settings.SecretNames.Count == 0 || _settings.SecretNames.Any(string.IsNullOrWhiteSpace)))
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

        // The configuration may contain either an absolute path or a relative path.
        // A relative path is resolved against several common deployment locations so the sample
        // works when the certificate is copied next to the app, placed in a certs folder, or
        // published into the runtime output directory.
        var candidates = new List<string>();

        if (Path.IsPathRooted(pemFilePath))
        {
            // Absolute paths are used as-is because they already describe the exact location.
            candidates.Add(pemFilePath);
        }
        else
        {
            // The candidates list provides a fallback chain for common deployment layouts.
            // This keeps the example robust without forcing every environment to use the same folder structure.
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

    private static bool IsCertificateAuthenticationFailure(Exception ex)
    {
        string message = ex.ToString();
        return message.Contains("AADSTS700027", StringComparison.OrdinalIgnoreCase)
            || message.Contains("certificate with identifier used to sign the client assertion", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid_client", StringComparison.OrdinalIgnoreCase);
    }

    private X509Certificate2? LoadCertificateFromWindowsStore()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_settings.CertificateThumbprint))
            {
                return null;
            }

            if (!Enum.TryParse<StoreLocation>(_settings.CertificateStoreLocation, true, out StoreLocation storeLocation))
            {
                storeLocation = StoreLocation.CurrentUser;
            }

            string storeName = string.IsNullOrWhiteSpace(_settings.CertificateStoreName) ? "My" : _settings.CertificateStoreName;
            using var store = new X509Store(storeName, storeLocation);
            store.Open(OpenFlags.ReadOnly);

            X509Certificate2Collection matches = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                _settings.CertificateThumbprint,
                validOnly: false);

            return matches.OfType<X509Certificate2>().FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Windows certificate load failed: {ex}");
            return null;
        }
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
                return X509CertificateLoader.LoadPkcs12FromFile(resolvedPemFilePath, string.Empty);
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
