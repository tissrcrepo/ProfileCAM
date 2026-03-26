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

; RTF Constants
!define SF_RTF 0x0002

Name "${APPNAME} ${VERSION}"
OutFile "FChassis-Installer-${VERSION}.exe"
InstallDir "${INSTALLDIR}"
RequestExecutionLevel admin

; -----------------------------------------------------------------------------
; Variables
; -----------------------------------------------------------------------------
Var InstalledState ; 0 = not installed, 1 = same version, 2 = older version, 3 = newer version
Var ExistingInstallDir
Var ExistingVersion
Var ExtractionCompleted
Var LogFileHandle
Var LocalAppDataDir
Var AppDataDir
Var LicenseAgree ; Tracks license agreement (1 = accept, 0 = do not accept)

; Dialog controls for license page
Var hwnd
Var LicenseRichEdit
Var AcceptButton
Var DeclineButton

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

; Custom License Page
Page custom LicensePageCreate LicensePageLeave

!define MUI_PAGE_CUSTOMFUNCTION_PRE DirectoryPre
!insertmacro MUI_PAGE_DIRECTORY

!define MUI_PAGE_CUSTOMFUNCTION_SHOW InstFilesShow
!define MUI_PAGE_CUSTOMFUNCTION_ABORT OnInstFilesAbort
!insertmacro MUI_PAGE_INSTFILES

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

; -----------------------------------------------------------------------------
; License Page Functions with RTF Support
; -----------------------------------------------------------------------------
Function LicensePageCreate
  !insertmacro LogMessage "=== Creating License Page ==="
  
  StrCpy $2 "C:\FluxSDK\EULA\FChassis-License-Agreement.txt"
  IfFileExists $2 license_file_found
    !insertmacro LogError "License file not found"
    MessageBox MB_OK|MB_ICONSTOP "License agreement file not found."
    Abort

license_file_found:
  !insertmacro MUI_HEADER_TEXT "License Agreement" "Please review the license terms before installing ${APPNAME}"
  nsDialogs::Create 1018
  Pop $hwnd
  ${If} $hwnd == error
    Abort
  ${EndIf}
  
  ${NSD_CreateLabel} 0 0 100% 20u "Please read the following license agreement:"
  Pop $0
  
  ; Create the text control
  ${NSD_CreateText} 0 25u 100% 130u ""
  Pop $LicenseRichEdit
  ${NSD_AddStyle} $LicenseRichEdit ${WS_VSCROLL}|${WS_HSCROLL}|${ES_MULTILINE}|${ES_AUTOVSCROLL}|${ES_AUTOHSCROLL}|${ES_READONLY}
  
  ; Read file and ensure proper Windows line endings
  FileOpen $0 $2 r
  ${If} $0 != ""
    StrCpy $1 ""
  read_loop:
    FileRead $0 $3
    ${If} ${Errors}
      Goto read_done
    ${EndIf}
    ; Force Windows line endings
    StrCpy $1 "$1$3$\r$\n"
    Goto read_loop
  read_done:
    FileClose $0
    ; Set the text
    SendMessage $LicenseRichEdit ${WM_SETTEXT} 0 "STR:$1"
  ${EndIf}
  
  ${NSD_CreateButton} 20% 160u 30% 14u "&Accept"
  Pop $AcceptButton
  ${NSD_OnClick} $AcceptButton OnAcceptLicense
  
  ${NSD_CreateButton} 55% 160u 30% 14u "&Do not Accept"
  Pop $DeclineButton
  ${NSD_OnClick} $DeclineButton OnDeclineLicense
  
  GetDlgItem $0 $HWNDPARENT 1
  EnableWindow $0 0
  ${NSD_SetFocus} $AcceptButton
  
  nsDialogs::Show
FunctionEnd

Function OnAcceptLicense
  Pop $0
  !insertmacro LogMessage "User clicked Accept button"
  StrCpy $LicenseAgree 1
  ; Enable Next button and proceed
  GetDlgItem $0 $HWNDPARENT 1
  EnableWindow $0 1
  SendMessage $HWNDPARENT ${WM_COMMAND} 1 0
FunctionEnd

Function OnDeclineLicense
  Pop $0
  !insertmacro LogMessage "User clicked Do not Accept button"
  StrCpy $LicenseAgree 0
  
  ; Show confirmation message
  MessageBox MB_YESNO|MB_ICONQUESTION "You have chosen not to accept the license agreement.$\nThe installation will now exit.$\n$\nAre you sure you want to cancel the installation?" IDYES decline_confirmed
  Return
  
