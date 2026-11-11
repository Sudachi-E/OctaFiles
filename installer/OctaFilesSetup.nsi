; Files setup: installs a plain file layout for the current user, no package registration.
Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef PAYLOAD
!error "PAYLOAD required"
!endif
!ifndef OUTPUT
!error "OUTPUT required"
!endif
!ifndef VERSION
!error "VERSION required"
!endif
!ifndef ESTIMATED_SIZE
!error "ESTIMATED_SIZE required"
!endif

!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\OctaFiles"

Name "Octa Files"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\Octa Files"
RequestExecutionLevel user
SetCompressor /SOLID lzma
AllowSkipFiles off
VIProductVersion "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "Octa Files"
VIAddVersionKey /LANG=1033 "FileDescription" "Octa Files Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Files Community"

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_WELCOME
!define MUI_COMPONENTSPAGE_NODESC
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN "$INSTDIR\OctaFiles.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Run Octa Files"
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

LangString AppSection ${LANG_ENGLISH} "Application and Start menu shortcut"
LangString DesktopSection ${LANG_ENGLISH} "Desktop shortcut"
LangString RemoveData ${LANG_ENGLISH} "Also delete Octa Files settings and data?$\r$\n$INSTDIR\data$\r$\n$\r$\nChoose No to keep them for a future installation."
LangString UninstallFailed ${LANG_ENGLISH} "Some files could not be removed. Close applications using this folder and run the uninstaller again."
LangString RequiresX64 ${LANG_ENGLISH} "64-bit Windows is required."

Function .onInit
    SetShellVarContext current
    StrCpy $INSTDIR "$LOCALAPPDATA\Programs\Octa Files"
    ${IfNot} ${RunningX64}
        MessageBox MB_ICONSTOP "$(RequiresX64)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
FunctionEnd

Section "$(AppSection)" AppSectionId
    SectionIn RO
    SetOutPath "$INSTDIR"
    SetOverwrite on
    File /r "${PAYLOAD}\*"
    WriteUninstaller "$INSTDIR\Uninstall.exe"
    CreateShortcut "$SMPROGRAMS\Octa Files.lnk" "$INSTDIR\OctaFiles.exe" "" "$INSTDIR\OctaFiles.exe" 0
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "Octa Files"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" '"$INSTDIR\OctaFiles.exe",0'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${ESTIMATED_SIZE}
    WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
    WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
    WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
SectionEnd

Section "$(DesktopSection)" DesktopSectionId
    CreateShortcut "$DESKTOP\Octa Files.lnk" "$INSTDIR\OctaFiles.exe" "" "$INSTDIR\OctaFiles.exe" 0
SectionEnd

Section "Uninstall"
    StrCpy $3 0
    MessageBox MB_YESNO|MB_ICONQUESTION|MB_DEFBUTTON2 "$(RemoveData)" /SD IDNO IDNO keepData
    StrCpy $3 1
keepData:
    StrCpy $4 0
    FindFirst $0 $1 "$INSTDIR\*"
loop:
    StrCmp $1 "" done
    StrCmp $1 "." next
    StrCmp $1 ".." next
    StrCmp $1 "Uninstall.exe" next
    ${If} $1 == "data"
    ${AndIf} $3 == 0
        Goto next
    ${EndIf}
    ClearErrors
    IfFileExists "$INSTDIR\$1\*" 0 file
    RMDir /r "$INSTDIR\$1"
    Goto checked
file:
    Delete "$INSTDIR\$1"
checked:
    ${If} ${Errors}
        StrCpy $4 1
    ${EndIf}
next:
    FindNext $0 $1
    Goto loop
done:
    FindClose $0
    ${If} $4 != 0
        MessageBox MB_ICONSTOP "$(UninstallFailed)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
    Delete "$DESKTOP\Octa Files.lnk"
    Delete "$SMPROGRAMS\Octa Files.lnk"
    DeleteRegKey HKCU "${UNINSTALL_KEY}"
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
SectionEnd
