@echo off
rem Abre el editor de Godot directamente en este proyecto (sin el administrador de proyectos).
rem Si tu Godot 4.6 .NET esta en otra carpeta, define la variable GODOT con la ruta al .exe.
if not defined GODOT set "GODOT=%USERPROFILE%\Documents\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
if not exist "%GODOT%" (
  echo No encontre Godot en "%GODOT%".
  echo Define la variable GODOT con la ruta a Godot 4.6 .NET ^(la version mono^).
  pause
  exit /b 1
)
start "" "%GODOT%" -e --path "%~dp0."