decline_confirmed:
  !insertmacro LogMessage "User confirmed license decline, exiting"
  Quit
FunctionEnd

Function LicensePageLeave
  !insertmacro LogMessage "=== License Page Leave ==="
  ${If} $LicenseAgree == 0
    !insertmacro LogMessage "License not accepted, staying on license page"
    MessageBox MB_OK|MB_ICONINFORMATION "You must accept the license agreement to continue with the installation."
    Abort
  ${EndIf}
  !insertmacro LogMessage "License accepted, proceeding to next page"
FunctionEnd

; -----------------------------------------------------------------------------
; Helper: broadcast env change
; -----------------------------------------------------------------------------
!macro BroadcastEnvChange
  !insertmacro LogMessage "Broadcasting environment change..."
  System::Call 'USER32::SendMessageTimeout(i ${HWND_BROADCAST}, i ${WM_SETTINGCHANGE}, i 0, w "Environment", i 0, i 5000, i 0)'
  !insertmacro LogMessage "Environment change broadcast completed"
!macroend

; -----------------------------------------------------------------------------
; Function to delete installer on abort
; -----------------------------------------------------------------------------
Function DeleteInstaller
  !insertmacro LogMessage "Deleting installer executable..."
  Sleep 1000
  ExecWait '"cmd.exe" /C ping 127.0.0.1 -n 2 > nul & del /f /q "$EXEPATH"'
  !insertmacro LogMessage "Installer deletion command executed: $EXEPATH"
FunctionEnd

; -----------------------------------------------------------------------------
; Function to handle early abortion (version conflicts)
; -----------------------------------------------------------------------------
Function .onGUIEnd
  ${If} ${Errors}
    !insertmacro LogMessage "Installation aborted with errors, deleting installer..."
    Call DeleteInstaller
  ${EndIf}
FunctionEnd

; -----------------------------------------------------------------------------
; Function called when installation fails
; -----------------------------------------------------------------------------
Function .onInstFailed
  !insertmacro LogMessage "Installation failed, deleting installer..."
  Call DeleteInstaller
FunctionEnd

; -----------------------------------------------------------------------------
; Function called when installation succeeds
; -----------------------------------------------------------------------------
Function .onInstSuccess
  ; Notify system about changes
  !insertmacro BroadcastEnvChange
  
  Call CreateShortcuts
  
  !insertmacro LogMessage "Installation completed successfully!"
FunctionEnd

; -----------------------------------------------------------------------------
; Version comparison function
; -----------------------------------------------------------------------------
Function CompareVersions
  Exch $0 ; version 1
  Exch
  Exch $1 ; version 2
  Push $2
  Push $3
  Push $4
  Push $5
  Push $6
  Push $7
  Push $8

  !insertmacro LogVar "Comparing version" $0
  !insertmacro LogVar "With version" $1

  ; Parse version 1
  StrCpy $8 $0
  ${WordFind} $8 "." "E+1" $2  ; major1
  ${WordFind} $8 "." "E+2" $3  ; minor1
  ${WordFind} $8 "." "E+3" $4  ; patch1

  ; Parse version 2
  StrCpy $8 $1
  ${WordFind} $8 "." "E+1" $5  ; major2
  ${WordFind} $8 "." "E+2" $6  ; minor2
  ${WordFind} $8 "." "E+3" $7  ; patch2

  ; Handle missing parts as 0
  ${If} $4 == ""
    StrCpy $4 "0"
  ${EndIf}
  ${If} $7 == ""
    StrCpy $7 "0"
  ${EndIf}

  ; Compare major
  IntCmp $2 $5 major_equal major1_greater major2_greater

major1_greater:
  !insertmacro LogMessage "Version $0 is greater than $1"
  StrCpy $0 "1"
  goto done
major2_greater:
  !insertmacro LogMessage "Version $0 is less than $1"
  StrCpy $0 "-1"
  goto done
major_equal:
  ; Compare minor
  IntCmp $3 $6 minor_equal minor1_greater minor2_greater

minor1_greater:
  !insertmacro LogMessage "Version $0 is greater than $1"
  StrCpy $0 "1"
  goto done
minor2_greater:
  !insertmacro LogMessage "Version $0 is less than $1"
  StrCpy $0 "-1"
  goto done
minor_equal:
  ; Compare patch
  IntCmp $4 $7 0 patch_diff

