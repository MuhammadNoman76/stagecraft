; Stagecraft installer (Inno Setup 6)
; Built by build.ps1, which publishes each edition to ..\publish\<edition> first.
;   /DEdition=x64     Windows 10/11, 64-bit Intel/AMD (also runs on ARM64 via emulation)
;   /DEdition=arm64   Windows 10/11 on ARM (Snapdragon, Surface Pro X...)
;   /DEdition=x86     Windows 10/11, 32-bit
;   /DEdition=legacy  Windows 7 SP1 / 8.1 (32- or 64-bit), needs .NET Framework 4.8
; This file is UTF-8 with a BOM.

#define AppName "Stagecraft"
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef Edition
  #define Edition "x64"
#endif
#define AppExe "Stagecraft.exe"
#define ProgId "Stagecraft.Deck"
#define PptxProgId "Stagecraft.PowerPoint"
#define PublishDir "..\publish\" + Edition

[Setup]
; Same AppId for every edition: installing another edition simply replaces the current one.
AppId={{B522CE93-BE8D-4A68-A206-8B1ED3ED7489}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL=https://www.nomanshafiq.com/stagecraft/
AppComments=Presentations that look designed. Without the effort.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
#if Edition == "legacy"
OutputBaseFilename=Stagecraft-Setup-{#AppVersion}-win7-8
#else
OutputBaseFilename=Stagecraft-Setup-{#AppVersion}-{#Edition}
#endif
SetupIconFile=..\src\Stagecraft\Assets\Stagecraft.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=4
#if Edition == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
#elif Edition == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
MinVersion=10.0
#elif Edition == "x86"
MinVersion=10.0
#else
; 32- or 64-bit Intel/AMD Windows 7 SP1 / 8.1 (ARM PCs run Windows 10/11 and use the arm64 edition).
ArchitecturesAllowed=x86os or x64os
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
#endif
; "Install for all users" (Program Files) by default; "only for me" needs no admin rights.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fileassoc"; Description: "Open Stagecraft presentations (.stagecraft) with {#AppName}"; GroupDescription: "Windows integration:"
Name: "pptxopenwith"; Description: "Add {#AppName} to ""Open with"" for PowerPoint files (.pptx, imported as a copy)"; GroupDescription: "Windows integration:"

[InstallDelete]
; Updating: remove the previous version's program files first, so nothing obsolete is left behind.
; Only in Stagecraft's own folder. Settings (%APPDATA%\Stagecraft) and the uninstaller (unins000.*) are never touched.
Type: filesandordirs; Name: "{app}\Fonts"; Check: IsOwnFolder
Type: files; Name: "{app}\*.dll"; Check: IsOwnFolder
Type: files; Name: "{app}\*.json"; Check: IsOwnFolder
Type: files; Name: "{app}\*.config"; Check: IsOwnFolder
Type: files; Name: "{app}\*.pdb"; Check: IsOwnFolder
Type: files; Name: "{app}\*.ico"; Check: IsOwnFolder
Type: files; Name: "{app}\THIRD-PARTY-NOTICES.txt"; Check: IsOwnFolder
Type: files; Name: "{app}\createdump.exe"; Check: IsOwnFolder
Type: files; Name: "{app}\{#AppExe}"; Check: IsOwnFolder

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; ---- .stagecraft: Stagecraft's own format
Root: HKA; Subkey: "Software\Classes\.stagecraft"; ValueType: string; ValueName: ""; ValueData: "{#ProgId}"; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.stagecraft"; ValueType: string; ValueName: "Content Type"; ValueData: "application/vnd.stagecraft.deck+zip"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.stagecraft"; ValueType: string; ValueName: "PerceivedType"; ValueData: "document"; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\.stagecraft\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Stagecraft presentation"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Stagecraft presentation"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\StagecraftDeck.ico"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell"; ValueType: string; ValueName: ""; ValueData: "open"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\present"; ValueType: string; ValueName: ""; ValueData: "Present"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\{#ProgId}\shell\present\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --present ""%1"""; Tasks: fileassoc

; ---- .pptx: only an extra "Open with" entry (never the default app); Stagecraft imports a copy
Root: HKA; Subkey: "Software\Classes\.pptx\OpenWithProgids"; ValueType: string; ValueName: "{#PptxProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: pptxopenwith
Root: HKA; Subkey: "Software\Classes\{#PptxProgId}"; ValueType: string; ValueName: ""; ValueData: "PowerPoint presentation (Stagecraft import)"; Flags: uninsdeletekey; Tasks: pptxopenwith
Root: HKA; Subkey: "Software\Classes\{#PptxProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\StagecraftDeck.ico"""; Tasks: pptxopenwith
Root: HKA; Subkey: "Software\Classes\{#PptxProgId}\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Tasks: pptxopenwith
Root: HKA; Subkey: "Software\Classes\{#PptxProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: pptxopenwith

; ---- Application registration (shows up nicely in "Open with" / "Choose another app")
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"",0"
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".stagecraft"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#AppExe}\SupportedTypes"; ValueType: string; ValueName: ".pptx"; ValueData: ""

; ---- Default Apps (Settings > Apps > Default apps > Stagecraft)
Root: HKA; Subkey: "Software\{#AppName}"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#AppName}"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Makes and presents beautiful slide decks."; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#AppName}\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".stagecraft"; ValueData: "{#ProgId}"; Tasks: fileassoc
Root: HKA; Subkey: "Software\{#AppName}\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pptx"; ValueData: "{#PptxProgId}"; Tasks: fileassoc and pptxopenwith
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#AppName}"; ValueData: "Software\{#AppName}\Capabilities"; Flags: uninsdeletevalue; Tasks: fileassoc

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Stagecraft's updater runs setup with /VERYSILENT ... /RELAUNCH=1: start Stagecraft again afterwards
; (as the signed-in user, not elevated, even when setup asked for administrator rights).
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait runasoriginaluser; Check: RelaunchRequested

[Code]
// /RELAUNCH=1 on the command line (passed by Stagecraft's updater).
function RelaunchRequested(): Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

// The install folder is Stagecraft's own (named Stagecraft and holding Stagecraft.exe), so old program files can go.
function IsOwnFolder(): Boolean;
begin
  Result := (CompareText(ExtractFileName(RemoveBackslashUnlessRoot(ExpandConstant('{app}'))), '{#AppName}') = 0) and
            FileExists(ExpandConstant('{app}\{#AppExe}'));
end;

#if Edition == "legacy"
function HasDotNet48(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not HasDotNet48() then
  begin
    if SuppressibleMsgBox('Stagecraft for Windows 7 / 8.1 needs Microsoft .NET Framework 4.8, which is not installed on this PC.' + #13#10#13#10 +
              'Open Microsoft''s download page now? Install it, restart if asked, then run this setup again.',
              mbConfirmation, MB_YESNO, IDNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;
#endif
