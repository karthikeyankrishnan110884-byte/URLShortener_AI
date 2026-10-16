@echo off
REM Runs all quality gates (format, build, tests + coverage >= 85%%, vulnerable packages). See scripts\quality-gates.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\quality-gates.ps1" %*