patch_diff:
  IntOp $0 $4 - $7
  ${If} $0 > 0
    !insertmacro LogMessage "Version $0 is greater than $1"
  ${ElseIf} $0 < 0
    !insertmacro LogMessage "Version $0 is less than $1"
  ${Else}
    !insertmacro LogMessage "Versions are equal"
  ${EndIf}
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
; CreateShortcuts Function with Icon Support
; -----------------------------------------------------------------------------
Function CreateShortcuts
  !insertmacro LogMessage "=== Creating shortcuts ==="

  ; ---------- Define paths ----------
  StrCpy $R1 "$INSTDIR\Bin\Resources\FChassis.ico" ; Icon path
  StrCpy $R2 "$INSTDIR\Bin\FChassis.exe"           ; Executable path
  !insertmacro LogVar "Icon path" $R1
  !insertmacro LogVar "Executable path" $R2

  ; ---------- Ensure target executable exists ----------
  IfFileExists "$R2" exe_found
    !insertmacro LogError "Target executable not found: $R2"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Error: FChassis.exe not found at $R2. Shortcuts cannot be created."
    Goto _Done
exe_found:
  !insertmacro LogMessage "Target executable found: $R2"

  ; ---------- Ensure icon file exists ----------
  ${If} ${FileExists} "$R1"
    !insertmacro LogMessage "Icon file found: $R1"
  ${Else}
    !insertmacro LogError "Icon file not found: $R1"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Warning: Icon file not found at $R1. Using default icon."
  ${EndIf}

  ; ---------- Try ALL USERS Start Menu ----------
  SetShellVarContext all
  StrCpy $R0 "all"
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  !insertmacro LogMessage "Creating Start Menu folder (all users): $9"

  ; Create Start Menu folder
  CreateDirectory "$9"
  IfErrors 0 folder_created_all
    !insertmacro LogError "Failed to create Start Menu directory (all users): $9"
    Goto _TryUserContext
folder_created_all:
  !insertmacro LogMessage "Start Menu folder created/exists (all users): $9"

  ; Create Start Menu shortcut
  ClearErrors
  Delete "$9\${APPNAME}.lnk" ; Remove stale shortcut
  ${If} ${FileExists} "$R1"
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}
  IfErrors 0 sm_shortcut_created
    !insertmacro LogError "Failed to create Start Menu shortcut (all users): $9\${APPNAME}.lnk"
    Goto _TryUserContext
sm_shortcut_created:
  !insertmacro LogMessage "Start Menu shortcut created (all users): $9\${APPNAME}.lnk"

  ; ---------- Fallback to CURRENT USER for Start Menu ----------
_TryUserContext:
  SetShellVarContext current
  StrCpy $R0 "current"
  StrCpy $9 "$SMPROGRAMS\${APPNAME}"
  !insertmacro LogMessage "Creating Start Menu folder (current user): $9"

  ; Create Start Menu folder
  CreateDirectory "$9"
  IfErrors 0 folder_created_user
    !insertmacro LogError "Failed to create Start Menu directory (current user): $9"
    Goto _CreateDesktop
folder_created_user:
  !insertmacro LogMessage "Start Menu folder created/exists (current user): $9"

  ; Create Start Menu shortcut
  ClearErrors
  Delete "$9\${APPNAME}.lnk" ; Remove stale shortcut
  ${If} ${FileExists} "$R1"
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$9\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}
  IfErrors 0 user_sm_shortcut_created
    !insertmacro LogError "Failed to create Start Menu shortcut (current user): $9\${APPNAME}.lnk"
    Goto _CreateDesktop
user_sm_shortcut_created:
  !insertmacro LogMessage "Start Menu shortcut created (current user): $9\${APPNAME}.lnk"

  ; ---------- Create Desktop shortcut (try CURRENT USER first) ----------
_CreateDesktop:
  SetShellVarContext current
  !insertmacro LogVar "Desktop directory (current user)" "$DESKTOP"

  ; Remove stale desktop shortcut
  ClearErrors
  Delete "$DESKTOP\${APPNAME}.lnk"

  ; Create Desktop shortcut for current user
  ${If} ${FileExists} "$R1"
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}
  IfErrors 0 desktop_shortcut_created
    !insertmacro LogError "Failed to create Desktop shortcut (current user): $DESKTOP\${APPNAME}.lnk"
    Goto _TryAllUsersDesktop
desktop_shortcut_created:
  !insertmacro LogMessage "Desktop shortcut created (current user): $DESKTOP\${APPNAME}.lnk"
  Goto _Done

  ; ---------- Fallback to ALL USERS Desktop ----------
