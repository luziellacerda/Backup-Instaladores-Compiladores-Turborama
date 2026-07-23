@echo off
title TurboRama ScreenScraper Downloader
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0TurboRama-ScreenScraper.ps1"
if errorlevel 1 pause
