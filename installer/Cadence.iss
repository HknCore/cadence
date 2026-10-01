; Cadence – Windows-Installer (Inno Setup 6.6 oder neuer, auch 7.x)
; Wird von build-installer.ps1 bzw. der GitHub-Release-Automatik aufgerufen:
;   ISCC.exe /DAppVersion=0.2.2 /DSourceDir=..\build\publish installer\Cadence.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\publish"
#endif

#define AppName "Cadence"
#define AppExe "Cadence.exe"
#define AppUrl "https://github.com/"

[Setup]
; Feste ID: nie aendern, sonst erkennt Windows Updates nicht als dieselbe App.
AppId={{6E0B6C8A-3F1D-4B7E-9C2A-5A1D7E4F8B21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; Standard: nur fuer den aktuellen Benutzer, ohne Admin-Abfrage.
; Wer will, waehlt im Dialog "fuer alle Benutzer" (dann mit Admin-Rechten).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableReadyPage=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 22H2 (Build 19045) oder neuer
MinVersion=10.0.19045

OutputDir=..\build\installer
OutputBaseFilename=Cadence-Setup-{#AppVersion}
SetupIconFile=..\src\Cadence.App\Assets\Cadence.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes

; Laufendes Cadence erkennen und zum Schliessen auffordern
AppMutex=CadenceAppMutex
CloseApplications=yes
RestartApplications=no

; Optik: dunkel wie die App (Dark Mode gibt es ab Inno Setup 6.6)
#if Ver >= 0x06060000
WizardStyle=modern dark
#else
WizardStyle=modern
#endif
WizardImageFile=assets\wizard-large-100.bmp,assets\wizard-large-200.bmp
WizardSmallImageFile=assets\wizard-small-100.bmp,assets\wizard-small-200.bmp
WizardSizePercent=110

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.DesktopIcon=Verknüpfung auf dem &Desktop erstellen
english.DesktopIcon=Create a &desktop shortcut
german.Autostart=Mit &Windows starten (minimiert im Infobereich)
english.Autostart=Start with &Windows (minimized to tray)
german.Extras=Zusätzlich:
english.Extras=Additional options:
german.LaunchApp=Cadence jetzt starten
english.LaunchApp=Launch Cadence now
german.RemoveSettings=Sollen auch deine Profile und Einstellungen gelöscht werden?%n%nWähle „Nein“, wenn du Cadence später wieder installieren möchtest.
english.RemoveSettings=Also delete your profiles and settings?%n%nChoose "No" if you plan to reinstall Cadence later.
german.GamesRunning=Bitte beende vor dem Fortfahren alle Spiele, die mit Cadence laufen. Ihre Cadence-Datei ist sonst noch in Benutzung.
english.GamesRunning=Please close all games running with Cadence before continuing. Otherwise its file is still in use.
german.WelcomeLabel2=Cadence {#AppVersion} wird jetzt auf deinem Computer installiert.%n%nCadence hält jedes Bild deiner Spiele exakt im Takt – für ein ruhiges, gleichmässiges Spielgefühl.%n%nHinweis: Nicht in Online-Spielen mit Anti-Cheat verwenden.
english.WelcomeLabel2=This will install Cadence {#AppVersion} on your computer.%n%nCadence keeps every frame of your games perfectly paced for a calm, even feel.%n%nNote: Do not use in online games with anti-cheat.

[Messages]

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Extras}"; Flags: unchecked
Name: "autostart"; Description: "{cm:Autostart}"; GroupDescription: "{cm:Extras}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Gleicher Eintrag, den auch der Schalter in den Cadence-Einstellungen setzt.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Cadence"; \
  ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ Ist die Hook-DLL noch in einem Spiel geladen, laesst sie sich nicht ueberschreiben.
  (Umbenennen wuerde trotzdem klappen, darum mit exklusivem Schreibzugriff pruefen.) }
function HookInUse(const Dir: String): Boolean;
var
  F: String;
  Stream: TFileStream;
begin
  Result := False;
  F := AddBackslash(Dir) + 'CadenceHook.dll';
  if not FileExists(F) then Exit;
  try
    Stream := TFileStream.Create(F, fmOpenReadWrite or fmShareExclusive);
    Stream.Free;
  except
    Result := True;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  while HookInUse(ExpandConstant('{app}')) do
  begin
    if MsgBox(CustomMessage('GamesRunning'), mbInformation, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := CustomMessage('GamesRunning');
      Exit;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  while HookInUse(ExpandConstant('{app}')) do
  begin
    if MsgBox(CustomMessage('GamesRunning'), mbInformation, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Autostart immer entfernen, egal ob vom Installer oder in der App gesetzt }
    RegDeleteValue(HKCU, RunKey, 'Cadence');
    if (not UninstallSilent) and
       (MsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(ExpandConstant('{localappdata}\Cadence'), True, True, True);
  end;
end;