_TryAllUsersDesktop:
  !insertmacro LogMessage "Falling back to all users context for Desktop shortcut"
  SetShellVarContext all
  !insertmacro LogVar "Desktop directory (all users)" "$DESKTOP"

  ; Remove stale desktop shortcut
  ClearErrors
  Delete "$DESKTOP\${APPNAME}.lnk"

  ; Create Desktop shortcut for all users
  ${If} ${FileExists} "$R1"
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R1" 0 SW_SHOWNORMAL
  ${Else}
    CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$R2" "" "$R2" 0 SW_SHOWNORMAL
  ${EndIf}
  IfErrors 0 all_users_desktop_created
    !insertmacro LogError "Failed to create Desktop shortcut (all users): $DESKTOP\${APPNAME}.lnk"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Error: Could not create Desktop shortcut for FChassis."
    Goto _Done
all_users_desktop_created:
  !insertmacro LogMessage "Desktop shortcut created (all users): $DESKTOP\${APPNAME}.lnk"

  ; ---------- Wrap up ----------
_Done:
  !insertmacro LogMessage "=== Shortcut creation completed ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Installation detection
; -----------------------------------------------------------------------------
Function .onInit
  ; Set 64-bit registry view for initialization
  SetRegView 64
  
  ; Initialize variables
  StrCpy $InstalledState 0 ; Default to not installed
  StrCpy $ExistingInstallDir "${INSTALLDIR}"
  StrCpy $ExistingVersion ""
  StrCpy $ExtractionCompleted 0
  StrCpy $LogFileHandle 0 ; Initialize to 0 (invalid handle)
  StrCpy $LicenseAgree 0 ; Initialize license agreement to not accepted

  ; Get LOCALAPPDATA for log file
  ReadRegStr $LocalAppDataDir HKCU "Volatile Environment" "LOCALAPPDATA"
  ${If} $LocalAppDataDir == ""
    StrCpy $LocalAppDataDir "$TEMP" ; Fallback to TEMP directory
  ${EndIf}
  
  ; Get APPDATA for user settings
  ReadRegStr $AppDataDir HKCU "Volatile Environment" "APPDATA"
  ${If} $AppDataDir == ""
    StrCpy $AppDataDir "$LocalAppDataDir" ; Fallback to LOCALAPPDATA if APPDATA is not available
  ${EndIf}
  !insertmacro LogVar "AppDataDir" $AppDataDir
  
  ; Ensure log directory exists
  CreateDirectory "$LocalAppDataDir"
  
  ; Initialize logging with error handling
  FileOpen $LogFileHandle "$LocalAppDataDir\FChassis_Install.log" w
  ${If} $LogFileHandle == ""
    ; If file opening failed, try TEMP directory as fallback
    StrCpy $LocalAppDataDir "$TEMP"
    FileOpen $LogFileHandle "$LocalAppDataDir\FChassis_Install.log" w
    ${If} $LogFileHandle == ""
      ; If still failing, just continue without logging to file
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

  ; Now log the start message using our macro (handles invalid file handles)
  !insertmacro LogMessage "=== Starting .onInit function ==="
  !insertmacro LogVar "LocalAppDataDir" $LocalAppDataDir

  !insertmacro LogMessage "Checking registry for existing installation..."

  ; Check if already installed via uninstall registry
  ReadRegStr $0 HKLM "${UNINSTALL_KEY}" "UninstallString"
  IfErrors check_install_flag
  !insertmacro LogMessage "Found uninstall registry key"
  !insertmacro LogVar "UninstallString" $0
  
  ; Get existing installation directory
  ReadRegStr $ExistingInstallDir HKLM "${UNINSTALL_KEY}" "InstallLocation"
  StrCmp $ExistingInstallDir "" 0 get_version
  StrCpy $ExistingInstallDir "${INSTALLDIR}"
  !insertmacro LogVar "ExistingInstallDir" $ExistingInstallDir
  
  get_version:
  ; Get installed version
  ReadRegStr $ExistingVersion HKLM "${UNINSTALL_KEY}" "DisplayVersion"
  IfErrors check_install_flag
  !insertmacro LogVar "ExistingVersion" $ExistingVersion
  
  ; Compare versions
  Push $ExistingVersion
  Push "${VERSION}"
  Call CompareVersions
  Pop $1
  !insertmacro LogVar "Version comparison result" $1
  
  ${If} $1 == "0"
    StrCpy $InstalledState 1 ; Same version installed
    !insertmacro LogMessage "Same version already installed"
  ${ElseIf} $1 == "1"
    StrCpy $InstalledState 3 ; Newer version installed
    !insertmacro LogMessage "Newer version already installed"
  ${Else}
    StrCpy $InstalledState 2 ; Older version installed
    !insertmacro LogMessage "Older version installed"
  ${EndIf}
  
  Goto done
  
  check_install_flag:
  !insertmacro LogMessage "Checking custom install flag registry..."
  ; Check our custom install flag
  ReadRegStr $0 HKLM "${INSTALL_FLAG_KEY}" "Installed"
  StrCmp $0 "1" 0 done
  !insertmacro LogMessage "Found custom install flag"
  
  ReadRegStr $ExistingVersion HKLM "${INSTALL_FLAG_KEY}" "Version"
  ReadRegStr $ExistingInstallDir HKLM "${INSTALL_FLAG_KEY}" "InstallPath"
  !insertmacro LogVar "ExistingVersion from custom key" $ExistingVersion
  !insertmacro LogVar "ExistingInstallDir from custom key" $ExistingInstallDir
  
  ; Compare versions if we found version info
  StrCmp $ExistingVersion "" done 0
  Push $ExistingVersion
  Push "${VERSION}"
  Call CompareVersions
  Pop $1
  !insertmacro LogVar "Version comparison result (custom key)" $1
  
  ${If} $1 == "0"
    StrCpy $InstalledState 1 ; Same version installed
    !insertmacro LogMessage "Same version installed (custom key)"
  ${ElseIf} $1 == "1"
    StrCpy $InstalledState 3 ; Newer version installed
    !insertmacro LogMessage "Newer version installed (custom key)"
  ${Else}
    StrCpy $InstalledState 2 ; Older version installed
    !insertmacro LogMessage "Older version installed (custom key)"
  ${EndIf}
  
  done:
  ; Set install directory to existing installation path
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
    
    ; Delete installer after showing message
    !insertmacro LogMessage "Deleting installer due to version conflict..."
    Call DeleteInstaller
    
    Abort
  ${EndIf}
  !insertmacro LogMessage "=== DirectoryPre function completed ==="
