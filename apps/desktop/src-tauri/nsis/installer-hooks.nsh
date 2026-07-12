; SPDX-License-Identifier: GPL-3.0-only

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
