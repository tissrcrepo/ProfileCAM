!include "x64.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "WordFunc.nsh"
!include "WinMessages.nsh"
!include "MUI2.nsh"
!include "nsDialogs.nsh"

; -----------------------------------------------------------------------------
; General Settings
; -----------------------------------------------------------------------------
!define APPNAME "FChassis"
!define VERSION "1.0.5"
!define COMPANY "Teckinsoft Neuronics Pvt. Ltd."
!define INSTALLDIR "C:\FChassis"
!define FluxSDKBin "C:\FluxSDK\Bin"
!define FluxSDKDir "C:\FluxSDK"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
!define INSTALL_FLAG_KEY "Software\${COMPANY}\${APPNAME}"

; Put this RTF next to this .nsi (or adjust the subfolder)
!define LICENSE_FILE "${__FILEDIR__}\FChassis-License-Agreement.txt"

!if ! /FileExists "${LICENSE_FILE}"
  !error "LICENSE_FILE not found at: ${LICENSE_FILE}"
!endif

Name "${APPNAME} ${VERSION}"
OutFile "FChassis-Installer-${VERSION}.exe"
InstallDir "${INSTALLDIR}"
RequestExecutionLevel admin

; -----------------------------------------------------------------------------
; Variables
; -----------------------------------------------------------------------------
Var InstalledState
Var ExistingInstallDir
Var ExistingVersion
Var ExtractionCompleted
Var LogFileHandle
Var LocalAppDataDir
Var AppDataDir

; -----------------------------------------------------------------------------
; Logging Macros
; -----------------------------------------------------------------------------
!macro LogMessage message
  Push $0
  ${If} $LogFileHandle != 0
    FileWrite $LogFileHandle "[$(^Name)] ${message}$\r$\n"
  ${EndIf}
  DetailPrint "LOG: ${message}"
  Pop $0
!macroend

!macro LogVar varname varvalue
  Push $0
  ${If} $LogFileHandle != 0
    FileWrite $LogFileHandle "[$(^Name)] ${varname}: ${varvalue}$\r$\n"
  ${EndIf}
  DetailPrint "LOG: ${varname}: ${varvalue}"
  Pop $0
!macroend

!macro LogError message
  Push $0
  ${If} $LogFileHandle != 0
    FileWrite $LogFileHandle "[$(^Name)] ERROR: ${message}$\r$\n"
  ${EndIf}
  DetailPrint "ERROR: ${message}"
  Pop $0
!macroend

; -----------------------------------------------------------------------------
; Pages
; -----------------------------------------------------------------------------
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING

!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"
!define MUI_PAGE_CUSTOMFUNCTION_PRE DirectoryPre
!insertmacro MUI_PAGE_DIRECTORY
!define MUI_PAGE_CUSTOMFUNCTION_SHOW InstFilesShow
!define MUI_PAGE_CUSTOMFUNCTION_ABORT OnInstFilesAbort
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

; -----------------------------------------------------------------------------
; Helper: broadcast env change
; -----------------------------------------------------------------------------
!macro BroadcastEnvChange
  !insertmacro LogMessage "Broadcasting environment change..."
  System::Call 'USER32::SendMessageTimeout(i ${HWND_BROADCAST}, i ${WM_SETTINGCHANGE}, i 0, w "Environment", i 0, i 5000, i 0)'
  !insertmacro LogMessage "Environment change broadcast completed"
!macroend

; -----------------------------------------------------------------------------
; Helper: Purge (Blast) $INSTDIR completely
; Tries direct removal; if blocked (e.g., Uninstall.exe running), schedules delayed rmdir.
; -----------------------------------------------------------------------------
!macro PurgeInstDir
  !insertmacro LogMessage "Blasting install directory: $INSTDIR"
  SetOutPath "$TEMP"
  ; remove RO/hidden attributes so RMDir can work
  ExecWait '"cmd.exe" /C attrib -r -s -h "$INSTDIR" /S /D'
  ClearErrors
  RMDir /r "$INSTDIR"
  ${If} ${Errors}
    !insertmacro LogMessage "Direct removal failed (likely in-use). Scheduling delayed purge..."
    Exec '"cmd.exe" /C ping 127.0.0.1 -n 3 > nul & rmdir /S /Q "$INSTDIR"'
  ${Else}
    !insertmacro LogMessage "Install directory removed immediately."
  ${EndIf}
!macroend