FunctionEnd

Function InstFilesShow
  !insertmacro LogMessage "=== InstFiles page shown ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Function to extract thirdParty.zip with progress feedback
; -----------------------------------------------------------------------------
Function ExtractThirdParty
  !insertmacro LogMessage "=== Starting ExtractThirdParty function ==="
  
  ; Check if extraction is already done
  IfFileExists "$INSTDIR\Bin\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin\TKernel.dll" extraction_complete
  !insertmacro LogMessage "ThirdParty extraction needed"
  
  DetailPrint "Extracting thirdParty.zip..."
  DetailPrint "This may take several minutes (90,000+ files)..."
  !insertmacro LogMessage "Extracting thirdParty.zip (90,000+ files, may take several minutes)"
  
  ; Show progress message
  SetDetailsPrint listonly
  DetailPrint "Extracting: Please wait patiently..."
  SetDetailsPrint both
  
  ; Extract using 7z with timeout
  !insertmacro LogMessage "Starting 7z extraction process..."
  nsExec::ExecToStack '"$INSTDIR\7z.exe" x "$INSTDIR\thirdParty.zip" -o"$INSTDIR" -y'
  Pop $0 ; Exit code
  Pop $1 ; Output
  
  ${If} $0 != 0
    !insertmacro LogError "7-Zip extraction failed with code: $0"
    !insertmacro LogError "7-Zip output: $1"
    DetailPrint "7-Zip extraction failed with code: $0"
    DetailPrint "Output: $1"
    MessageBox MB_OK|MB_ICONEXCLAMATION "Failed to extract third-party components. Installation may be incomplete."
  ${Else}
    StrCpy $ExtractionCompleted 1
    !insertmacro LogMessage "Third-party components extraction completed successfully"
    DetailPrint "Third-party components extraction completed successfully."
  ${EndIf}
  
  extraction_complete:
  !insertmacro LogMessage "ThirdParty extraction already complete or completed"
  !insertmacro LogMessage "=== ExtractThirdParty function completed ==="
FunctionEnd

