@echo off
REM Build, test, run the URL Shortener API and open Swagger. Logs go to .\logs
setlocal
cd /d "%~dp0"
if not exist logs mkdir logs

echo === .NET SDKs ===
dotnet --list-sdks > logs\sdks.log 2>&1
type logs\sdks.log

echo === Build ===
dotnet build -c Debug > logs\build.log 2>&1
set BUILD_RC=%ERRORLEVEL%
type logs\build.log | findstr /R /C:"error" /C:"warn" /C:"Build succeeded" /C:"Elapsed"
if not "%BUILD_RC%"=="0" ( echo BUILD FAILED - see logs\build.log & echo BUILD_FAILED> logs\status.txt & pause & exit /b 1 )

echo === Test ===
dotnet test --no-build --logger "console;verbosity=normal" > logs\test.log 2>&1
set TEST_RC=%ERRORLEVEL%
type logs\test.log | findstr /R /C:"Passed!" /C:"Failed!" /C:"Failed " /C:"Total tests"
if not "%TEST_RC%"=="0" ( echo TESTS FAILED - see logs\test.log & echo TESTS_FAILED> logs\status.txt & pause & exit /b 1 )

echo OK> logs\status.txt
echo === Run (Ctrl+C to stop) - opening http://localhost:5104/swagger ===
start "" powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep 6; Start-Process 'http://localhost:5104/swagger'"
dotnet run --project src\UrlShortener.Api --no-build --launch-profile http
