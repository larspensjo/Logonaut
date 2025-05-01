$ErrorActionPreference = 'Stop';

$packageName = $env:ChocolateyPackageName

# --- Find Uninstall Information ---
# Inno Setup typically registers under HKLM with the AppId or AppName + "_is1"
# Prioritize AppId if it exists, otherwise fallback to AppName. Check both 32-bit and 64-bit registry views.
$uninstallKeyBase = "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"
$appIdGuid = "3120345d-28c6-4671-ae58-0772533582b2" # From your .iss [Setup] AppId
$uninstallKeyNameAppId = "$($appIdGuid)_is1"
$uninstallKeyNameAppName = "$($packageName)_is1" # Fallback using AppName

$registryKeys = @(
    @{ Path = "HKLM:\$uninstallKeyBase\$uninstallKeyNameAppId"; Name = $uninstallKeyNameAppId },
    @{ Path = "HKCU:\$uninstallKeyBase\$uninstallKeyNameAppId"; Name = $uninstallKeyNameAppId },
    @{ Path = "HKLM:\$uninstallKeyBase\$uninstallKeyNameAppName"; Name = $uninstallKeyNameAppName },
    @{ Path = "HKCU:\$uninstallKeyBase\$uninstallKeyNameAppName"; Name = $uninstallKeyNameAppName },
    @{ Path = "HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyNameAppId"; Name = $uninstallKeyNameAppId },
    @{ Path = "HKCU:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyNameAppId"; Name = $uninstallKeyNameAppId },
    @{ Path = "HKLM:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyNameAppName"; Name = $uninstallKeyNameAppName },
    @{ Path = "HKCU:\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$uninstallKeyNameAppName"; Name = $uninstallKeyNameAppName }
)

$uninstallKey = $null
foreach ($keyInfo in $registryKeys) {
    Write-Verbose "Checking uninstall key: $($keyInfo.Path)"
    if (Test-Path $keyInfo.Path) {
        $uninstallKey = $keyInfo.Name
        Write-Host "Found uninstall key: $uninstallKey at $($keyInfo.Path)"
        break
    }
}

if ($null -eq $uninstallKey) {
    Write-Warning "Could not find uninstall registry key for $packageName. Attempting generic uninstall."
    # Fallback or error out - For InnoSetup, finding the key is preferred.
    # We'll let Uninstall-ChocolateyPackage try its best without a specific key if needed,
    # but it's less reliable.
}

# --- Uninstallation Arguments ---
$silentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"

# --- Uninstallation ---
$uninstallArgs = @{
    packageName = $packageName
    fileType    = 'exe'
    silentArgs  = $silentArgs
    # Pass the discovered key name; allows Chocolatey to find the uninstaller path more reliably
    # If $uninstallKey is $null, Chocolatey will try other methods.
    keyName     = $uninstallKey
}

Write-Host "Uninstalling $packageName..."
Uninstall-ChocolateyPackage @uninstallArgs
Write-Host "$packageName uninstalled."
