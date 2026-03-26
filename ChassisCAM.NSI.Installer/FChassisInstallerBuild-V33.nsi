; -----------------------------------------------------------------------------
; FChassis Installer - Version 1.0.24
; -----------------------------------------------------------------------------
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
!define VERSION "1.0.24" 
!define COMPANY "Teckinsoft Neuronics Pvt. Ltd."
!define INSTALLDIR "C:\FChassis"
!define FluxSDKBin "C:\FluxSDK\Bin"
!define FluxSDKDir "C:\FluxSDK"
!define OCCT_INSTALL_PATH "C:\OCCT_7_7_0"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
!define INSTALL_FLAG_KEY "Software\${COMPANY}\${APPNAME}"
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
Var IsInstalling
Var ShowRepairPage
Var RepairPageHWND

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
; PATH Management Macro (Fixed as per ChatGPT's diagnosis)
; -----------------------------------------------------------------------------
; -----------------------------------------------------------------------------
; PATH Management Macro (Fixed)
; -----------------------------------------------------------------------------
; -----------------------------------------------------------------------------
; PATH Management Macro (Fixed - using unique label without decimals)
; -----------------------------------------------------------------------------
!macro AddToSystemPathIfMissing dir
  Push $0
  Push $1
  Push $2
  Push $3
  
  ; Create a unique label name without decimals
  !define _UNIQUE_LABEL _AddToPath_${__LINE__}
  
  SetRegView 64
  ClearErrors
  ReadRegStr $0 HKLM \
    "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"

  ${If} ${Errors}
    !insertmacro LogError "FAILED to read System PATH. Aborting PATH modification for ${dir}."
    Goto ${_UNIQUE_LABEL}
  ${EndIf}

  ; Length check
  StrLen $1 $0
  ${If} $1 > 32000
    !insertmacro LogError "System PATH too long ($1 chars). Cannot append ${dir}."
    Goto ${_UNIQUE_LABEL}
  ${EndIf}

  ; Duplicate check
  StrCpy $2 "$0;"
  StrCpy $3 ";${dir};"
  System::Call "shlwapi::StrStrI(t r2, t r3) i .r2"
  ${If} $2 != 0
    !insertmacro LogMessage "${dir} already present in System PATH"
    Goto ${_UNIQUE_LABEL}
  ${EndIf}

  ; Append
  ${If} $0 == ""
    StrCpy $0 "${dir}"
  ${Else}
    StrCpy $0 "$0;${dir}"
  ${EndIf}

  WriteRegExpandStr HKLM \
    "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" \
    "Path" "$0"

  ${_UNIQUE_LABEL}:
  Pop $3
  Pop $2
  Pop $1
  Pop $0
  
  !undef _UNIQUE_LABEL
!macroend



; -----------------------------------------------------------------------------
; Custom Repair Page Interface
; -----------------------------------------------------------------------------
Function RepairPageCreate
  ${If} $ShowRepairPage != 1
    Abort
  ${EndIf}
 
  !insertmacro LogMessage "=== Creating Repair Page ==="
 
  !insertmacro MUI_HEADER_TEXT "Repair Installation" "Same version already exists"
 
  nsDialogs::Create 1018
  Pop $RepairPageHWND
 
  ${If} $RepairPageHWND == error
    Abort
  ${EndIf}
 
  ${NSD_CreateLabel} 0 30 100% 40u "Same version ($ExistingVersion) already exists. Do you want to repair?"
  Pop $0
 
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Repair"
  EnableWindow $0 1
 
  GetDlgItem $0 $HWNDPARENT 2
  EnableWindow $0 1
 
  GetDlgItem $0 $HWNDPARENT 3
  EnableWindow $0 1
 
  !insertmacro LogMessage "=== Repair Page Buttons Customized ==="
 
  nsDialogs::Show
  !insertmacro LogMessage "=== Repair Page Created ==="
FunctionEnd

Function RepairPageLeave
  ${If} $ShowRepairPage != 1
    Abort
  ${EndIf}
 
  !insertmacro LogMessage "=== Repair Page Leave ==="
  !insertmacro LogMessage "=== Repair Page Navigation Complete ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Pages
; -----------------------------------------------------------------------------
!define MUI_ABORTWARNING
!define MUI_UNABORTWARNING

!define MUI_PAGE_CUSTOMFUNCTION_SHOW WelcomeShow
!insertmacro MUI_PAGE_WELCOME

!insertmacro MUI_PAGE_LICENSE "${LICENSE_FILE}"

Page custom RepairPageCreate RepairPageLeave

!define MUI_PAGE_CUSTOMFUNCTION_PRE DirectoryPre
!define MUI_PAGE_CUSTOMFUNCTION_SHOW DirectoryShow
!insertmacro MUI_PAGE_DIRECTORY

!define MUI_PAGE_CUSTOMFUNCTION_PRE PreInstFiles
!define MUI_PAGE_CUSTOMFUNCTION_SHOW InstFilesShow
!insertmacro MUI_PAGE_INSTFILES

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!define MUI_CUSTOMFUNCTION_ABORT OnInstFilesAbort
!define MUI_INSTFILESPAGE_ABORTHEADER_TEXT "Installation Aborted"
!define MUI_INSTFILESPAGE_ABORTHEADER_SUBTEXT "The installation was canceled and changes have been rolled back."

!insertmacro MUI_LANGUAGE "English"

; -----------------------------------------------------------------------------
; Page Pre-functions and Show functions
; -----------------------------------------------------------------------------
Function WelcomeShow
  !insertmacro LogMessage "=== Welcome Page Show ==="
 
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Next"
 
  ${If} $InstalledState == 1
    StrCpy $ShowRepairPage 1
    !insertmacro LogMessage "Same version detected - will show repair page after license"
  ${Else}
    StrCpy $ShowRepairPage 0
    !insertmacro LogMessage "Fresh installation or upgrade - will skip repair page"
  ${EndIf}
 
  !insertmacro LogMessage "=== Welcome Page Buttons Customized (Always 'Next') ==="
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

Function DirectoryShow
  !insertmacro LogMessage "=== Directory Page Show ==="
 
  GetDlgItem $0 $HWNDPARENT 1
 
  ${If} $InstalledState == 1
    SendMessage $0 ${WM_SETTEXT} 0 "STR:Repair"
  ${Else}
    SendMessage $0 ${WM_SETTEXT} 0 "STR:Install"
  ${EndIf}
 
  !insertmacro LogMessage "=== Directory Page Buttons Customized ==="
FunctionEnd

Function PreInstFiles
  !insertmacro LogMessage "=== Entering InstFiles page (setting IsInstalling flag) ==="
  StrCpy $IsInstalling 1
FunctionEnd

Function InstFilesShow
  !insertmacro LogMessage "=== InstFiles page shown ==="
  GetDlgItem $0 $HWNDPARENT 2
  EnableWindow $0 1
FunctionEnd

; -----------------------------------------------------------------------------
; Helper: Broadcast Environment Change
; -----------------------------------------------------------------------------
!macro BroadcastEnvChange
  !insertmacro LogMessage "Broadcasting environment change..."
  System::Call 'USER32::SendMessageTimeout(i ${HWND_BROADCAST}, i ${WM_SETTINGCHANGE}, i 0, w "Environment", i 0, i 5000, i 0)'
  !insertmacro LogMessage "Environment change broadcast completed"
!macroend

; -----------------------------------------------------------------------------
; Helper: Purge $INSTDIR (Blocking)
; -----------------------------------------------------------------------------
!macro PurgeInstDirBlocking KILL7Z
  SetDetailsPrint both
  !insertmacro LogMessage "Blasting install directory: $INSTDIR (blocking cleanup)"
  ${If} "${KILL7Z}" == "1"
    !insertmacro LogMessage "Attempting to kill 7z.exe..."
    ExecWait '"cmd.exe" /C taskkill /F /IM 7z.exe /T >nul 2>&1'
    Sleep 1000
  ${EndIf}
  SetOutPath "$TEMP"
  ExecWait '"cmd.exe" /C attrib -r -s -h "$INSTDIR\*" /S /D >nul 2>&1'
  StrCpy $R9 0
  ${Do}
    ClearErrors
    RMDir /r "$INSTDIR"
    ${IfNot} ${Errors}
      DetailPrint "Install folder removed successfully."
      ${Break}
    ${EndIf}
    IntOp $R9 $R9 + 1
    DetailPrint "Waiting for files to close... (attempt $R9)"
    Sleep 1000
    ${If} $R9 >= 30
      ${Break}
    ${EndIf}
  ${Loop}
  ${If} ${FileExists} "$INSTDIR"
    DetailPrint "Some files still in use; attempting forced removal..."
    ExecWait '"cmd.exe" /C rmdir /S /Q "$INSTDIR"'
  ${EndIf}
  ${If} ${FileExists} "$INSTDIR"
    System::Call 'kernel32::MoveFileEx(t "$INSTDIR", t "", i 4)'
    DetailPrint "Could not remove all files now. Cleanup will complete after restart."
  ${EndIf}
!macroend

; -----------------------------------------------------------------------------
; Delete Installer EXE
; -----------------------------------------------------------------------------
Function DeleteInstaller
  !insertmacro LogMessage "Deleting installer executable..."
  Sleep 1000
  ExecWait '"cmd.exe" /C ping 127.0.0.1 -n 2 > nul & del /f /q "$EXEPATH"'
  !insertmacro LogMessage "Scheduled installer deletion: $EXEPATH"
FunctionEnd

; -----------------------------------------------------------------------------
; GUI End Hook
; -----------------------------------------------------------------------------
Function .onGUIEnd
  ${If} ${Errors}
    !insertmacro LogMessage "Installation aborted with errors, deleting installer..."
    Call DeleteInstaller
  ${EndIf}
FunctionEnd

; -----------------------------------------------------------------------------
; Install Failed Hook
; -----------------------------------------------------------------------------
Function .onInstFailed
  StrCpy $IsInstalling 0
  !insertmacro LogMessage "Installation failed, deleting installer..."
  Call DeleteInstaller
FunctionEnd

; -----------------------------------------------------------------------------
; Install Success Hook
; -----------------------------------------------------------------------------
Function .onInstSuccess
  StrCpy $IsInstalling 0
  !insertmacro BroadcastEnvChange
  Call CreateShortcuts
  !insertmacro LogMessage "Installation completed successfully!"
FunctionEnd

; -----------------------------------------------------------------------------
; Version Comparison
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
; Create Shortcuts
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
  IfFileExists "$R1" +2 0
    !insertmacro LogError "Icon file not found: $R1"

  SetShellVarContext all
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  CreateDirectory "$9"
  Delete "$9\${APPNAME}.lnk"
  IfFileExists "$R1" 0 +2
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  IfFileExists "$R1" +2 0
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL

  SetShellVarContext current
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  CreateDirectory "$9"
  Delete "$9\${APPNAME}.lnk"
  IfFileExists "$R1" 0 +2
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  IfFileExists "$R1" +2 0
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL

  SetShellVarContext current
  Delete "$DESKTOP\${APPNAME}.lnk"
  IfFileExists "$R1" 0 +2
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  IfFileExists "$R1" +2 0
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL

  !insertmacro LogMessage "=== Shortcut creation completed ==="

_Done:
FunctionEnd

; -----------------------------------------------------------------------------
; Check if OCCT is installed
; -----------------------------------------------------------------------------
Function CheckOCCTInstalled
  Push $0
 
  ; Check if OCCT directory exists by checking for a specific DLL file
  ${If} ${FileExists} "${OCCT_INSTALL_PATH}\opencascade-7.7.0\win64\vc14\bin\TKernel.dll"
    !insertmacro LogMessage "OCCT is already installed at ${OCCT_INSTALL_PATH}"
    StrCpy $0 1
  ${Else}
    !insertmacro LogMessage "OCCT is not installed at ${OCCT_INSTALL_PATH}"
    StrCpy $0 0
  ${EndIf}
 
  Exch $0
FunctionEnd

; -----------------------------------------------------------------------------
; Installation Detection
; -----------------------------------------------------------------------------
Function .onInit
  SetRegView 64
  StrCpy $InstalledState 0
  StrCpy $ExistingInstallDir "${INSTALLDIR}"
  StrCpy $ExistingVersion ""
  StrCpy $ExtractionCompleted 0
  StrCpy $LogFileHandle 0
  StrCpy $ShowRepairPage 0

  ReadRegStr $LocalAppDataDir HKCU "Volatile Environment" "LOCALAPPDATA"
  ${If} $LocalAppDataDir == ""
    ReadRegStr $LocalAppDataDir HKCU "Environment" "LOCALAPPDATA"
    ${If} $LocalAppDataDir == ""
      StrCpy $LocalAppDataDir "$PROFILE\AppData\Local"
    ${EndIf}
  ${EndIf}
  !insertmacro LogVar "LocalAppDataDir" $LocalAppDataDir

  ReadRegStr $AppDataDir HKCU "Volatile Environment" "APPDATA"
  ${If} $AppDataDir == ""
    StrCpy $AppDataDir "$LocalAppDataDir"
  ${EndIf}
  !insertmacro LogVar "AppDataDir" $AppDataDir

  CreateDirectory "$LocalAppDataDir"
  CreateDirectory "$LocalAppDataDir\FChassis"
  CreateDirectory "$LocalAppDataDir\FChassis\Sample"
  CreateDirectory "$LocalAppDataDir\FChassis\Data"

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
    StrCpy $ShowRepairPage 1
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
    StrCpy $ShowRepairPage 1
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
  !insertmacro LogVar "ShowRepairPage" $ShowRepairPage
  
  !insertmacro LogMessage "=== .onInit function completed ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Installation Abort Handler
; -----------------------------------------------------------------------------
Function OnInstFilesAbort
  !insertmacro LogMessage "=== Installation abort requested ==="
  ${If} $IsInstalling != 1
    !insertmacro LogMessage "Aborted before installation started - no cleanup needed"
    Goto done
  ${EndIf}
  
  !insertmacro LogMessage "Aborted during installation - performing cleanup (rollback)"
  MessageBox MB_OK|MB_ICONINFORMATION "Installation canceled. Rolling back..."

  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"

  SetRegView 64
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"
 
  DeleteRegKey SHCTX "Software\Classes\Applications\FChassis.exe"
  DeleteRegKey SHCTX "Software\Classes\FChassis.Document"
  DeleteRegKey SHCTX "Software\Classes\.fchassis"
 
  DeleteRegKey SHCTX "Software\Classes\CLSID\{YOUR-APP-CLSID}"
 
  DeleteRegKey HKLM "Software\${COMPANY}\${APPNAME}"
  DeleteRegKey HKCU "Software\${COMPANY}\${APPNAME}"
 
  DeleteRegValue HKLM "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}"
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "${APPNAME}"

  Delete "$LOCALAPPDATA\FChassis\FChassis.User.RecentFiles.JSON"
  Delete "$LOCALAPPDATA\FChassis\FChassis.User.Settings.JSON"
  RMDir "$LOCALAPPDATA\FChassis"

  !insertmacro BroadcastEnvChange
  !insertmacro PurgeInstDirBlocking 1
  Call DeleteInstaller
  FileClose $LogFileHandle

done:
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
  IfFileExists "${FluxSDKDir}\FChassis-License-Agreement.txt" license_file_exists license_file_missing

license_file_exists:
  CopyFiles /SILENT "${FluxSDKDir}\FChassis-License-Agreement.txt" "$INSTDIR"
  !insertmacro LogMessage "License file copied to: $INSTDIR\FChassis-License-Agreement.txt"
  Goto license_file_done

license_file_missing:
  !insertmacro LogError "License file not found at source: ${FluxSDKDir}\FChassis-License-Agreement.txt"

license_file_done:
  !insertmacro LogMessage "Copying third-party components..."
  File "${FluxSDKDir}\7z.exe"
  File "${FluxSDKDir}\7z.dll"
  File "${FluxSDKDir}\VC_redist.x64.exe"
  File "${FluxSDKDir}\opencascade-7.7.0-vc14-64.exe"

  ; Check if OCCT needs to be installed
  !insertmacro LogMessage "Checking if OCCT is already installed..."
  Call CheckOCCTInstalled
  Pop $0
  
  ${If} $0 == 0
    !insertmacro LogMessage "Installing OCCT..."
    DetailPrint "Installing OpenCASCADE 7.7.0..."
    
    nsExec::ExecToStack '"$INSTDIR\opencascade-7.7.0-vc14-64.exe" /VERYSILENT /DIR=${OCCT_INSTALL_PATH}'
    Pop $0
    Pop $1
    
    !insertmacro LogVar "OCCT installation result" $0
    !insertmacro LogVar "OCCT installation output" $1
   
    ; Wait for installation to complete
    Sleep 5000
   
    ${If} $0 != 0
      !insertmacro LogError "OCCT installation failed with code: $0"
      MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to install OpenCASCADE components. Installation may be incomplete."
    ${Else}
      !insertmacro LogMessage "OCCT installation completed successfully"
    ${EndIf}
  ${Else}
    !insertmacro LogMessage "OCCT is already installed, skipping installation"
  ${EndIf}
 
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

  !insertmacro LogMessage "Adding to System PATH environment variable (using corrected macro)..."
  
  ; Add application bin directory to System PATH
  !insertmacro LogMessage "Adding $INSTDIR\Bin to System PATH"
  !insertmacro AddToSystemPathIfMissing "$INSTDIR\Bin"

  ; Add OCCT directories to System PATH
  !insertmacro LogMessage "Adding OCCT binaries path to System PATH..."
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\draco-1.4.1-vc14-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\opencascade-7.7.0\win64\vc14\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\ffmpeg-3.3.4-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\freeimage-3.17.0-vc14-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\tbb_2021.5-vc14-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\freetype-2.5.5-vc14-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\openvr-1.14.15-64\bin\win64"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\qt5.11.2-vc14-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\rapidjson-1.1.0\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\tcltk-86-64\bin"
  !insertmacro AddToSystemPathIfMissing "${OCCT_INSTALL_PATH}\vtk-6.1.0-vc14-64\bin"
  
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
; Uninstaller: Remove from System PATH (Fixed - clean version)
; -----------------------------------------------------------------------------
Function un.RemoveFromSystemPath
  Exch $0  ; dir to remove
  Push $1
  Push $2
  Push $3
  Push $4
  Push $5
  Push $6
  Push $7
  
  !insertmacro LogMessage "=== Removing $0 from System PATH ==="
  
  ; Ensure 64-bit registry view
  SetRegView 64
  
  ; Read System PATH
  ClearErrors
  ReadRegStr $1 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  ${If} ${Errors}
    !insertmacro LogError "Failed to read System PATH during uninstall! Cannot remove $0"
    Goto done
  ${EndIf}
  
  !insertmacro LogVar "System PATH before removal" $1
  
  ${If} $1 != ""
    ; Add semicolons for easier matching
    StrCpy $2 "$1;"
    StrCpy $3 ";$0;"
    StrLen $4 $3
    StrCpy $7 0  ; Found flag
    
    ; Check if path is at the beginning
    StrLen $5 $0
    StrCpy $6 $1 $5
    ${If} $6 == $0
      ; Path is at beginning
      IntOp $5 $5 + 1
      StrCpy $1 $1 "" $5
      StrCpy $7 1
      !insertmacro LogMessage "Found $0 at beginning of PATH"
    ${EndIf}
    
    ; Check if path is in the middle or at the end
    ${If} $7 == 0
      ; Not found at beginning, search in the string
      StrCpy $2 "$1;"
      
      ; Use StrStrI to find the path
      System::Call "shlwapi::StrStrI(t r2, t r3) i .r5"
      ${If} $5 != 0
        ; Found it, remove it
        IntOp $5 $5 - 1  ; Adjust for the leading semicolon
        StrCpy $6 $2 $5  ; Before the match
        IntOp $5 $5 + $4
        StrCpy $2 $2 "" $5  ; After the match
        StrCpy $1 "$6$2"
        StrCpy $7 1  ; Set found flag
        !insertmacro LogMessage "Found $0 in middle of PATH"
      ${EndIf}
    ${EndIf}
    
    ${If} $7 == 1
      ; Clean up the result
      ; Remove trailing semicolon if present
      StrLen $4 $1
      ${If} $4 > 0
        IntOp $4 $4 - 1
        StrCpy $2 $1 1 $4
        ${If} $2 == ";"
          StrCpy $1 $1 $4
        ${EndIf}
      ${EndIf}
      
      ; Remove leading semicolon if present
      ${If} $1 != ""
        StrCpy $2 $1 1
        ${If} $2 == ";"
          StrCpy $1 $1 "" 1
        ${EndIf}
      ${EndIf}
      
      !insertmacro LogVar "System PATH after removal" $1
      
      ; Write back to System PATH
      WriteRegExpandStr HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path" $1
      ${If} ${Errors}
        !insertmacro LogError "Failed to update System PATH during uninstall!"
      ${Else}
        !insertmacro LogMessage "Successfully removed $0 from System PATH"
      ${EndIf}
    ${Else}
      !insertmacro LogMessage "$0 not found in System PATH"
    ${EndIf}
  ${Else}
    !insertmacro LogMessage "System PATH is empty, nothing to remove"
  ${EndIf}
  
done:
  Pop $7
  Pop $6
  Pop $5
  Pop $4
  Pop $3
  Pop $2
  Pop $1
  Pop $0
FunctionEnd

; -----------------------------------------------------------------------------
; Uninstaller Init (with logging)
; -----------------------------------------------------------------------------
Function un.onInit
  SetRegView 64
  
  ; Re-open log file for uninstaller
  ReadRegStr $LocalAppDataDir HKCU "Volatile Environment" "LOCALAPPDATA"
  ${If} $LocalAppDataDir == ""
    StrCpy $LocalAppDataDir "$PROFILE\AppData\Local"
  ${EndIf}
  
  CreateDirectory "$LocalAppDataDir\FChassis"
  FileOpen $LogFileHandle "$LocalAppDataDir\FChassis_Install.log" a
  ${If} $LogFileHandle != ""
    FileSeek $LogFileHandle 0 END
    FileWrite $LogFileHandle "$\r$\n=== Uninstallation Started: [$(^Time)] ===$\r$\n"
  ${EndIf}
FunctionEnd

; =============================================================================
; UNINSTALLER SECTION - REMOVES FCHASSIS APPLICATION
; =============================================================================
Section "Uninstall"
  !insertmacro LogMessage "=== STARTING UNINSTALLATION PROCESS ==="
  !insertmacro LogMessage "Installation Directory: $INSTDIR"
  !insertmacro LogMessage "Local AppData Directory: $LOCALAPPDATA"
  
  ; ===========================================================================
  ; 1. SET REGISTRY VIEW FOR 64-BIT SYSTEM
  ; ===========================================================================
  !insertmacro LogMessage "1. Setting registry view to 64-bit..."
  SetRegView 64
  !insertmacro LogMessage "   Registry view set to 64-bit"

  ; ===========================================================================
  ; 2. REMOVE SHORTCUTS (ALL USERS AND CURRENT USER)
  ; ===========================================================================
  !insertmacro LogMessage "2. Removing shortcuts..."
  
  ; Remove All Users shortcuts
  !insertmacro LogMessage "   Removing All Users shortcuts..."
  SetShellVarContext all
  
  ; Check and delete All Users Start Menu shortcut
  ${If} ${FileExists} "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    !insertmacro LogMessage "   Deleted: $SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  ${Else}
    !insertmacro LogMessage "   Shortcut not found (All Users): $SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  ${EndIf}
  
  ; Remove All Users Start Menu folder
  ${If} ${FileExists} "$SMPROGRAMS\${APPNAME}"
    RMDir "$SMPROGRAMS\${APPNAME}"
    !insertmacro LogMessage "   Removed directory: $SMPROGRAMS\${APPNAME}"
  ${Else}
    !insertmacro LogMessage "   Directory not found (All Users): $SMPROGRAMS\${APPNAME}"
  ${EndIf}
  
  ; Remove Current User shortcuts
  !insertmacro LogMessage "   Removing Current User shortcuts..."
  SetShellVarContext current
  
  ; Check and delete Current User Start Menu shortcut
  ${If} ${FileExists} "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
    !insertmacro LogMessage "   Deleted: $SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  ${Else}
    !insertmacro LogMessage "   Shortcut not found (Current User): $SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  ${EndIf}
  
  ; Remove Current User Start Menu folder
  ${If} ${FileExists} "$SMPROGRAMS\${APPNAME}"
    RMDir "$SMPROGRAMS\${APPNAME}"
    !insertmacro LogMessage "   Removed directory: $SMPROGRAMS\${APPNAME}"
  ${Else}
    !insertmacro LogMessage "   Directory not found (Current User): $SMPROGRAMS\${APPNAME}"
  ${EndIf}
  
  ; Remove Desktop shortcut
  ${If} ${FileExists} "$DESKTOP\${APPNAME}.lnk"
    Delete "$DESKTOP\${APPNAME}.lnk"
    !insertmacro LogMessage "   Deleted: $DESKTOP\${APPNAME}.lnk"
  ${Else}
    !insertmacro LogMessage "   Desktop shortcut not found: $DESKTOP\${APPNAME}.lnk"
  ${EndIf}
  
  !insertmacro LogMessage "   Shortcut removal completed"

  ; ===========================================================================
  ; 3. REMOVE APPLICATION FROM SYSTEM PATH ENVIRONMENT VARIABLE
  ; ===========================================================================
  !insertmacro LogMessage "3. Removing application from System PATH..."
  Push "$INSTDIR\Bin"
  Call un.RemoveFromSystemPath
  !insertmacro LogMessage "   PATH removal completed"

  ; ===========================================================================
  ; 4. REMOVE REGISTRY ENTRIES
  ; ===========================================================================
  !insertmacro LogMessage "4. Removing registry entries..."
  
  ; Remove standard uninstall registry key
  ClearErrors
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "DisplayName"
  ${IfNot} ${Errors}
    DeleteRegKey HKLM "${UNINSTALL_KEY}"
    !insertmacro LogMessage "   Deleted registry key: ${UNINSTALL_KEY}"
  ${Else}
    !insertmacro LogMessage "   Registry key not found: ${UNINSTALL_KEY}"
  ${EndIf}
  
  ; Remove custom install flag registry key
  ClearErrors
  ReadRegStr $0 HKLM "${INSTALL_FLAG_KEY}" "Installed"
  ${IfNot} ${Errors}
    DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"
    !insertmacro LogMessage "   Deleted registry key: ${INSTALL_FLAG_KEY}"
  ${Else}
    !insertmacro LogMessage "   Registry key not found: ${INSTALL_FLAG_KEY}"
  ${EndIf}
  
  !insertmacro LogMessage "   Registry cleanup completed"

  ; ===========================================================================
  ; 5. PRESERVE USER DATA FILES (DO NOT DELETE)
  ; ===========================================================================
  !insertmacro LogMessage "5. Preserving user data files..."
  
  ; Check if user data files exist
  ${If} ${FileExists} "$LOCALAPPDATA\FChassis\FChassis.User.RecentFiles.JSON"
    !insertmacro LogMessage "   User data file preserved: FChassis.User.RecentFiles.JSON"
    !insertmacro LogMessage "   Location: $LOCALAPPDATA\FChassis\FChassis.User.RecentFiles.JSON"
  ${Else}
    !insertmacro LogMessage "   User data file not found: FChassis.User.RecentFiles.JSON"
  ${EndIf}
  
  ${If} ${FileExists} "$LOCALAPPDATA\FChassis\FChassis.User.Settings.JSON"
    !insertmacro LogMessage "   User data file preserved: FChassis.User.Settings.JSON"
    !insertmacro LogMessage "   Location: $LOCALAPPDATA\FChassis\FChassis.User.Settings.JSON"
  ${Else}
    !insertmacro LogMessage "   User data file not found: FChassis.User.Settings.JSON"
  ${EndIf}
  
  ${If} ${FileExists} "$LOCALAPPDATA\FChassis"
    !insertmacro LogMessage "   User data directory preserved: $LOCALAPPDATA\FChassis"
  ${Else}
    !insertmacro LogMessage "   User data directory not found: $LOCALAPPDATA\FChassis"
  ${EndIf}
  
  !insertmacro LogMessage "   User data preservation completed"

  ; ===========================================================================
  ; 6. FORCEFULLY REMOVE INSTALLATION FILES AND FOLDERS
  ; ===========================================================================
  !insertmacro LogMessage "6. Forcefully removing installation files from: $INSTDIR"
  
  ; 6.1 Kill any running FChassis processes
  !insertmacro LogMessage "   6.1 Terminating running FChassis processes..."
  ExecWait '"cmd.exe" /C taskkill /F /IM FChassis.exe /T >nul 2>&1'
  Sleep 1500
  !insertmacro LogMessage "   Process termination attempted"
  
  ; 6.2 Remove file attributes (read-only, system, hidden)
  !insertmacro LogMessage "   6.2 Removing file attributes..."
  ExecWait '"cmd.exe" /C attrib -r -s -h "$INSTDIR\*" /S /D >nul 2>&1'
  !insertmacro LogMessage "   File attributes cleared"
  
  ; 6.3 Attempt to delete installation directory with retry logic
  !insertmacro LogMessage "   6.3 Attempting to delete installation directory..."
  StrCpy $R9 0  ; Counter for retry attempts
  
  ${Do}
    ClearErrors
    RMDir /r "$INSTDIR"
    
    ${IfNot} ${Errors}
      !insertmacro LogMessage "   Installation directory removed successfully on attempt $R9"
      ${Break}
    ${EndIf}
    
    IntOp $R9 $R9 + 1
    !insertmacro LogMessage "   Delete attempt $R9 failed, waiting before retry..."
    Sleep 1000
    
    ${If} $R9 >= 5
      !insertmacro LogMessage "   Multiple attempts failed, using aggressive deletion..."
      
      ; Try command-line deletion as fallback
      ExecWait '"cmd.exe" /C rmdir /S /Q "$INSTDIR" >nul 2>&1'
      ${Break}
    ${EndIf}
  ${Loop}
  
  ; 6.4 Check if directory still exists and handle accordingly
  ${If} ${FileExists} "$INSTDIR"
    !insertmacro LogMessage "   6.4 Directory still exists, scheduling deletion on system restart..."
    
    ; Use MoveFileEx with MOVEFILE_DELAY_UNTIL_REBOOT flag (value 4)
    System::Call 'kernel32::MoveFileEx(t "$INSTDIR", t "", i 4)'
    
    ${If} ${Errors}
      !insertmacro LogError "Failed to schedule directory deletion on reboot"
    ${Else}
      !insertmacro LogMessage "   Directory scheduled for deletion on next system restart"
    ${EndIf}
    
    DetailPrint "Note: Some files could not be removed immediately."
    DetailPrint "Remaining files will be deleted after system restart."
  ${Else}
    !insertmacro LogMessage "   6.4 Installation directory successfully removed"
  ${EndIf}
  
  !insertmacro LogMessage "   File removal process completed"

  ; ===========================================================================
  ; 7. BROADCAST ENVIRONMENT CHANGES
  ; ===========================================================================
  !insertmacro LogMessage "7. Broadcasting environment changes..."
  !insertmacro BroadcastEnvChange
  !insertmacro LogMessage "   Environment changes broadcasted"

  ; ===========================================================================
  ; 8. UNINSTALLATION COMPLETION
  ; ===========================================================================
  !insertmacro LogMessage "=== UNINSTALLATION PROCESS COMPLETED SUCCESSFULLY ==="
  !insertmacro LogMessage "Summary:"
  !insertmacro LogMessage "  - Shortcuts removed"
  !insertmacro LogMessage "  - PATH environment variable updated"
  !insertmacro LogMessage "  - Registry entries cleaned"
  !insertmacro LogMessage "  - User data files preserved"
  !insertmacro LogMessage "  - Installation files removed"
  !insertmacro LogMessage "  - Environment changes applied"
  
  ; Show completion message to user
  MessageBox MB_OK|MB_ICONINFORMATION \
    "${APPNAME} has been successfully uninstalled.$\n$\n\
    Note: Your user settings and recent files have been preserved at:$\n\
    $LOCALAPPDATA\FChassis$\n$\n\
    You can delete this folder manually if you don't want to keep these files."
  
SectionEnd

; =============================================================================
; HELPER MACRO: Force Remove Directory with Detailed Logging
; =============================================================================
!macro ForceRemoveDirectoryWithLogging dir
  /* 
  Description: Forcefully removes a directory with detailed logging and retry logic
  Parameters:
    dir - Directory path to remove
  */
  
  Push $R0
  Push $R1
  Push $R2
  
  !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Starting removal of: ${dir}"
  
  ${IfNot} ${FileExists} "${dir}"
    !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Directory does not exist: ${dir}"
    Goto done
  ${EndIf}
  
  ; Step 1: Kill related processes
  !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 1: Terminating related processes..."
  ExecWait '"cmd.exe" /C taskkill /F /IM FChassis.exe /T >nul 2>&1'
  ExecWait '"cmd.exe" /C taskkill /F /IM 7z.exe /T >nul 2>&1'
  Sleep 1000
  
  ; Step 2: Remove file attributes
  !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 2: Clearing file attributes..."
  ExecWait '"cmd.exe" /C attrib -r -s -h "${dir}\*" /S /D >nul 2>&1'
  
  ; Step 3: Attempt removal with retry logic
  !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 3: Attempting directory removal..."
  StrCpy $R1 0  ; Attempt counter
  
  ${ForEach} $R2 In 1 2 3 4 5
    IntOp $R1 $R1 + 1
    !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging]   Attempt $R1 of 5"
    
    ClearErrors
    RMDir /r "${dir}"
    
    ${IfNot} ${Errors}
      !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging]   Directory removed successfully"
      Goto success
    ${EndIf}
    
    ${If} $R1 < 5
      !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging]   Waiting before next attempt..."
      Sleep 1000
    ${EndIf}
  ${Next}
  
  ; Step 4: Try command-line force delete
  !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 4: Using command-line force delete..."
  ExecWait '"cmd.exe" /C rmdir /S /Q "${dir}" >nul 2>&1'
  
