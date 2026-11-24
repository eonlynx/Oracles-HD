@echo off
rem Build helper for machines where the .NET 8 SDK is installed per-user.
rem Usage: build.cmd [dotnet build arguments]
setlocal
if not defined DOTNET_ROOT set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
set "PATH=%DOTNET_ROOT%;%PATH%"
"%DOTNET_ROOT%\dotnet.exe" build %*
