; SPDX-License-Identifier: GPL-3.0-only

!include "WinMessages.nsh"

; The retained NSIS completion page can be shown before the themed progress bar
; finishes animating its final instruction-based updates. Once every install
; action has succeeded, collapse the range to one completed step so later NSIS
; bookkeeping remains visibly pinned at 100%.
!macro NSIS_HOOK_POSTINSTALL
  ${IfNot} ${Silent}
  ${AndIf} $mui.InstFilesPage.ProgressBar <> 0
    SendMessage $mui.InstFilesPage.ProgressBar ${PBM_SETRANGE32} 0 1
    SendMessage $mui.InstFilesPage.ProgressBar ${PBM_SETPOS} 1 0
  ${EndIf}
!macroend

; Tauri removes app data stored under the bundle identifier when the user selects
; "Delete app data". KM's native data caches intentionally live beside the
; current-user installation instead, so remove those known cache directories too.
; Never run this during an updater-driven uninstall.
!macro NSIS_HOOK_POSTUNINSTALL
  ${If} $DeleteAppDataCheckboxState = 1
  ${AndIf} $UpdateMode <> 1
    SetShellVarContext current
    RMDir /r "$LOCALAPPDATA\KM Editor\PokemonLegendsZACache"
    RMDir /r "$LOCALAPPDATA\KM Editor\ScarletVioletCache"
    RMDir "$LOCALAPPDATA\KM Editor"
  ${EndIf}
!macroend