success:
  ; Step 5: Check if removal was successful
  ${If} ${FileExists} "${dir}"
    !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 5: Directory still exists, scheduling reboot deletion"
    System::Call 'kernel32::MoveFileEx(t "${dir}", t "", i 4)'
    !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging]   Directory scheduled for deletion on reboot"
  ${Else}
    !insertmacro LogMessage "[ForceRemoveDirectoryWithLogging] Step 5: Directory successfully removed"
  ${EndIf}
  
done:
  Pop $R2
  Pop $R1
  Pop $R0
!macroend

; =============================================================================
; HELPER MACRO: Clean Registry Thoroughly
; =============================================================================
!macro CleanRegistryThoroughly
  /*
  Description: Performs comprehensive registry cleanup for the application
  */
  
  !insertmacro LogMessage "[CleanRegistryThoroughly] Starting comprehensive registry cleanup..."
  
  ; List of registry paths to clean
  !define REG_PATHS \
    "Software\Classes\Applications\FChassis.exe" \
    "Software\Classes\FChassis.Document" \
    "Software\Classes\.fchassis" \
    "Software\Microsoft\Windows\CurrentVersion\Run" \
    "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" \
    "Software\${COMPANY}\${APPNAME}"
  
  ; Clean HKLM (64-bit view)
  SetRegView 64
  ${ForEach} $R0 In ${REG_PATHS}
    ${If} ${RegKeyExists} HKLM "$R0"
      DeleteRegKey HKLM "$R0"
      !insertmacro LogMessage "[CleanRegistryThoroughly] Deleted HKLM key: $R0"
    ${EndIf}
  ${Next}
  
  ; Clean HKCU
  ${ForEach} $R0 In ${REG_PATHS}
    ${If} ${RegKeyExists} HKCU "$R0"
      DeleteRegKey HKCU "$R0"
      !insertmacro LogMessage "[CleanRegistryThoroughly] Deleted HKCU key: $R0"
    ${EndIf}
  ${Next}
  
  !insertmacro LogMessage "[CleanRegistryThoroughly] Registry cleanup completed"
