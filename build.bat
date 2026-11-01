@echo off
rem Builds bsp2obj.exe and model2obj.exe into bin\ as self-contained Windows x64 programs (no .NET install needed to
rem run them). Needs the .NET 10 SDK and the reclaimer submodule: git submodule update --init
cd /d "%~dp0"
if not exist reclaimer\Reclaimer.Blam\Reclaimer.Blam.csproj (
  echo The reclaimer submodule is missing: run  git submodule update --init
  exit /b 1
)
for %%p in (bsp2obj model2obj) do (
  dotnet publish %%p\%%p.csproj -c Release -r win-x64 --self-contained true -o bin -v q -nologo || exit /b 1
)
echo Built: %~dp0bin\bsp2obj.exe and %~dp0bin\model2obj.exe