; -----------------------------------------------------------------------------
; Function to handle cancellation during installation
; -----------------------------------------------------------------------------
Function OnInstFilesAbort
  !insertmacro LogMessage "=== Installation abort requested ==="
  MessageBox MB_YESNO|MB_ICONQUESTION "Are you sure you want to cancel the installation?" IDYES +2
  Return
  
  !insertmacro LogMessage "User confirmed cancellation, performing cleanup"
  MessageBox MB_OK|MB_ICONINFORMATION "Installation canceled. Cleaning up..."
  
  ; Remove installed files and directories
  !insertmacro LogMessage "Removing installed files and directories..."
  RMDir /r "$INSTDIR\Bin"
  RMDir /r "$INSTDIR\cs"
  RMDir /r "$INSTDIR\de"
  RMDir /r "$INSTDIR\es"
  RMDir /r "$INSTDIR\fr"
  RMDir /r "$INSTDIR\hoops"
  RMDir /r "$INSTDIR\it"
  RMDir /r "$INSTDIR\ja"
  RMDir /r "$INSTDIR\ko"
  RMDir /r "$INSTDIR\pl"
  RMDir /r "$INSTDIR\pt-BR"
  RMDir /r "$INSTDIR\ru"
  RMDir /r "$INSTDIR\runtimes"
  RMDir /r "$INSTDIR\tr"
  RMDir /r "$INSTDIR\zh-Hans"
  RMDir /r "$INSTDIR\zh-Hant"
  RMDir /r "$INSTDIR\Resources"  ; Remove Resources directory
  
  ; Remove individual files
  !insertmacro LogMessage "Removing individual files..."
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.exe"
  Delete "$INSTDIR\*.json"
  Delete "$INSTDIR\*.xml"
  Delete "$INSTDIR\*.wad"
  Delete "$INSTDIR\*.pdb"
  Delete "$INSTDIR\*.exp"
  Delete "$INSTDIR\*.lib"
  Delete "$INSTDIR\*.ico"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$INSTDIR\thirdParty.zip"
  Delete "$INSTDIR\7z.exe"
  Delete "$INSTDIR\7z.dll"
  Delete "$INSTDIR\VC_redist.x64.exe"
  
  ; Remove user settings files from %APPDATA%\FChassis
  !insertmacro LogMessage "Removing user settings files..."
  Delete "$AppDataDir\FChassis\FChassis.User.RecentFiles.JSON"
  Delete "$AppDataDir\FChassis\FChassis.User.Settings.JSON"
  RMDir "$AppDataDir\FChassis" ; Remove directory if empty
  
  ; Remove shortcuts if they were created
  !insertmacro LogMessage "Removing shortcuts..."
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"
  
  ; Remove registry entries if they were created
  !insertmacro LogMessage "Removing registry entries..."
  SetRegView 64 ; Ensure 64-bit registry view for cleanup
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"
  
  ; Remove environment variables if they were added
  !insertmacro LogMessage "Removing environment variables..."
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
  
  ; Broadcast environment change
  !insertmacro BroadcastEnvChange
  
  ; Delete installer
  Call DeleteInstaller
  
  ; Close log file
  FileClose $LogFileHandle
  
  Abort
FunctionEnd

; -----------------------------------------------------------------------------
; Installation Section
; -----------------------------------------------------------------------------
Section "Main Installation" SecMain
  !insertmacro LogMessage "=== Starting Main Installation Section ==="
  SetRegView 64 ; Ensure 64-bit registry view for installation
  
  ; Set output path to the installation directory
  SetOutPath "$INSTDIR"
  
  ; Ensure Bin directory exists
  CreateDirectory "$INSTDIR\Bin"
  !insertmacro LogMessage "Created directory: $INSTDIR\Bin"
  
  ; Check if we need to uninstall previous version
  ${If} $InstalledState == 1
    !insertmacro LogMessage "Same version already installed, proceeding with reinstallation"
  ${ElseIf} $InstalledState == 2
    !insertmacro LogMessage "Older version detected, proceeding with upgrade"
  ${EndIf}
  
  ; Copy main application files
  !insertmacro LogMessage "Copying main application files..."
  File /r "${FluxSDKDir}\*.*"
  
  ; Copy license file to installation directory
  !insertmacro LogMessage "Copying license file to installation directory..."
  IfFileExists "${FluxSDKDir}\FChassis-License-Agreement.rtf" license_file_exists license_file_missing
  
license_file_exists:
  CopyFiles /SILENT "${FluxSDKDir}\FChassis-License-Agreement.rtf" "$INSTDIR"
  !insertmacro LogMessage "License file copied to: $INSTDIR\FChassis-License-Agreement.rtf"
  Goto license_file_done
  
license_file_missing:
  !insertmacro LogError "License file not found at source: ${FluxSDKDir}\FChassis-License-Agreement.rtf"
  
