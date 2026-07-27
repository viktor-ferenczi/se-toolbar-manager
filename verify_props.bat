REM This file is ran in a pre-build event when data from "Directory.Build.props" is required
REM It assumes "Directory.Build.props" and "verify_props" are both in the solution directory

@echo off
setlocal

set SOLUTION=%~dp0
set STATUS=0

REM Loop through each parameter provided
for %%a in (%*) do (
    
    REM Detect if the parameter is not a valid path
    if not exist "%%~a" (

        REM Raise an error for each bad path - this will prevent the build from completing.
        echo ERROR: Invalid path "%%~a" in "%SOLUTION%Directory.Build.props" 1>&2
        set STATUS=1
    )
)

exit /b %STATUS%
