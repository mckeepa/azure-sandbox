namespace OnPrem_CSharp_WebApp.Configuration;

/// <summary>
/// Represents the settings needed to authenticate to Azure Key Vault with a client certificate.
/// </summary>
/// <remarks>
/// This example is intended to be easy to follow for other developers.
/// Written by Paul McKee.
/// </remarks>
public sealed class KeyVaultOptions
{
    public string? VaultUri { get; set; }
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? PemFilePath { get; set; }
    public string? PublicCertificateFilePath { get; set; }
    public bool UseWindowsCertificateStore { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificateStoreLocation { get; set; } = "CurrentUser";
    public string? CertificateStoreName { get; set; } = "My";
    public List<string>? SecretNames { get; set; }
    public string? SubscriptionId { get; set; }
    public string? VaultResourceId { get; set; }
}
