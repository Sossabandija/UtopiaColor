@echo off
rem Compila y abre el visor directamente, sin pasar por el editor de Godot.
rem Si tu Godot 4.6 .NET esta en otra carpeta, define la variable GODOT con la ruta al .exe.
if not defined GODOT set "GODOT=%USERPROFILE%\Documents\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64\Godot_v4.6.3-stable_mono_win64.exe"
if not exist "%GODOT%" (
  echo No encontre Godot en "%GODOT%".
  echo Define la variable GODOT con la ruta a Godot 4.6 .NET ^(la version mono^).
  pause
  exit /b 1
)
echo Compilando...
dotnet build "%~dp0UtopiaViewer.csproj" -nologo -v q
if errorlevel 1 (
  echo La compilacion fallo. Revisa los errores de arriba.
  pause
  exit /b 1
)
start "" "%GODOT%" --path "%~dp0."
