; -- Logonaut Inno Setup Script --
; Requirements:
; - Inno Setup Compiler installed (https://jrsoftware.org/isinfo.php)
; - Logonaut built in Release mode (adjust Source paths if needed)
; - Icon file present at src\Logonaut.UI\Assets\Logonaut.ico
; - WizardImage.bmp and WizardSmallImage.bmp present alongside this script

[Setup]
; --- Basic Application Info (CHANGE THESE!) ---
AppName=Logonaut
AppVersion=1.0.0
AppPublisher=Lars Pensjö
AppPublisherURL=https://github.com/larspensjo/Logonaut
AppId=3120345d-28c6-4671-ae58-0772533582b2
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

; --- Installation Defaults ---
DefaultDirName={autopf}\Logonaut
DefaultGroupName=Logonaut
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Logonaut-1.0.0-Setup
SetupIconFile=src\Logonaut.UI\Assets\Logonaut.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=WizardImage.bmp
WizardSmallImageFile=WizardSmallImage.bmp
UninstallDisplayIcon={app}\Logonaut.UI.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "associate"; Description: "Associate .log files with Logonaut"; GroupDescription: "File Associations:"; Flags: unchecked

[Files]
; --- Application Executable ---
Source: "src\Logonaut.UI\bin\Release\net8.0-windows\Logonaut.UI.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "src\Logonaut.UI\bin\Release\net8.0-windows\Logonaut.UI.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "src\Logonaut.UI\bin\Release\net8.0-windows\Logonaut.UI.deps.json"; DestDir: "{app}"; Flags: ignoreversion

; --- Required DLLs (adjust wildcard/paths if needed) ---
; Copy all DLLs from the output directory. Refine if needed to exclude unnecessary ones.
Source: "src\Logonaut.UI\bin\Release\net8.0-windows\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

; --- Other necessary files (e.g., config if any, specific assets) ---
; Example: Source: "src\Logonaut.UI\bin\Release\net8.0-windows\YourApp.exe.config"; DestDir: "{app}"; Flags: ignoreversion

; NOTE: Ensure the .NET Runtime is handled by the [Code] section check, not deployed here unless self-contained.

[Icons]
Name: "{group}\Logonaut"; Filename: "{app}\Logonaut.UI.exe"

[Registry]
; --- Optional File Association for .log files ---
; Root key HKEY_CLASSES_ROOT (HKCR)
Root: HKCR; Subkey: ".log"; ValueType: string; ValueName: ""; ValueData: "Logonaut.LogFile"; Tasks: associate; Flags: uninsdeletevalue

[Run]
; Optional: Launch after install
Filename: "{app}\Logonaut.UI.exe"; Description: "{cm:LaunchProgram,Logonaut}"; Flags: nowait postinstall skipifsilent

[Code]

const
  KEY_WOW64_64KEY = $0100;
  KEY_ENUMERATE_SUB_KEYS = $0008;
  KEY_READ = $20019;
  DOTNET_URL = 'https://dotnet.microsoft.com/download/dotnet/8.0';
  // Targeting Desktop Runtime, check both x64 and x86 paths for broader compatibility,
  // although your app likely targets a specific architecture or AnyCPU.
  DOTNET_REG_KEY_X64 = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  MIN_DOTNET_VERSION = '8.0.0'; // Minimum required .NET 8 version

// --- Version Comparison Helper ---
// Returns <0 if V1<V2, 0 if V1=V2, >0 if V1>V2
function CompareVersion(V1, V2: String): Integer;
var
  P, N1, N2: Integer;
  S1, S2: String;
begin
  Result := 0;
  while (Result = 0) and ((Length(V1) > 0) or (Length(V2) > 0)) do
  begin
    // --- Extract first part of V1 ---
    P := Pos('.', V1);
    if P > 0 then
    begin
      S1 := Copy(V1, 1, P - 1);
      V1 := Copy(V1, P + 1, Length(V1));
    end
    else
    begin
      S1 := V1;
      V1 := '';
    end;

    // --- Extract first part of V2 ---
    P := Pos('.', V2);
    if P > 0 then
    begin
      S2 := Copy(V2, 1, P - 1);
      V2 := Copy(V2, P + 1, Length(V2));
    end
    else
    begin
      S2 := V2;
      V2 := '';
    end;

    // --- Convert parts to integers using StrToIntDef ---
    N1 := StrToIntDef(Trim(S1), 0); // Use Trim for safety and provide 0 as default on error
    N2 := StrToIntDef(Trim(S2), 0); // Use Trim for safety and provide 0 as default on error

    // --- Compare the integer parts ---
    if N1 < N2 then Result := -1
    else if N1 > N2 then Result := 1;
  end;
end;

function RegOpenKeyEx(
  hKey: Integer;
  lpSubKey: String;
  ulOptions: Integer;
  samDesired: Integer;
  var phkResult: Integer
): Integer;
external 'RegOpenKeyExW@advapi32.dll stdcall';

function RegEnumKeyEx(
  hKey: Integer;
  dwIndex: Integer;
  lpName: String;
  var lpcName: Integer;
  lpReserved: Integer;
  lpClass: String;
  var lpcClass: Integer;
  lpftLastWriteTime: Integer
): Integer;
external 'RegEnumKeyExW@advapi32.dll stdcall';

function RegCloseKey(hKey: Integer): Integer;
external 'RegCloseKey@advapi32.dll stdcall';

function RegEnumDotnetSubkeys64(RootKey: Integer; SubKey: String; var Version: String): Boolean;
var
  hKey, Index, Res, NameLen: Integer;
  SubkeyName: String;
  DummyClass: String;
  DummyClassLen: Integer;
begin
  Result := False;
  Version := '';
  Res := RegOpenKeyEx(RootKey, SubKey, 0, KEY_READ or KEY_ENUMERATE_SUB_KEYS or KEY_WOW64_64KEY, hKey);
  if Res <> 0 then
  begin
    Log('RegOpenKeyEx failed: ' + IntToStr(Res));
    exit;
  end;

  Index := 0;
  while True do
  begin
    NameLen := 255;
    SubkeyName := StringOfChar(#0, NameLen);
    DummyClass := '';
    DummyClassLen := 0;
    Res := RegEnumKeyEx(hKey, Index, SubkeyName, NameLen, 0, DummyClass, DummyClassLen, 0);
    if Res <> 0 then break;

    SubkeyName := Copy(SubkeyName, 1, NameLen);
    Log('.NET Check: Found installed version subkey: ' + SubkeyName);

    if CompareVersion(SubkeyName, MIN_DOTNET_VERSION) >= 0 then
    begin
      if (not Result) or (CompareVersion(SubkeyName, Version) > 0) then
      begin
        Version := SubkeyName;
        Result := True;
      end;
    end;
    Index := Index + 1;
  end;

  RegCloseKey(hKey);
end;

function InitializeSetup(): Boolean;
var
  BestVersion: String;
begin
  Log('.NET Check: Starting verification.');

  if RegEnumDotnetSubkeys64(HKEY_LOCAL_MACHINE, DOTNET_REG_KEY_X64, BestVersion) then
  begin
    Log('.NET Check: Found suitable version: ' + BestVersion);
  end
  else
  begin
    Log('.NET Check: Required .NET Desktop Runtime version ' + MIN_DOTNET_VERSION + ' or newer was not found.');
    MsgBox('Warning: Logonaut requires the Microsoft .NET Desktop Runtime version ' + MIN_DOTNET_VERSION + ' or a newer version.'#13#13 +
           'It was not detected on this system. The application may fail to run properly.'#13#13 +
           'You can install the runtime from:'#13 + DOTNET_URL, mbInformation, MB_OK);
  end;

  // Continue installation regardless
  Result := True;
end;
