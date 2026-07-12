using System.Security.Cryptography.X509Certificates;

namespace OnPrem_CSharp_WebApp.Services;

public interface IKeyVaultSecretService
{
    IReadOnlyList<string> SecretNames { get; }

    Task<IReadOnlyDictionary<string, string>> GetSecretValuesAsync();

    Task<IReadOnlyDictionary<string, string>> ListAccessibleSecretsAsync();

    Task<string> GetSecretValueAsync(string secretName);

    X509Certificate2? LoadCertificate();
}
