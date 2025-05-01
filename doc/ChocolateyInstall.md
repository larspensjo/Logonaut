Since Logonaut uses an Inno Setup installer (`.exe`), we'll create a package that downloads this installer and runs it silently.

**Core Concepts:**

1.  **`.nuspec` File:** XML metadata file describing the package (ID, version, dependencies, author, description, etc.).
2.  **`chocolateyInstall.ps1`:** PowerShell script that Chocolatey runs to perform the installation. It downloads the installer, verifies its checksum, and runs it silently.
3.  **`chocolateyUninstall.ps1`:** PowerShell script to perform the uninstallation.
4.  **Hosting:** You need a stable URL to host the `Logonaut-*.Setup.exe` file (e.g., GitHub Releases).

**Steps to Create the Chocolatey Package:**

1.  **Create Package Directory Structure:**
    Create a folder for your package source, conventionally named using the package ID (lowercase):
    ```
    logonaut.choco/
    ├── logonaut.nuspec
    └── tools/
        ├── chocolateyInstall.ps1
        └── chocolateyUninstall.ps1
        # Optional: LICENSE.txt, VERIFICATION.txt
    ```

2.  **Create `logonaut.nuspec` Metadata File:**
    This file defines your package. Replace placeholders like version numbers, URLs, and checksums as needed.

    *   **`id`:** Must be lowercase.
    *   **`version`:** Match your application/installer version.
    *   **`iconUrl`:** Needs to be a direct link to the *raw* icon file. GitHub doesn't serve raw files directly well. Use services like `raw.githack.com` or `jsdelivr.com` pointing to your icon in the repo (replace `COMMIT_HASH_OR_TAG`).
    *   **`dependencies`:** This is crucial. It tells Chocolatey that Logonaut needs the .NET 8 Desktop Runtime. `dotnet-desktopruntime` is the metapackage that should pull in the latest appropriate version. The version range `[8.0.0,9.0.0)` means "version 8.0.0 or higher, but less than 9.0.0".

3.  **Create `tools\chocolateyInstall.ps1`:**
    This script downloads and runs the installer.

    *   **`$url64`**: **Crucial:** Update this URL to point to the *actual* download location of your `Logonaut-1.0.0-Setup.exe` file (e.g., a GitHub Release asset).
    *   **`$checksum64`**: **Crucial:** Calculate the SHA256 checksum of your installer file and paste it here. Use `Get-FileHash .\Logonaut-1.0.0-Setup.exe -Algorithm SHA256 | Select-Object -ExpandProperty Hash`.
    *   **`$silentArgs`**: Uses standard Inno Setup silent flags. `/LOG` is optional but helpful for debugging. `/TASKS` controls optional components defined in the `[Tasks]` section of your `.iss`. We default to *not* associating files unless the user passes a parameter.
    *   **`Install-ChocolateyPackage`**: The core command to download, verify, and run the installer.

4.  **Create `tools\chocolateyUninstall.ps1`:**
    This script tells Chocolatey how to uninstall Logonaut using the registry information Inno Setup creates.

    *   **Uninstall Key:** Finding the correct registry key is important. Inno Setup usually uses `<AppId>_is1` or `<AppName>_is1`. The script tries the AppId first (from your `.iss`) and falls back to the AppName. It checks both HKLM and HKCU, and both 64-bit and 32-bit registry views. You should verify the actual key name created on your system after installing manually (`regedit`).
    *   **`Uninstall-ChocolateyPackage`**: The core command to run the uninstaller found via the registry key.

5.  **Host the Installer:**
    *   Build your Logonaut solution in **Release** mode.
    *   Compile the Inno Setup script (`LogonautInstaller.iss`) to generate `Logonaut-1.0.0-Setup.exe`.
    *   Create a **Release** on your GitHub repository (e.g., tag `v1.0.0`).
    *   **Upload** the `Logonaut-1.0.0-Setup.exe` file as a **binary asset** to that GitHub Release.
    *   Get the **stable download URL** for that asset and put it in `$url64` in `chocolateyInstall.ps1`.

6.  **Calculate Checksum:**
    *   Download the hosted `.exe` file.
    *   Open PowerShell and run:
        ```powershell
        Get-FileHash .\Logonaut-1.0.0-Setup.exe -Algorithm SHA256
        ```
    *   Copy the resulting `Hash` value and paste it into `$checksum64` in `chocolateyInstall.ps1`.

7.  **Test Locally:**
    *   Open PowerShell **as Administrator**.
    *   Navigate to the *parent* directory containing `logonaut.choco`.
    *   Pack the package: `choco pack .\logonaut.choco\logonaut.nuspec` (This creates `logonaut.1.0.0.nupkg`).
    *   Test installation: `choco install logonaut -s . -fdv` ( `-s .` uses the current directory as the source, `-f` forces reinstall if needed, `-d` enables debug output, `-v` enables verbose output).
    *   Test parameters: `choco install logonaut -s . -fdv --params "/AssociateFiles"`
    *   Test uninstallation: `choco uninstall logonaut -dv`
    *   Fix any errors in your `.ps1` or `.nuspec` files and repeat `choco pack` and `choco install/uninstall`.

8.  **Publish (Optional - Community Repository):**
    *   If you want to share this on the public Chocolatey Community Repository (chocolatey.org):
        *   Register for an account on chocolatey.org.
        *   Get your API key from your account page.
        *   Set your API key locally: `choco apikey -k YOUR_API_KEY -s https://push.chocolatey.org/`
        *   Push the package: `choco push logonaut.1.0.0.nupkg -s https://push.chocolatey.org/`
    *   **Moderation:** Your package will go through a human moderation process. Read the creation guidelines carefully (checksums, stable URLs, silent install, licensing, dependencies, etc.) to avoid rejection. [https://docs.chocolatey.org/en-us/create/create-packages](https://docs.chocolatey.org/en-us/create/create-packages)

9.  **Consider Automatic Updates (Advanced):**
    *   For easier maintenance, look into the Chocolatey Automatic Package Updater Module (AU). It can automatically check for new releases on GitHub, download the installer, calculate the checksum, update the scripts/nuspec, and push the new package version. This requires more setup but saves effort long-term.

This detailed process will help you create a robust Chocolatey package for Logonaut! Remember to replace placeholders and test thoroughly.