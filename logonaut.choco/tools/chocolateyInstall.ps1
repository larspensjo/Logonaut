$ErrorActionPreference = 'Stop'; # Stop on error

$packageName = $env:ChocolateyPackageName
$toolsDir = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
# $fileLocation = Join-Path $toolsDir 'INSTALLER_FILE_NAME' # Use if embedding installer

# --- Installer Download ---
# !! REPLACE THESE WITH ACTUAL VALUES !!
$url64 = 'https://github.com/larspensjo/Logonaut/releases/download/v1.0.0/Logonaut-1.0.0-Setup.exe' # URL to your installer on GitHub releases
$checksum64 = '6D948664A512755D2F9E94E86B764097526E12CE3544CF60CFABB0AA9B804EDB' # Checksum of the installer file (use Get-FileHash)

# --- Installation Arguments ---
# Standard Inno Setup silent install arguments
$silentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=`"$($env:TEMP)\$($packageName).Install.log`""

# --- Handle Package Parameters (Optional) ---
# Example: Allow user to associate .log files via `choco install logonaut --params "/AssociateFiles"`
$packageParameters = $env:chocolateyPackageParameters
Write-Host "Package Parameters: $packageParameters"
if ($packageParameters -like '*/AssociateFiles*') {
    Write-Host "Adding /TASKS=""associate"" based on package parameters."
    $silentArgs += ' /TASKS="associate"' # Match the task name in your .iss file
} else {
     # Default: Install WITHOUT the association task
     $silentArgs += ' /TASKS="!associate"' # Exclude the task explicitly
}

# --- Installation ---
$installArgs = @{
    packageName   = $packageName
    fileType      = 'exe' # Installer type
    url64bit      = $url64
    checksum64    = $checksum64
    checksumType64= 'sha256' # Or checksumType if not using 64bit specific
    silentArgs    = $silentArgs
    validExitCodes= @(0) # Standard success code for installers
}

Write-Host "Installing $packageName..."
Install-ChocolateyPackage @installArgs
Write-Host "$packageName installed successfully."
