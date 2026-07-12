using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Authorization.Models;
using Microsoft.Graph;
using OnPrem_CSharp_WebApp.Services;

public class KeyVaultAdminService : IKeyVaultAdminService
{
    private readonly SecretClient _secretClient;
    private readonly ArmClient _armClient;
    private readonly GraphServiceClient _graphClient;
    private readonly string _vaultResourceId; 
    private readonly ResourceIdentifier _subscriptionScopeId;

    public KeyVaultAdminService(string tenantId, string clientId, System.Security.Cryptography.X509Certificates.X509Certificate2 certificate, string vaultUri, string vaultResourceId, string subscriptionId)
    {
        var credential = new ClientCertificateCredential(tenantId, clientId, certificate);
        
        _secretClient = new SecretClient(new Uri(vaultUri), credential);
        _armClient = new ArmClient(credential);
        _vaultResourceId = vaultResourceId;
        _subscriptionScopeId = new ResourceIdentifier($"/subscriptions/{subscriptionId}");

        // Initialize Graph Client for Entra ID lookups
        _graphClient = new GraphServiceClient(credential);
    }

    /// <summary>
    /// Creates a secret and dynamically looks up Roles and Principals by name to apply strict access control.
    /// </summary>
    /// <param name="secretName">Name of the secret to create</param>
    /// <param name="secretValue">Value of the secret</param>
    /// <param name="readerName">Display Name of SPN or User Principal Name (e.g. user@domain.com)</param>
    /// <param name="writerName">Display Name of SPN or User Principal Name (e.g. user@domain.com)</param>
    /// <param name="readerRoleName">Built-in role name (e.g., "Key Vault Secrets User")</param>
    /// <param name="writerRoleName">Built-in role name (e.g., "Key Vault Secrets Officer")</param>
    public async Task CreateSecretWithGranularAccessAsync(
        string secretName, 
        string secretValue, 
        string readerName, 
        string writerName,
        string readerRoleName = "Key Vault Secrets User",
        string writerRoleName = "Key Vault Secrets Officer")
    {
        // 1. Resolve human-readable Role Names to Azure Resource IDs
        ResourceIdentifier readerRoleId = await GetRoleDefinitionIdByNameAsync(readerRoleName);
        ResourceIdentifier writerRoleId = await GetRoleDefinitionIdByNameAsync(writerRoleName);

        // 2. Resolve human-readable Principal Names to Entra ID Object GUIDs
        Guid readerPrincipalId = await GetPrincipalIdByNameAsync(readerName);
        Guid writerPrincipalId = await GetPrincipalIdByNameAsync(writerName);

        // 3. Create or update the secret in Key Vault
        KeyVaultSecret newSecret = await _secretClient.SetSecretAsync(secretName, secretValue);

        // 4. Build the precise Azure Resource ID for this specific secret
        string secretResourceId = $"{_vaultResourceId}/secrets/{secretName}";
        ResourceIdentifier scopeId = new ResourceIdentifier(secretResourceId);

        // 5. Assign the Writer Role
        await AssignRoleAtScopeAsync(scopeId, writerPrincipalId, writerRoleId);

        // 6. Assign the Reader Role
        await AssignRoleAtScopeAsync(scopeId, readerPrincipalId, readerRoleId);
    }

    /// <summary>
    /// Queries Azure RBAC definitions to find the Role ID matching the human-readable string name.
    /// </summary>
    private async Task<ResourceIdentifier> GetRoleDefinitionIdByNameAsync(string roleName)
    {
        AuthorizationRoleDefinitionCollection roleDefinitions = _armClient.GetAuthorizationRoleDefinitions(_subscriptionScopeId);
        
        // Filter by role name directly in the Azure API call
        string filter = $"roleName eq '{roleName}'";
        
        await foreach (AuthorizationRoleDefinitionResource role in roleDefinitions.GetAllAsync(filter: filter))
        {
            return role.Data.Id; // Returns the full resource identifier path
        }

        throw new InvalidOperationException($"Azure RBAC Role '{roleName}' could not be found in this subscription.");
    }

    /// <summary>
    /// Queries Microsoft Entra ID to locate a User or Service Principal (SPN) by name and return its Unique Object ID.
    /// </summary>
    private async Task<Guid> GetPrincipalIdByNameAsync(string name)
    {
        // Case A: Test if it's a User via User Principal Name (email format)
        if (name.Contains("@"))
        {
            var user = await _graphClient.Users[name].GetAsync();
            if (user?.Id != null) return Guid.Parse(user.Id);
        }

        // Case B: Search Service Principals (Apps/Managed Identities) by Display Name
        var servicePrincipals = await _graphClient.ServicePrincipals
            .GetAsync(requestConfiguration => 
            {
                requestConfiguration.QueryParameters.Filter = $"displayName eq '{name}'";
            });

        var spn = servicePrincipals?.Value?.FirstOrDefault();
        if (spn?.Id != null) return Guid.Parse(spn.Id);

        // Case C: Search Users by Display Name if not using a UPN email format
        var users = await _graphClient.Users
            .GetAsync(requestConfiguration => 
            {
                requestConfiguration.QueryParameters.Filter = $"displayName eq '{name}'";
            });

        var userByDN = users?.Value?.FirstOrDefault();
        if (userByDN?.Id != null) return Guid.Parse(userByDN.Id);

        throw new InvalidOperationException($"Could not find an Entra ID User or Service Principal matching the name '{name}'.");
    }

    private async Task AssignRoleAtScopeAsync(ResourceIdentifier scopeId, Guid principalId, ResourceIdentifier roleDefinitionId)
    {
        string assignmentName = Guid.NewGuid().ToString();

        RoleAssignmentCreateOrUpdateContent content = new RoleAssignmentCreateOrUpdateContent(
            roleDefinitionId: roleDefinitionId,
            principalId: principalId
        );

        RoleAssignmentCollection roleAssignments = _armClient.GetRoleAssignments(scopeId);
        await roleAssignments.CreateOrUpdateAsync(WaitUntil.Completed, assignmentName, content);
    }
}