license_file_done:
  
  ; Copy third-party components
  !insertmacro LogMessage "Copying third-party components..."
  File "${FluxSDKDir}\thirdParty.zip"
  File "${FluxSDKDir}\7z.exe"
  File "${FluxSDKDir}\7z.dll"
  File "${FluxSDKDir}\VC_redist.x64.exe"
  
  ; Extract third-party components
  Call ExtractThirdParty
  
  ; Copy user settings files to %LOCALAPPDATA%\FChassis
  !insertmacro LogMessage "Copying user settings files to $LOCALAPPDATA\FChassis..."
  CreateDirectory "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to create directory: $LOCALAPPDATA\FChassis"
  
  ; Copy FChassis.User.RecentFiles.JSON
  IfFileExists "${FluxSDKBin}\FChassis.User.RecentFiles.JSON" recent_file_exists recent_file_missing
recent_file_exists:
  !insertmacro LogMessage "Copying ${FluxSDKBin}\FChassis.User.RecentFiles.JSON to $LOCALAPPDATA\FChassis"
  CopyFiles /SILENT "${FluxSDKBin}\FChassis.User.RecentFiles.JSON" "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to copy FChassis.User.RecentFiles.JSON to $LOCALAPPDATA\FChassis"
  Goto recent_file_done
recent_file_missing:
  !insertmacro LogError "FChassis.User.RecentFiles.JSON not found at ${FluxSDKBin}"
recent_file_done:

  ; Copy FChassis.User.Settings.JSON
  IfFileExists "${FluxSDKBin}\FChassis.User.Settings.JSON" settings_file_exists settings_file_missing
settings_file_exists:
  !insertmacro LogMessage "Copying ${FluxSDKBin}\FChassis.User.Settings.JSON to $LOCALAPPDATA\FChassis"
  CopyFiles /SILENT "${FluxSDKBin}\FChassis.User.Settings.JSON" "$LOCALAPPDATA\FChassis"
  IfErrors 0 +2
    !insertmacro LogError "Failed to copy FChassis.User.Settings.JSON to $LOCALAPPDATA\FChassis"
  Goto settings_file_done
settings_file_missing:
  !insertmacro LogError "FChassis.User.Settings.JSON not found at ${FluxSDKBin}"
settings_file_done:
  
  ; Install VC++ redistributable if needed
  !insertmacro LogMessage "Checking VC++ redistributable installation..."
  nsExec::ExecToStack '"$INSTDIR\VC_redist.x64.exe" /install /quiet /norestart'
  Pop $0
  !insertmacro LogVar "VC++ redistributable installation result" $0
  
  ; Add to PATH environment variable
  !insertmacro LogMessage "Adding to PATH environment variable..."
  EnVar::SetHKLM
  ; Log current PATH before modification
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH before modification" $0
  
  EnVar::AddValue "Path" "$INSTDIR\Bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\Bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\Bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64 to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64 to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin to PATH"
  ${EndIf}
  
  EnVar::AddValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to add $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin to PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully added $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin to PATH"
  ${EndIf}
  
  ; Log PATH after modification
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH after modification" $0
  
  ; Broadcast environment change
  !insertmacro BroadcastEnvChange
  
  ; Write registry entries for uninstallation
  !insertmacro LogMessage "Writing registry entries..."
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "${COMPANY}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Bin\Resources\FChassis.ico"  ; Use same icon path
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  
  ; Write our custom install flag
  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "Installed" "1"
  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "Version" "${VERSION}"
  WriteRegStr HKLM "${INSTALL_FLAG_KEY}" "InstallPath" "$INSTDIR"
  
  ; Create uninstaller
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  
  !insertmacro LogMessage "=== Main Installation Section completed ==="
SectionEnd

; -----------------------------------------------------------------------------
; Uninstaller Section
; -----------------------------------------------------------------------------
Section "Uninstall"
  !insertmacro LogMessage "=== Starting Uninstallation ==="
  SetRegView 64 ; Ensure 64-bit registry view for uninstallation
  
  ; Remove shortcuts
  !insertmacro LogMessage "Removing shortcuts..."
  ; Try 'all' context first
  SetShellVarContext all
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  !insertmacro LogMessage "Attempting to remove Start Menu directory: $SMPROGRAMS\${APPNAME}"
  RMDir "$SMPROGRAMS\${APPNAME}" ; Remove Start Menu directory if empty
  IfErrors 0 sm_dir_removed
  !insertmacro LogError "Failed to remove Start Menu directory: $SMPROGRAMS\${APPNAME}"
  Goto sm_dir_done
sm_dir_removed:
  !insertmacro LogMessage "Successfully removed Start Menu directory: $SMPROGRAMS\${APPNAME}"
