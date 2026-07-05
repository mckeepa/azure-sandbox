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
    public List<string>? SecretNames { get; set; }
}
