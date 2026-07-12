# Azure Key Vault On-Prem Demo

This repository contains a small .NET web application that demonstrates a secure way for on-premises workloads to consume secrets from Azure Key Vault without relying on shared secrets or embedded passwords.

The sample is intentionally developer-friendly and practical:
- It exposes a simple browser UI at the root URL.
- It supports read, list, and create workflows for Key Vault secrets.
- It uses certificate-based authentication to Microsoft Entra ID.
- It enforces least-privilege access so different workloads only receive access to the secrets they need.

## What this app does
The application can:
- Read a specific secret from Azure Key Vault.
- List the secrets an identity is permitted to access.
- Create a new secret and assign reader/writer access at the secret scope.

It is designed for a scenario where an on-premises host needs to retrieve secrets from Azure Key Vault in a controlled and auditable way.

## Security model
This solution is based on the following principles:
- Shared secrets are not allowed for Key Vault access.
- Authentication must use certificates (or other strong workload identity mechanisms) rather than client secrets or connection strings.
- At least two service identities are expected:
  - A reader identity for workloads that only need to read secrets.
  - A writer identity for workloads that create or manage secrets.
- Access should be scoped tightly to the secrets each workload needs. In practice, this means applying Azure RBAC or equivalent secret-level access control so each workload can access only the secrets relevant to its purpose.

## Repository layout
```text
OnPrem-CSharp-WebApp/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Test.json
├── appsettings.Production.json
├── Configuration/
│   └── KeyVaultOptions.cs
├── Services/
│   ├── KeyVaultAdminService.cs
│   └── KeyVaultSecretService.cs
├── scripts/
│   ├── rotate-client-certificate.sh
│   └── rotate-client-certificate.ps1
└── Properties/
    └── launchSettings.json
```

## Prerequisites
- .NET 10 SDK
- An Azure subscription
- An Azure Key Vault instance
- Microsoft Entra ID access to create or configure application registrations / service principals
- A certificate for client authentication
- Access to Azure CLI or the Azure portal for role assignment and configuration

## Quick start
1. Build the app:
   ```bash
   dotnet build OnPrem-CSharp-WebApp/OnPrem-CSharp-WebApp.csproj
   ```
2. Run the app:
   ```bash
   dotnet run --project OnPrem-CSharp-WebApp/OnPrem-CSharp-WebApp.csproj
   ```
3. Open the UI in your browser:
   ```text
   https://localhost:7017/
   ```

The app also exposes these endpoints:
- POST /secrets/read
- POST /secrets/list-all
- POST /secrets/create

## Configuration
The app reads its settings from appsettings files and environment variables. The most important values are under the AzureKeyVault section:

```json
{
  "AzureKeyVault": {
    "VaultUri": "https://example-vault.vault.azure.net",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "ClientId": "11111111-1111-1111-1111-111111111111",
    "PemFilePath": "certs/private.pem",
    "PublicCertificateFilePath": "certs/public.crt",
    "UseWindowsCertificateStore": false,
    "CertificateThumbprint": "",
    "CertificateStoreLocation": "CurrentUser",
    "CertificateStoreName": "My",
    "SecretNames": [
      "MySecretName"
    ]
  }
}
```

## Authentication and identity model
This sample expects certificate-based authentication to Microsoft Entra ID.

### Required identities
You should plan for at least two identities:
- Reader identity: used by workloads that only need to read secrets.
- Writer identity: used by automation or deployment workflows that create secrets and grant access.

These identities should be represented as Entra application registrations or service principals, not as shared client secrets.

### Certificate requirement
The private key must remain on the trusted on-premises host. The public certificate is uploaded to the Entra application registration so Azure can validate the certificate-based client assertion.

This sample explicitly assumes that certificate-based authentication is the approved method. Shared secrets are not part of the design.

## Azure Key Vault access design
Use Azure RBAC at the secret scope whenever possible. The goal is to give each workload access only to the specific secrets it needs.

Recommended pattern:
- Reader identity gets the Key Vault Secrets User role on only the secrets it needs to read.
- Writer identity gets the Key Vault Secrets Officer role on the secrets it needs to create or manage.
- Avoid granting broad vault-level access when a secret-specific assignment is enough.

The sample includes logic to create a secret and assign roles at the specific secret scope so that reader and writer access can be granted independently.

## Setup steps
### 1. Create the Entra application identities
Create or identify the reader and writer service principals in Microsoft Entra ID.

### 2. Upload the public certificates
Generate or rotate a certificate and upload the public portion to the corresponding Entra application registration.

### 3. Enable Azure RBAC on the Key Vault
Use Azure RBAC instead of legacy access policies where possible.

### 4. Assign secret-scoped permissions
Grant:
- Reader identity: Key Vault Secrets User on the target secret(s)
- Writer identity: Key Vault Secrets Officer on the target secret(s)

This keeps workloads isolated and prevents unnecessary access.

### 5. Configure the app
Populate the AzureKeyVault section with the vault URI, tenant ID, client ID, and certificate settings.

### 6. Run and use the app
Once configured, the app can be launched locally and used through the browser UI or by posting to the API endpoints.

## Automation expectations
All setup and operational workflows should be automated where possible.

This includes:
- Certificate creation and rotation
- Certificate upload to Microsoft Entra ID
- Application configuration provisioning
- Secret creation and role assignment
- Secret read workflows for runtime consumption

The repository includes helper scripts for certificate lifecycle management and the app itself supports automated creation and assignment of access for secrets.

## Certificate workflow
### Linux
```bash
openssl req -x509 -newkey rsa:2048 -nodes \
  -days 365 \
  -subj "/CN=OnPremKeyVaultApp" \
  -keyout private.pem \
  -out public.crt
```

### Windows
Use the PowerShell helper script to create or rotate a certificate in the Windows certificate store.

## Helper scripts
- [OnPrem-CSharp-WebApp/scripts/rotate-client-certificate.sh](OnPrem-CSharp-WebApp/scripts/rotate-client-certificate.sh) creates or rotates a certificate on Linux.
- [OnPrem-CSharp-WebApp/scripts/rotate-client-certificate.ps1](OnPrem-CSharp-WebApp/scripts/rotate-client-certificate.ps1) creates or rotates a certificate on Windows.

## How the runtime flow works
1. The application loads configuration from appsettings files and environment variables.
2. It resolves the configured certificate source from a file or Windows certificate store.
3. It creates a client certificate credential for Microsoft Entra ID.
4. It authenticates to Azure Key Vault using that credential.
5. It reads or creates secrets according to the requested workflow.
6. It returns the result through the browser UI or the HTTP endpoints.

## Operational notes
- The private key stays on the on-premises machine and is never sent to Azure.
- The public certificate is uploaded to the app registration so Entra ID can validate the identity.
- Secret-level RBAC is preferred over broad vault-wide access.
- The design is intended to support automated deployment and runtime operations rather than manual secret handling.

## Troubleshooting
- If authentication fails, verify that the public certificate was uploaded to the correct Entra application registration and that the private key matches.
- If a secret cannot be read, confirm that the reader identity has access to that specific secret.
- If secret creation fails, confirm that the writer identity has the required role assignment and that the Key Vault and subscription settings are correct.