sm_dir_done:
  ; Try 'current' context for Start Menu cleanup
  SetShellVarContext current
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  !insertmacro LogMessage "Attempting to remove user Start Menu directory: $SMPROGRAMS\${APPNAME}"
  RMDir "$SMPROGRAMS\${APPNAME}" ; Remove user Start Menu directory if empty
  IfErrors 0 user_sm_dir_removed
  !insertmacro LogError "Failed to remove user Start Menu directory: $SMPROGRAMS\${APPNAME}"
  Goto user_sm_dir_done
user_sm_dir_removed:
  !insertmacro LogMessage "Successfully removed user Start Menu directory: $SMPROGRAMS\${APPNAME}"
user_sm_dir_done:
  ; Delete desktop shortcut
  Delete "$DESKTOP\${APPNAME}.lnk"
  
  ; Remove from PATH environment variable
  !insertmacro LogMessage "Removing from PATH environment variable..."
  EnVar::SetHKLM
  ; Log current PATH before modification
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH before uninstall modification" $0
  
  EnVar::DeleteValue "Path" "$INSTDIR\Bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\Bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\Bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\draco-1.4.1-vc14-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\opencascade-7.7.0\win64\vc14\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\ffmpeg-3.3.4-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freeimage-3.17.0-vc14-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tbb_2021.5-vc14-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\freetype-2.5.5-vc14-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64 from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\openvr-1.14.15-64\bin\win64 from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\qt5.11.2-vc14-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\rapidjson-1.1.0\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\tcltk-86-64\bin from PATH"
  ${EndIf}
  
  EnVar::DeleteValue "Path" "$INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin"
  Pop $0
  ${If} $0 != 0
    !insertmacro LogError "Failed to remove $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin from PATH, error code: $0"
  ${Else}
    !insertmacro LogMessage "Successfully removed $INSTDIR\thirdParty\OpenCASCADE-7.7.0-vc14-64\vtk-6.1.0-vc14-64\bin from PATH"
  ${EndIf}
  
  ; Log PATH after modification
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Control\Session Manager\Environment" "Path"
  !insertmacro LogVar "System PATH after uninstall modification" $0
  
  ; Remove user settings files from %APPDATA%\FChassis
  !insertmacro LogMessage "Removing user settings files..."
  Delete "$AppDataDir\FChassis\FChassis.User.RecentFiles.JSON"
  Delete "$AppDataDir\FChassis\FChassis.User.Settings.JSON"
  RMDir "$AppDataDir\FChassis" ; Remove directory if empty
  
  ; Broadcast environment change
  !insertmacro BroadcastEnvChange
  
  ; Remove registry entries
  !insertmacro LogMessage "Removing registry entries..."
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "${INSTALL_FLAG_KEY}"
  
  ; Remove files and directories
  !insertmacro LogMessage "Removing files and directories..."
  RMDir /r "$INSTDIR\Bin"
  RMDir /r "$INSTDIR\cs"
  RMDir /r "$INSTDIR\de"
  RMDir /r "$INSTDIR\es"
  RMDir /r "$INSTDIR\fr"
  RMDir /r "$INSTDIR\hoops"
  RMDir /r "$INSTDIR\it"
  RMDir /r "$INSTDIR\ja"
  RMDir /r "$INSTDIR\ko"
  RMDir /r "$INSTDIR\pl"
  RMDir /r "$INSTDIR\pt-BR"
  RMDir /r "$INSTDIR\ru"
  RMDir /r "$INSTDIR\runtimes"
  RMDir /r "$INSTDIR\tr"
  RMDir /r "$INSTDIR\zh-Hans"
  RMDir /r "$INSTDIR\zh-Hant"
  RMDir /r "$INSTDIR\thirdParty"
  RMDir /r "$INSTDIR\Resources"  ; Remove Resources directory
  
  ; Remove individual files including license file
  Delete "$INSTDIR\*.dll"
  Delete "$INSTDIR\*.exe"
  Delete "$INSTDIR\*.json"
  Delete "$INSTDIR\*.xml"
  Delete "$INSTDIR\*.wad"
  Delete "$INSTDIR\*.pdb"
  Delete "$INSTDIR\*.exp"
  Delete "$INSTDIR\*.lib"
  Delete "$INSTDIR\*.ico"
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$INSTDIR\thirdParty.zip"
  Delete "$INSTDIR\7z.exe"
  Delete "$INSTDIR\7z.dll"
  Delete "$INSTDIR\VC_redist.x64.exe"
  Delete "$INSTDIR\FChassis-License-Agreement.rtf" ; Remove license file
  
  ; Remove installation directory if empty
  RMDir "$INSTDIR"
  
  !insertmacro LogMessage "=== Uninstallation completed ==="
SectionEnd