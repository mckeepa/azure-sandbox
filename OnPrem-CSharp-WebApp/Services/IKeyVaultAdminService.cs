namespace OnPrem_CSharp_WebApp.Services;

public interface IKeyVaultAdminService
{
    Task CreateSecretWithGranularAccessAsync(
        string secretName,
        string secretValue,
        string readerName,
        string writerName,
        string readerRoleName = "Key Vault Secrets User",
        string writerRoleName = "Key Vault Secrets Officer");
}