!macroend

; =============================================================================
; HELPER MACRO: Validate Uninstallation
; =============================================================================
!macro ValidateUninstallation
  /*
  Description: Validates that uninstallation was successful
  Returns: 1 if successful, 0 if issues found
  */
  
  Push $R0
  Push $R1
  
  !insertmacro LogMessage "[ValidateUninstallation] Starting validation..."
  StrCpy $R1 1  ; Assume success
  
  ; Check 1: Installation directory should not exist
  ${If} ${FileExists} "$INSTDIR"
    !insertmacro LogError "[ValidateUninstallation] FAILED: Installation directory still exists: $INSTDIR"
    StrCpy $R1 0
  ${Else}
    !insertmacro LogMessage "[ValidateUninstallation] PASSED: Installation directory removed"
  ${EndIf}
  
  ; Check 2: Uninstall registry key should not exist
  ${If} ${RegKeyExists} HKLM "${UNINSTALL_KEY}"
    !insertmacro LogError "[ValidateUninstallation] FAILED: Uninstall registry key still exists"
    StrCpy $R1 0
  ${Else}
    !insertmacro LogMessage "[ValidateUninstallation] PASSED: Uninstall registry key removed"
  ${EndIf}
  
  ; Check 3: User data should still exist
  ${If} ${FileExists} "$LOCALAPPDATA\FChassis"
    !insertmacro LogMessage "[ValidateUninstallation] PASSED: User data directory preserved"
  ${Else}
    !insertmacro LogMessage "[ValidateUninstallation] WARNING: User data directory not found"
  ${EndIf}
  
  ; Return result
  Exch $R1
  Pop $R0
  Exch $R1
  
  ${If} $R1 == 1
    !insertmacro LogMessage "[ValidateUninstallation] Validation PASSED"
  ${Else}
    !insertmacro LogMessage "[ValidateUninstallation] Validation FAILED - issues found"
  ${EndIf}
!macroend