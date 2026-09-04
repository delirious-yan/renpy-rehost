@echo off
rem Dev runner for the rehost CLI. Usage: run <command> [args]
rem   run detect    "C:\path\to\game"
rem   run preflight "C:\path\to\game"
rem   run convert   "C:\path\to\game" --serve
dotnet run --project "%~dp0src\RenpyRehost.Cli" -c Release -- %*