; -----------------------------------------------------------------------------
; Delete installer EXE (self-delete for the *installer*, not uninstaller)
; -----------------------------------------------------------------------------
Function DeleteInstaller
  !insertmacro LogMessage "Deleting installer executable..."
  Sleep 1000
  Exec '"cmd.exe" /C ping 127.0.0.1 -n 2 > nul & del /f /q "$EXEPATH"'
  !insertmacro LogMessage "Scheduled installer deletion: $EXEPATH"
FunctionEnd

; -----------------------------------------------------------------------------
; GUI end hook
; -----------------------------------------------------------------------------
Function .onGUIEnd
  ${If} ${Errors}
    !insertmacro LogMessage "Installation aborted with errors, deleting installer..."
    Call DeleteInstaller
  ${EndIf}
FunctionEnd

; -----------------------------------------------------------------------------
; Install failed hook
; -----------------------------------------------------------------------------
Function .onInstFailed
  !insertmacro LogMessage "Installation failed, deleting installer..."
  Call DeleteInstaller
FunctionEnd

; -----------------------------------------------------------------------------
; Install success hook
; -----------------------------------------------------------------------------
Function .onInstSuccess
  !insertmacro BroadcastEnvChange
  Call CreateShortcuts
  !insertmacro LogMessage "Installation completed successfully!"
FunctionEnd

; -----------------------------------------------------------------------------
; Version comparison function
; -----------------------------------------------------------------------------
Function CompareVersions
  Exch $0
  Exch
  Exch $1
  Push $2
  Push $3
  Push $4
  Push $5
  Push $6
  Push $7
  Push $8

  !insertmacro LogVar "Comparing version" $0
  !insertmacro LogVar "With version" $1

  StrCpy $8 $0
  ${WordFind} $8 "." "E+1" $2
  ${WordFind} $8 "." "E+2" $3
  ${WordFind} $8 "." "E+3" $4

  StrCpy $8 $1
  ${WordFind} $8 "." "E+1" $5
  ${WordFind} $8 "." "E+2" $6
  ${WordFind} $8 "." "E+3" $7

  ${If} $4 == ""
    StrCpy $4 "0"
  ${EndIf}
  ${If} $7 == ""
    StrCpy $7 "0"
  ${EndIf}

  IntCmp $2 $5 major_equal major1_greater major2_greater
major1_greater:
  StrCpy $0 "1"
  goto done
major2_greater:
  StrCpy $0 "-1"
  goto done
major_equal:
  IntCmp $3 $6 minor_equal minor1_greater minor2_greater
minor1_greater:
  StrCpy $0 "1"
  goto done
minor2_greater:
  StrCpy $0 "-1"
  goto done
minor_equal:
  IntCmp $4 $7 0 patch_diff
patch_diff:
  IntOp $0 $4 - $7
  goto done

done:
  Pop $8
  Pop $7
  Pop $6
  Pop $5
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Exch $0
FunctionEnd

; -----------------------------------------------------------------------------
; CreateShortcuts (icon-aware)
; -----------------------------------------------------------------------------
Function CreateShortcuts
  !insertmacro LogMessage "=== Creating shortcuts ==="

  StrCpy $R1 "$INSTDIR\Bin\Resources\FChassis.ico"
  StrCpy $R2 "$INSTDIR\Bin\FChassis.exe"
  !insertmacro LogVar "Icon path" $R1
  !insertmacro LogVar "Executable path" $R2

  IfFileExists "$R2" exe_found
    !insertmacro LogError "Target executable not found: $R2"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Error: FChassis.exe not found at $R2. Shortcuts cannot be created."
    Goto _Done
exe_found:

  ${If} ${FileExists} "$R1"
    !insertmacro LogMessage "Icon file found: $R1"
  ${Else}
    !insertmacro LogError "Icon file not found: $R1"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Warning: Icon file not found at $R1. Using default icon."
  ${EndIf}

  SetShellVarContext all
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  CreateDirectory "$9"
  Delete "$9\${APPNAME}.lnk"
  ${If} ${FileExists} "$R1"
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}

  SetShellVarContext current
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  CreateDirectory "$9"
  Delete "$9\${APPNAME}.lnk"
  ${If} ${FileExists} "$R1"
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}

  SetShellVarContext current
  Delete "$DESKTOP\${APPNAME}.lnk"
  ${If} ${FileExists} "$R1"
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}

  !insertmacro LogMessage "=== Shortcut creation completed ==="
_Done:
FunctionEnd

