; Squint - copied-link safety checker for Windows.
; Copyright (C) 2026 milkmade
; SPDX-License-Identifier: GPL-3.0-or-later

; Squint installer.
; Built by tools\build-installer.ps1, which supplies PayloadDir, OutputDir, IconFile, AppVersion.
;
; Per-user install by design: no UAC prompt, nothing outside the user's own profile.
; The payload is a single self-contained Squint.exe with .NET bundled inside it.

#ifndef PayloadDir
  #error PayloadDir must be defined - run tools\build-installer.ps1
#endif
#ifndef AppVersion
  #define AppVersion "1.3.0"
#endif

#define AppName "Squint"
#define AppExe  "Squint.exe"

[Setup]
; New product identity: Squint is a rename, so it must not upgrade in place over the old
; Link Inspector install. That one is uninstalled explicitly in RemoveLegacyInstall below.
AppId={{0F9C0361-E582-4E62-BCF8-896FAB2D43CB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

; lowest, with no override dialog: always a per-user install, so setup never asks anything
; about permissions and never triggers UAC.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Squint
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}

OutputDir={#OutputDir}
OutputBaseFilename=Squint-Setup
SetupIconFile={#IconFile}
Compression=lzma2/max
SolidCompression=yes

; Record packed-file timestamps in UTC. Without this the recorded value depends on the
; build machine's timezone, so CI and a local build would never produce the same bytes.
TimeStampsInUTC=yes

; Smallest wizard that still teaches it: Welcome, How it works, API keys, Finish.
WizardStyle=modern
WizardSizePercent=120
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Squint
WelcomeLabel2=Checks every link you copy against Google Safe Browsing, VirusTotal and URLhaus, and tells you what it found.%n%nInstalls to your own user account — no administrator password needed.
FinishedHeadingLabel=Squint is running
FinishedLabelNoIcons=Look for the icon in the system tray, under the ^ arrow beside the clock. If the icon is there, the app is running. It will start automatically every time you sign in.%n%nLink checking itself starts switched OFF. Left-click the tray icon to turn it on.
FinishedLabel=Look for the icon in the system tray, under the ^ arrow beside the clock. If the icon is there, the app is running. It will start automatically every time you sign in.%n%nLink checking itself starts switched OFF. Left-click the tray icon to turn it on.

[InstallDelete]
; Wipe leftovers from the older framework-dependent layouts. This matters: a stray
; runtimeconfig.json beside a single-file exe sends it looking for a shared .NET runtime
; that won't exist on a clean machine.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\install.ps1"
Type: files; Name: "{app}\uninstall.ps1"
Type: filesandordirs; Name: "{app}\app"

[Files]
; notimestamp: don't store the source file's modification time in the installer. That time is
; what made two builds of identical input produce different bytes. Mutually exclusive with
; touch, and it already gives the installed file a sensible time.
Source: "{#PayloadDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion notimestamp

; Wizard artwork, unpacked to temp only.
Source: "verified.bmp"; Flags: dontcopy notimestamp
Source: "caution.bmp";  Flags: dontcopy notimestamp
Source: "suspect.bmp";  Flags: dontcopy notimestamp

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} Settings"; Filename: "{app}\{#AppExe}"; Parameters: "--settings"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Run at login, switched ON, so the tray icon is always there to tell you it's alive.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
    ValueName: "Squint"; ValueData: """{app}\{#AppExe}"""; Flags: uninsdeletevalue

; Windows keeps the on/off state for that entry separately. 02 = enabled, 03 = disabled.
; Writing 02 clears any leftover "disabled" from an earlier install.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"; \
    ValueType: binary; ValueName: "Squint"; \
    ValueData: "02 00 00 00 00 00 00 00 00 00 00 00"; \
    Flags: uninsdeletevalue

[Run]
; Start it before the finish page, so "it's running" is true by the time they read it.
Filename: "{app}\{#AppExe}"; Flags: nowait skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopApp"

[UninstallDelete]
; The uninstaller runs from {app}, so Windows won't let it remove its own folder while it's
; still there. Sweep it afterwards so nothing empty is left behind.
Type: dirifempty; Name: "{app}"

[Code]
var
  TutorialPage: TWizardPage;
  KeysPage: TWizardPage;
  GsbEdit, VtEdit, HausEdit: TNewEdit;
  SettingsAlreadyExist: Boolean;

function SettingsPath(): String;
begin
  Result := ExpandConstant('{userappdata}\Squint\settings.json');
end;

function JsonEscape(S: String): String;
begin
  StringChangeEx(S, '\', '\\', True);
  StringChangeEx(S, '"', '\"', True);
  Result := S;
end;

procedure OpenUrl(Url: String);
var
  ResultCode: Integer;
begin
  ShellExec('open', Url, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

procedure GsbLinkClick(Sender: TObject);
begin
  OpenUrl('https://console.cloud.google.com/apis/library/safebrowsing.googleapis.com');
end;

procedure VtLinkClick(Sender: TObject);
begin
  OpenUrl('https://www.virustotal.com/gui/join-us');
end;

procedure HausLinkClick(Sender: TObject);
begin
  OpenUrl('https://auth.abuse.ch/');
end;

{ ---------------------------------------------------------------- tutorial page ---- }

procedure AddVerdictRow(Page: TWizardPage; BmpName: String; Top: Integer;
                        Heading, Body: String; Colour: TColor);
var
  Img: TBitmapImage;
  Title, Text: TNewStaticText;
begin
  ExtractTemporaryFile(BmpName);

  Img := TBitmapImage.Create(Page);
  Img.Parent := Page.Surface;
  Img.Bitmap.LoadFromFile(ExpandConstant('{tmp}\') + BmpName);
  Img.Left := 0;
  Img.Top := Top;
  Img.Width := ScaleX(38);
  Img.Height := ScaleY(38);
  Img.Stretch := True;

  Title := TNewStaticText.Create(Page);
  Title.Parent := Page.Surface;
  Title.Left := ScaleX(50);
  Title.Top := Top;
  Title.Font.Style := [fsBold];
  Title.Font.Color := Colour;
  Title.Caption := Heading;

  Text := TNewStaticText.Create(Page);
  Text.Parent := Page.Surface;
  Text.Left := ScaleX(50);
  Text.Top := Top + ScaleY(16);
  Text.Width := Page.SurfaceWidth - ScaleX(50);
  Text.AutoSize := False;
  Text.Height := ScaleY(28);
  Text.WordWrap := True;
  Text.Caption := Body;
end;

procedure AddParagraph(Page: TWizardPage; Top, Height: Integer; Caption: String);
var
  L: TNewStaticText;
begin
  L := TNewStaticText.Create(Page);
  L.Parent := Page.Surface;
  L.Left := 0;
  L.Top := Top;
  L.Width := Page.SurfaceWidth;
  L.Height := Height;
  L.AutoSize := False;
  L.WordWrap := True;
  L.Caption := Caption;
end;

procedure BuildTutorialPage();
begin
  TutorialPage := CreateCustomPage(wpWelcome,
    'How it works',
    'Worth thirty seconds, then you never think about it again.');

  AddParagraph(TutorialPage, 0, ScaleY(28),
    'Copy any link — Ctrl+C, or right-click a link and choose "Copy link address". ' +
    'A card appears in the bottom-right corner of your screen with the result.');

  { Colours below are BGR, not RGB. }
  AddVerdictRow(TutorialPage, 'verified.bmp', ScaleY(38), 'VERIFIED',
    'A site we recognise, and nothing flagged it.', $005EC522);

  AddVerdictRow(TutorialPage, 'caution.bmp', ScaleY(86), 'CAUTION',
    'Unknown. Not an accusation — most links land here, because "nobody has reported it" ' +
    'is not the same as "it is safe".', $000B9EF5);

  AddVerdictRow(TutorialPage, 'suspect.bmp', ScaleY(140), 'SUSPECT',
    'Something flagged it, or the address is impersonating a real site. Don''t open it.',
    $004444EF);

  AddParagraph(TutorialPage, ScaleY(190), ScaleY(32),
    'Squint sits in the system tray, under the ^ arrow next to the clock. ' +
    'Left-click to pause or resume, right-click for settings.');
end;

{ ---------------------------------------------------------------- API keys page ---- }

function AddKeyField(Page: TWizardPage; Top: Integer; Caption, Hint: String;
                     OnGetKey: TNotifyEvent): TNewEdit;
var
  Label1, HintLabel: TNewStaticText;
  Btn: TNewButton;
  Edit: TNewEdit;
begin
  Label1 := TNewStaticText.Create(Page);
  Label1.Parent := Page.Surface;
  Label1.Left := 0;
  Label1.Top := Top;
  Label1.Font.Style := [fsBold];
  Label1.Caption := Caption;

  Btn := TNewButton.Create(Page);
  Btn.Parent := Page.Surface;
  Btn.Width := ScaleX(76);
  Btn.Height := ScaleY(21);
  Btn.Left := Page.SurfaceWidth - Btn.Width;
  Btn.Top := Top - ScaleY(4);
  Btn.Caption := 'Get key';
  Btn.OnClick := OnGetKey;

  Edit := TNewEdit.Create(Page);
  Edit.Parent := Page.Surface;
  Edit.Left := 0;
  Edit.Top := Top + ScaleY(19);
  Edit.Width := Page.SurfaceWidth;
  Edit.Text := '';

  HintLabel := TNewStaticText.Create(Page);
  HintLabel.Parent := Page.Surface;
  HintLabel.Left := 0;
  HintLabel.Top := Top + ScaleY(43);
  HintLabel.Width := Page.SurfaceWidth;
  HintLabel.Height := ScaleY(26);
  HintLabel.AutoSize := False;
  HintLabel.WordWrap := True;
  HintLabel.Font.Color := clGrayText;
  HintLabel.Caption := Hint;

  Result := Edit;
end;

procedure BuildKeysPage();
begin
  KeysPage := CreateCustomPage(TutorialPage.ID,
    'Your API keys',
    'All three are free. Click "Get key", then paste it in. You can skip this and add them later.');

  GsbEdit := AddKeyField(KeysPage, 0, 'Google Safe Browsing',
    'Sign in, click Enable on the page that opens, then Credentials > Create credentials > API key.',
    @GsbLinkClick);

  VtEdit := AddKeyField(KeysPage, ScaleY(76), 'VirusTotal',
    'Sign up free, then click your avatar (top right) > API key. This is the one that earns green ticks.',
    @VtLinkClick);

  HausEdit := AddKeyField(KeysPage, ScaleY(152), 'URLhaus (abuse.ch)',
    'Sign up free with your email; they send you an Auth-Key. Catches brand-new malware links.',
    @HausLinkClick);

  AddParagraph(KeysPage, ScaleY(226), ScaleY(28),
    'With no keys at all it still spots fake lookalike addresses and unsafe steam:// links, ' +
    'but every other link will just say CAUTION.');
end;

procedure InitializeWizard();
begin
  { Also counts settings inherited from the old Link Inspector install, which are migrated
    during ssInstall - otherwise we'd ask for keys the user already has. }
  SettingsAlreadyExist := FileExists(SettingsPath())
    or FileExists(ExpandConstant('{userappdata}\LinkInspector\settings.json'));
  BuildTutorialPage();
  BuildKeysPage();
end;

{ A reinstall must never clobber keys or trusted domains that are already saved. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = KeysPage.ID) and SettingsAlreadyExist;
end;

procedure WriteSettings();
var
  Lines: TArrayOfString;
  Gsb, Vt, Haus: String;
begin
  if SettingsAlreadyExist then
    Exit;

  Gsb := Trim(GsbEdit.Text);
  Vt := Trim(VtEdit.Text);
  Haus := Trim(HausEdit.Text);

  if (Gsb = '') and (Vt = '') and (Haus = '') then
    Exit;

  CreateDir(ExpandConstant('{userappdata}\Squint'));

  SetArrayLength(Lines, 7);
  Lines[0] := '{';
  Lines[1] := '  "ApiKey": "' + JsonEscape(Gsb) + '",';
  Lines[2] := '  "VirusTotalKey": "' + JsonEscape(Vt) + '",';
  Lines[3] := '  "UrlHausKey": "' + JsonEscape(Haus) + '",';
  Lines[4] := '  "FollowRedirects": true,';
  Lines[5] := '  "TrustedDomains": []';
  Lines[6] := '}';

  SaveStringsToFile(SettingsPath(), Lines, False);
end;

{ This app used to be called Link Inspector. Remove that install and carry its settings over,
  so upgrading doesn't leave a second copy in Apps or lose the user's API keys. }
procedure RemoveLegacyInstall();
var
  OldDir, OldUninstaller, OldSettings, NewSettings: String;
  ResultCode: Integer;
begin
  OldDir := ExpandConstant('{localappdata}\Programs\LinkInspector');
  OldUninstaller := OldDir + '\unins000.exe';

  { Keys first - the old uninstaller keeps them when run silently, but not if the user later
    runs it by hand. }
  OldSettings := ExpandConstant('{userappdata}\LinkInspector\settings.json');
  NewSettings := ExpandConstant('{userappdata}\Squint\settings.json');

  if FileExists(OldSettings) and not FileExists(NewSettings) then
  begin
    CreateDir(ExpandConstant('{userappdata}\Squint'));
    FileCopy(OldSettings, NewSettings, False);
  end;

  { The Inno-built Link Inspector, if this machine got that far. }
  if FileExists(OldUninstaller) then
    Exec(OldUninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { The even older PowerShell install, which registered itself by hand. }
  if RegKeyExists(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\LinkInspector') then
    RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\LinkInspector');

  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LinkInspector');
  RegDeleteValue(HKEY_CURRENT_USER,
    'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run', 'LinkInspector');

  DeleteFile(ExpandConstant('{userprograms}\Link Inspector.lnk'));
  DeleteFile(ExpandConstant('{userprograms}\Link Inspector Settings.lnk'));
  DelTree(ExpandConstant('{userprograms}\Link Inspector'), True, True, True);
  DelTree(OldDir, True, True, True);

  { Delete the old settings file by name before the DelTree. The old uninstaller finishes
    asynchronously (it relaunches itself from temp), and racing it made DelTree leave the
    file - which would strand a copy of the user's API keys on disk. }
  DeleteFile(OldSettings);
  DelTree(ExpandConstant('{userappdata}\LinkInspector'), True, True, True);
end;

{ Pinning the tray icon onto the taskbar is handled by the app itself, not here: Windows only
  creates the registry entry once the icon has been shown, which happens after setup has gone. }

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveLegacyInstall();

  if CurStep = ssPostInstall then
    WriteSettings();
end;

{ On uninstall, offer to keep the API keys — they're the one thing that's a chore to recreate. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Dir := ExpandConstant('{userappdata}\Squint');
  if not DirExists(Dir) then
    Exit;

  if SuppressibleMsgBox(
       'Also delete your saved API keys and trusted domains?' + #13#10#13#10 +
       'Choose No if you might reinstall later — the keys are free, but fiddly to fetch again.',
       mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
    DelTree(Dir, True, True, True);
end;
