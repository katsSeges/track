#ifndef AppVersion
  #error AppVersion must be supplied by scripts\package.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by scripts\package.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by scripts\package.ps1
#endif
; Override only for isolated installer verification.
#ifndef TrackAppId
  #define TrackAppId "{{70D34EE0-8412-48C0-961F-BD0EC5C678A7}"
#endif

[Setup]
AppId={#TrackAppId}
AppName=Track
AppVersion={#AppVersion}
AppPublisher=Track
AppPublisherURL=https://github.com/katsSeges/track
AppSupportURL=https://github.com/katsSeges/track/issues
DefaultDirName={localappdata}\Programs\Track
DefaultGroupName=Track
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=Track-{#AppVersion}-win-x64-setup
SetupIconFile=..\..\src\Track.App\Assets\track.ico
UninstallDisplayIcon={app}\Track.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
; Track holds this mutex for its lifetime. Never overwrite or remove a running copy.
AppMutex=Local\Track.TrayApp.SingleInstance
CloseApplications=yes
CloseApplicationsFilter=Track.exe
RestartApplications=no
UsePreviousTasks=no

[Tasks]
Name: "startup"; Description: "Start Track when Windows starts"
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Set-StartupPreference.ps1"; Flags: dontcopy

[Icons]
Name: "{group}\Track"; Filename: "{app}\Track.exe"
Name: "{autodesktop}\Track"; Filename: "{app}\Track.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Track"; ValueData: """{app}\Track.exe"""; Check: StartupWanted

[Run]
Filename: "{app}\Track.exe"; Description: "Launch Track"; Flags: nowait postinstall skipifsilent

[Code]
function StartupCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\Track.exe') + '"';
end;

procedure RemoveOwnedStartupEntry;
var
  Command: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Track', Command) and
     (CompareText(Command, StartupCommand) = 0) then
    if not RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Track') then
      RaiseException('Could not remove the Track startup entry.');
end;

function SettingsFile: String;
var
  DataDir: String;
begin
  DataDir := GetEnv('TRACK_DATA');
  if DataDir = '' then
    DataDir := ExpandConstant('{localappdata}\Track');
  Result := AddBackslash(DataDir) + 'settings.json';
end;

var
  DefaultStartup, StartupDefaultApplied: Boolean;

function TasksGiven: Boolean;
begin
  Result := (ExpandConstant('{param:TASKS|#}') <> '#') or (ExpandConstant('{param:MERGETASKS|#}') <> '#');
end;

procedure InitializeWizard;
var
  Command: String;
begin
  { Startup is on for a new install, like the app's own default. On an upgrade the live entry decides, not a
    stale selection from a previous installation: Track already used here without an entry means the user
    turned startup off. Decided once, before Setup changes anything. }
  DefaultStartup := RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Track', Command)
    or not FileExists(SettingsFile);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { Inno builds the task list when this page is reached, so the default is applied here, not in InitializeWizard. }
  { Once: going Back and Next again keeps what the user picked. }
  if (CurPageID = wpSelectTasks) and not TasksGiven and not StartupDefaultApplied then
  begin
    StartupDefaultApplied := True;
    if DefaultStartup then
      WizardSelectTasks('startup')
    else
      WizardSelectTasks('!startup');
  end;
end;

{ The checkbox when the user saw it or /TASKS chose; otherwise (a silent upgrade) the default above. }
function StartupWanted: Boolean;
begin
  if TasksGiven or not WizardSilent then
    Result := WizardIsTaskSelected('startup')
  else
    Result := DefaultStartup;
end;

function SaveStartupPreference(ValidateOnly: Boolean): String;
var
  Params, ErrorFile: String;
  ErrorText: AnsiString;
  ExitCode: Integer;
begin
  Result := '';
  ErrorFile := ExpandConstant('{tmp}\track-settings-error.txt');
  Params := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{tmp}\Set-StartupPreference.ps1') + '" -SettingsPath "' +
    SettingsFile + '" -Enabled ';
  if StartupWanted then
    Params := Params + 'true'
  else
    Params := Params + 'false';
  Params := Params + ' -ErrorFile "' + ErrorFile + '"';
  if ValidateOnly then
    Params := Params + ' -ValidateOnly';

  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Could not start Windows PowerShell to save the startup preference: ' + SysErrorMessage(ExitCode)
  else if ExitCode <> 0 then
  begin
    Result := 'Could not save the Track startup preference.';
    if LoadStringFromFile(ErrorFile, ErrorText) then
      Result := Result + #13#10 + String(ErrorText);
  end;
  if Result <> '' then
    Log(Result);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('Set-StartupPreference.ps1');
  Result := SaveStartupPreference(True);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Error: String;
begin
  if CurStep = ssPostInstall then
  begin
    Error := SaveStartupPreference(False);
    if Error <> '' then
      RaiseException(Error);
    if not StartupWanted then
      RemoveOwnedStartupEntry;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveOwnedStartupEntry;
end;
