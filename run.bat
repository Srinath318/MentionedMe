@echo off
title Mentioned Me? - Launcher
cd /d "%~dp0"

if exist "bin\Release\net8.0-windows10.0.19041.0\MentionedMe.exe" (
    start "" "bin\Release\net8.0-windows10.0.19041.0\MentionedMe.exe"
    exit
)

if exist "bin\Debug\net8.0-windows10.0.19041.0\MentionedMe.exe" (
    start "" "bin\Debug\net8.0-windows10.0.19041.0\MentionedMe.exe"
    exit
)

echo Building and starting Mentioned Me?...
dotnet run --project MentionedMe.csproj
