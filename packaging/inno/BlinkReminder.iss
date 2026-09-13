#ifndef AppVersion
  #error Build with build/Package-Exe.ps1
#endif

[Setup]
AppId={{{#AppGuid}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={code:GetInstallDirectory}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UsePreviousPrivileges=yes
UsePreviousAppDir=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir={#OutputDirectory}
OutputBaseFilename=BlinkReminder-{#AppVersion}-win-x64-setup
SetupIconFile={#RepositoryRoot}\assets\generated\app.ico
UninstallDisplayIcon={app}\BlinkReminder.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}.0
#ifdef SignedBuild
SignTool=BlinkSign
SignedUninstaller=yes
#endif

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
brazilianportuguese.PrivilegesRequiredOverrideCurrentUserRecommended=Somente para &mim — recomendado
brazilianportuguese.PrivilegesRequiredOverrideAllUsers=Para &todos os usuários deste computador — requer administrador
english.PrivilegesRequiredOverrideCurrentUserRecommended=Install for &me only (recommended)
english.PrivilegesRequiredOverrideAllUsers=Install for &all users (requires administrator)
french.PrivilegesRequiredOverrideCurrentUserRecommended=Installer pour &moi seulement (recommandé)
french.PrivilegesRequiredOverrideAllUsers=Installer pour &tous les utilisateurs (administrateur requis)

[CustomMessages]
english.ScopeConflict=An installation in another scope already exists. Update it in its current scope, or uninstall it explicitly before changing scope. Personal settings are retained. Contact your administrator when required.
brazilianportuguese.ScopeConflict=Já existe uma instalação em outro escopo. Atualize no escopo atual ou desinstale explicitamente antes de mudar o escopo. As preferências pessoais são preservadas. Procure a equipe de TI quando necessário.
french.ScopeConflict=Une installation existe dans une autre portée. Mettez-la à jour dans sa portée actuelle, ou désinstallez-la explicitement avant de changer de portée. Les préférences sont conservées. Consultez votre administrateur au besoin.
english.InvalidDirectory=The installation directory must remain in the standard directory for the selected scope. Changing scope requires an explicit uninstall first.
brazilianportuguese.InvalidDirectory=O diretório deve permanecer no local padrão do escopo selecionado. Para mudar o escopo, desinstale explicitamente primeiro.
french.InvalidDirectory=Le dossier doit rester dans le répertoire standard de la portée choisie. Désinstallez explicitement avant de changer de portée.

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\BlinkReminder.exe"; WorkingDir: "{app}"

[UninstallRun]
; Never launch a user's application under an administrator's identity.
Filename: "{app}\BlinkReminder.exe"; Parameters: "--uninstall-integration"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: CanCleanUserIntegration

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1';

function GetInstallDirectory(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := ExpandConstant('{autopf}\{#InstallName}')
  else
    Result := ExpandConstant('{localappdata}\Programs\{#InstallName}');
end;

function HasUserInstallation: Boolean;
var
  Users: TArrayOfString;
  I: Integer;
begin
  Result := RegKeyExists(HKCU64, UninstallKey);
  if Result then Exit;
  { Loaded hives include the initiating user when another account supplies UAC.
    Do not load or walk any user's profile or inspect application settings. }
  if RegGetSubkeyNames(HKU, '', Users) then
    for I := 0 to GetArrayLength(Users) - 1 do
      if (Pos('S-1-', Users[I]) = 1) and
         RegKeyExists(HKU, Users[I] + '\' + UninstallKey) then
      begin
        Result := True;
        Exit;
      end;
end;

function ScopeConflict: Boolean;
begin
  if IsAdminInstallMode then
    Result := HasUserInstallation
  else
    Result := RegKeyExists(HKLM64, UninstallKey);
end;

function InitializeSetup: Boolean;
begin
  Result := not ScopeConflict;
  if not Result then
    SuppressibleMsgBox(CustomMessage('ScopeConflict'), mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if ScopeConflict then
    Result := CustomMessage('ScopeConflict')
  else if CompareText(RemoveBackslash(ExpandConstant('{app}')),
                      RemoveBackslash(GetInstallDirectory(''))) <> 0 then
    Result := CustomMessage('InvalidDirectory');
end;

function CanCleanUserIntegration: Boolean;
begin
  Result := (not IsAdminInstallMode) and (not IsAdmin);
end;