; -----------------------------------------------------------------------------
; Installation detection
; -----------------------------------------------------------------------------
Function .onInit
  SetRegView 64

  StrCpy $InstalledState 0
  StrCpy $ExistingInstallDir "${INSTALLDIR}"
  StrCpy $ExistingVersion ""
  StrCpy $ExtractionCompleted 0
  StrCpy $LogFileHandle 0

  ReadRegStr $LocalAppDataDir HKCU "Volatile Environment" "LOCALAPPDATA"
  ${If} $LocalAppDataDir == ""
    StrCpy $LocalAppDataDir "$TEMP"
  ${EndIf}

  ReadRegStr $AppDataDir HKCU "Volatile Environment" "APPDATA"
  ${If} $AppDataDir == ""
    StrCpy $AppDataDir "$LocalAppDataDir"
  ${EndIf}
  !insertmacro LogVar "AppDataDir" $AppDataDir

  CreateDirectory "$LocalAppDataDir"

  FileOpen $LogFileHandle "$LocalAppDataDir\FChassis_Install.log" w
  ${If} $LogFileHandle == ""
    StrCpy $LocalAppDataDir "$TEMP"
    FileOpen $LogFileHandle "$LocalAppDataDir\FChassis_Install.log" w
    ${If} $LogFileHandle == ""
      StrCpy $LogFileHandle 0
      DetailPrint "ERROR: Failed to open log file in both locations"
    ${Else}
      FileWrite $LogFileHandle "=== FChassis Installation Log ===$\r$\n"
      FileWrite $LogFileHandle "Started: [$(^Time)] (Fallback to TEMP)$\r$\n"
      DetailPrint "Log file opened in TEMP directory"
    ${EndIf}
  ${Else}
    FileWrite $LogFileHandle "=== FChassis Installation Log ===$\r$\n"
    FileWrite $LogFileHandle "Started: [$(^Time)]$\r$\n"
    DetailPrint "Log file opened: $LocalAppDataDir\FChassis_Install.log"
  ${EndIf}

  !insertmacro LogMessage "=== Starting .onInit function ==="
  !insertmacro LogVar "LocalAppDataDir" $LocalAppDataDir

  !insertmacro LogMessage "Checking registry for existing installation..."

  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "UninstallString"
  IfErrors check_install_flag
  !insertmacro LogMessage "Found uninstall registry key"
  !insertmacro LogVar "UninstallString" $0

  ReadRegStr $ExistingInstallDir HKLM "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $ExistingInstallDir "" 0 get_version
  StrCpy $ExistingInstallDir "${INSTALLDIR}"
  !insertmacro LogVar "ExistingInstallDir" $ExistingInstallDir

get_version:
  ReadRegStr $ExistingVersion HKLM "${UNINSTALL_KEY}" "DisplayVersion"
  IfErrors check_install_flag
  !insertmacro LogVar "ExistingVersion" $ExistingVersion

  Push $ExistingVersion
  Push "${VERSION}"
  Call CompareVersions
  Pop $1
  !insertmacro LogVar "Version comparison result" $1

  ${If} $1 == "0"
    StrCpy $InstalledState 1
    !insertmacro LogMessage "Same version already installed"
  ${ElseIf} $1 == "1"
    StrCpy $InstalledState 3
    !insertmacro LogMessage "Newer version already installed"
  ${Else}
    StrCpy $InstalledState 2
    !insertmacro LogMessage "Older version installed"
  ${EndIf}
  Goto done

check_install_flag:
  !insertmacro LogMessage "Checking custom install flag registry..."
  ReadRegStr $0 HKLM "${INSTALL_FLAG_KEY}" "Installed"
  StrCmp $0 "1" 0 done
  !insertmacro LogMessage "Found custom install flag"
  ReadRegStr $ExistingVersion HKLM "${INSTALL_FLAG_KEY}" "Version"
  ReadRegStr $ExistingInstallDir HKLM "${INSTALL_FLAG_KEY}" "InstallPath"
  !insertmacro LogVar "ExistingVersion from custom key" $ExistingVersion
  !insertmacro LogVar "ExistingInstallDir from custom key" $ExistingInstallDir

  StrCmp $ExistingVersion "" done 0
  Push $ExistingVersion
  Push "${VERSION}"
  Call CompareVersions
  Pop $1
  !insertmacro LogVar "Version comparison result (custom key)" $1

  ${If} $1 == "0"
    StrCpy $InstalledState 1
    !insertmacro LogMessage "Same version installed (custom key)"
  ${ElseIf} $1 == "1"
    StrCpy $InstalledState 3
    !insertmacro LogMessage "Newer version installed (custom key)"
  ${Else}
    StrCpy $InstalledState 2
    !insertmacro LogMessage "Older version installed (custom key)"
  ${EndIf}

