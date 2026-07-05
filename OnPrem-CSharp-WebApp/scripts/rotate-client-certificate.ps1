[CmdletBinding()]
param(
    [string]$AppName = "OnPrem-CSharp-WebApp",
    [string]$Subject = "CN=OnPrem-CSharp-WebApp",
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$StoreLocation = 'CurrentUser',
    [ValidateSet('My')]
    [string]$StoreName = 'My',
    [string]$ServiceAccount = '',
    [int]$Days = 7,
    [string]$FriendlyName = 'OnPrem-CSharp-WebApp Client Certificate',
    [string]$AppId = '',
    [string]$TenantId = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Set-PrivateKeyAccess {
    param(
        [string]$Thumbprint,
        [string]$StoreLocationName,
        [string]$StoreNameName,
        [string]$Account
    )

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($StoreNameName, $StoreLocationName)
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

    try {
        $certificate = $store.Certificates | Where-Object { $_.Thumbprint -eq $Thumbprint } | Select-Object -First 1
        if (-not $certificate) {
            throw "Certificate with thumbprint $Thumbprint was not found in $StoreLocationName\\$StoreNameName."
        }

        $certUtilOutput = & certutil -store $StoreNameName $Thumbprint 2>&1 | Out-String
        $containerMatch = [regex]::Match($certUtilOutput, 'Container Name:\s*(.+)')
        if (-not $containerMatch.Success) {
            throw "Unable to determine the private key container name for thumbprint $Thumbprint."
        }

        $containerName = $containerMatch.Groups[1].Value.Trim()
        $keyFolder = if ($StoreLocationName -eq 'CurrentUser') {
            Join-Path $env:APPDATA 'Microsoft\Crypto\Keys'
        }
        else {
            Join-Path $env:ProgramData 'Microsoft\Crypto\RSA\MachineKeys'
        }

        $privateKeyPath = Join-Path $keyFolder $containerName
        if (-not (Test-Path $privateKeyPath)) {
            throw "Private key container not found at $privateKeyPath"
        }

        $acl = Get-Acl -Path $privateKeyPath
        $identity = New-Object System.Security.Principal.NTAccount($Account)
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, [System.Security.AccessControl.FileSystemRights]::Read, [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.SetAccessRule($accessRule)
        Set-Acl -Path $privateKeyPath -AclObject $acl
        Write-Host "Granted Read access to $Account on $privateKeyPath"
    }
    finally {
        $store.Close()
    }
}

$storeLocationPath = "Cert:\$StoreLocation\$StoreName"
if (-not (Test-Path $storeLocationPath)) {
    New-Item -ItemType Directory -Path $storeLocationPath -Force | Out-Null
}

$certificate = New-SelfSignedCertificate -CertStoreLocation $storeLocationPath -Subject $Subject -FriendlyName $FriendlyName -NotAfter (Get-Date).AddDays($Days) -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable

$thumbprint = $certificate.Thumbprint
$backupDirectory = Join-Path $env:ProgramData "OnPrem-CSharp-WebApp\certs"
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$pfxPath = Join-Path $backupDirectory ("{0}.pfx" -f $thumbprint)
$cerPath = Join-Path $backupDirectory ("{0}.cer" -f $thumbprint)

Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password (ConvertTo-SecureString -String 'changeit' -AsPlainText -Force) | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null

if ($ServiceAccount) {
    Set-PrivateKeyAccess -Thumbprint $thumbprint -StoreLocationName $StoreLocation -StoreNameName $StoreName -Account $ServiceAccount
}

Write-Host "Created certificate in $storeLocationPath"
Write-Host "Thumbprint: $thumbprint"
Write-Host "PFX backup: $pfxPath"
Write-Host "CER backup: $cerPath"

if ($AppId -and $TenantId) {
    Write-Host "Uploading public certificate to Microsoft Entra app registration $AppId"
    az ad app credential reset --id $AppId --append --display-name "$AppName-$((Get-Date).ToString('yyyyMMddHHmmss'))" --cert "@$cerPath" --output none | Out-Null
}