done:
  StrCpy $INSTDIR $ExistingInstallDir
  !insertmacro LogVar "Final INSTDIR set to" $INSTDIR
  !insertmacro LogVar "InstalledState" $InstalledState
  !insertmacro LogMessage "=== .onInit function completed ==="
FunctionEnd

Function DirectoryPre
  !insertmacro LogMessage "=== Starting DirectoryPre function ==="
  ${If} $InstalledState == 3
    !insertmacro LogMessage "Newer version detected, showing warning message"
    MessageBox MB_OK|MB_ICONEXCLAMATION "A newer version ($ExistingVersion) of ${APPNAME} is already installed.$\nCannot downgrade to version ${VERSION}.$\n$\nPlease uninstall the newer version first."
    !insertmacro LogMessage "Deleting installer due to version conflict..."
    Call DeleteInstaller
    Abort
  ${EndIf}
  !insertmacro LogMessage "=== DirectoryPre function completed ==="
FunctionEnd

Function InstFilesShow
  !insertmacro LogMessage "=== InstFiles page shown ==="
  GetDlgItem $0 $HWNDPARENT 2
  EnableWindow $0 1
FunctionEnd

; -----------------------------------------------------------------------------
; Extract thirdParty with progress
; -----------------------------------------------------------------------------
Function ExtractThirdParty
  !insertmacro LogMessage "=== Starting ExtractThirdParty function ==="
  IfFileExists "$INSTDIR\Bin\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin\TKernel.dll" extraction_complete
  !insertmacro LogMessage "ThirdParty extraction needed"
  DetailPrint "Extracting thirdParty.zip..."
  DetailPrint "This may take several minutes (90,000+ files)..."
  SetDetailsPrint listonly
  DetailPrint "Extracting: Please wait patiently..."
  SetDetailsPrint both
  nsExec::ExecToStack '"$INSTDIR\7z.exe" x "$INSTDIR\thirdParty.zip" -o"$INSTDIR" -y'
  Pop $0
  Pop $1
  ${If} $0 != 0
    !insertmacro LogError "7-Zip extraction failed with code: $0"
    !insertmacro LogError "7-Zip output: $1"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to extract third-party components. Installation may be incomplete."
  ${Else}
    StrCpy $ExtractionCompleted 1
    !insertmacro LogMessage "Third-party components extraction completed successfully"
  ${EndIf}
extraction_complete:
  !insertmacro LogMessage "ThirdParty extraction already complete or completed"
  !insertmacro LogMessage "=== ExtractThirdParty function completed ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Installation ABORT handler  -> full rollback + blast $INSTDIR
; -----------------------------------------------------------------------------
Function OnInstFilesAbort
  !insertmacro LogMessage "=== Installation abort requested ==="
  MessageBox MB_YESNO|MB_ICONQUESTION "Are you sure you want to cancel the installation?" IDYES +2
  Return

  !insertmacro LogMessage "User confirmed cancellation, performing cleanup (rollback)"
  MessageBox MB_OK|MB_ICONINFORMATION "Installation canceled. Rolling back..."

  ; Shortcuts
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"

  ; PATH removals (system)
  EnVar::SetHKLM
  EnVar::DeleteValue "Path" "$INSTDIR\Bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin"
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin"

  ; Registry cleanup
  SetRegView 64
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"

  ; AppData (per-user)
  Delete "$AppDataDir\FChassis\FChassis.User.RecentFiles.JSON"
  Delete "$AppDataDir\FChassis\FChassis.User.Settings.JSON"
  RMDir "$AppDataDir\FChassis"

  ; Blast install directory entirely
  !insertmacro PurgeInstDir

  ; Env change broadcast
  !insertmacro BroadcastEnvChange

  ; Schedule installer EXE deletion (the .exe you're running now)
  Call DeleteInstaller

  ; Close log
  FileClose $LogFileHandle

  Abort
FunctionEnd

; -----------------------------------------------------------------------------
; Main Installation
; -----------------------------------------------------------------------------
Section "Main Installation" SecMain
  !insertmacro LogMessage "=== Starting Main Installation Section ==="
  SetRegView 64

  SetOutPath "$INSTDIR"

  CreateDirectory "$INSTDIR\Bin"
  !insertmacro LogMessage "Created directory: $INSTDIR\Bin"

  ${If} $InstalledState == 1
    !insertmacro LogMessage "Same version already installed, proceeding with reinstallation"
  ${ElseIf} $InstalledState == 2
    !insertmacro LogMessage "Older version detected, proceeding with upgrade"
  ${EndIf}

  !insertmacro LogMessage "Copying main application files..."
  File /r "${FluxSDKDir}\*.*"

  !insertmacro LogMessage "Copying license file to installation directory..."
  IfFileExists "${FluxSDKDir}\FChassis-License-Agreement.rtf" license_file_exists license_file_missing
license_file_exists:
  CopyFiles /SILENT "${FluxSDKDir}\FChassis-License-Agreement.rtf" "$INSTDIR"
  !insertmacro LogMessage "License file copied to: $INSTDIR\FChassis-License-Agreement.rtf"
  Goto license_file_done
license_file_missing:
  !insertmacro LogError "License file not found at source: ${FluxSDKDir}\FChassis-License-Agreement.rtf"
license_file_done:

  !insertmacro LogMessage "Copying third-party components..."
  File "${FluxSDKDir}\thirdParty.zip"
  File "${FluxSDKDir}\7z.exe"
  File "${FluxSDKDir}\7z.dll"
  File "${FluxSDKDir}\VC_redist.x64.exe"

  Call ExtractThirdParty

  !insertmacro LogMessage "Copying user settings files to $LOCALAPPDATA\FChassis..."
  CreateDirectory "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to create directory: $LOCALAPPDATA\FChassis"

  IfFileExists "${FluxSDKBin}\FChassis.User.RecentFiles.JSON" recent_file_exists recent_file_missing
recent_file_exists:
  CopyFiles /SILENT "${FluxSDKBin}\FChassis.User.RecentFiles.JSON" "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to copy FChassis.User.RecentFiles.JSON to $LOCALAPPDATA\FChassis"
  Goto recent_file_done
recent_file_missing:
  !insertmacro LogError "FChassis.User.RecentFiles.JSON not found at ${FluxSDKBin}"
recent_file_done:

  IfFileExists "${FluxSDKBin}\FChassis.User.Settings.JSON" settings_file_exists settings_file_missing
settings_file_exists:
  CopyFiles /SILENT "${FluxSDKBin}\FChassis.User.Settings.JSON" "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to copy FChassis.User.Settings.JSON to $LOCALAPPDATA\FChassis"
  Goto settings_file_done
settings_file_missing:
  !insertmacro LogError "FChassis.User.Settings.JSON not found at ${FluxSDKBin}"
settings_file_done:

  !insertmacro LogMessage "Checking VC++ redistributable installation..."
  nsExec::ExecToStack '"$INSTDIR\VC_redist.x64.exe" /install /quiet /norestart'
  Pop $0
  !insertmacro LogVar "VC++ redistributable installation result" $0

  !insertmacro LogMessage "Adding to PATH environment variable..."
  EnVar::SetHKLM
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH before modification" $0

  EnVar::AddValue "Path" "$INSTDIR\Bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin"
  Pop $0
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin"
  Pop $0

  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH after modification" $0

  !insertmacro BroadcastEnvChange

  !insertmacro LogMessage "Writing registry entries..."
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${COMPANY}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Bin\Resources\FChassis.ico"
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1

  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "Installed" "1"
  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "Version" "${VERSION}"
  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "InstallPath" "$INSTDIR"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  !insertmacro LogMessage "=== Main Installation Section completed ==="
SectionEnd

; -----------------------------------------------------------------------------
; Uninstaller Section  -> full rollback + blast $INSTDIR
; -----------------------------------------------------------------------------
Section "Uninstall"
  !insertmacro LogMessage "=== Starting Uninstallation ==="
  SetRegView 64

  ; Shortcuts
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"

  ; PATH removals (system)
  EnVar::SetHKLM
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH before uninstall modification" $0

  EnVar::DeleteValue "Path" "$INSTDIR\Bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin"
  Pop $0
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin"
  Pop $0

  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH after uninstall modification" $0

  ; Registry cleanup
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"

  ; AppData cleanup (per-user)
  Delete "$AppDataDir\FChassis\FChassis.User.RecentFiles.JSON"
  Delete "$AppDataDir\FChassis\FChassis.User.Settings.JSON"
  RMDir "$AppDataDir\FChassis"

  ; Blast install directory (handles Uninstall.exe via delayed rmdir if needed)
  !insertmacro PurgeInstDir

  ; Env change broadcast
  !insertmacro BroadcastEnvChange

  !insertmacro LogMessage "=== Uninstallation completed ==="
SectionEnd
