@echo off
setlocal enabledelayedexpansion

REM Parameters: NAME SOURCE [TFM]
if "%~2" == "" (
    echo ERROR: Missing required parameters
    exit /b 1
)

REM Extract parameters and remove quotes
set "NAME=%~1"
set "SOURCE=%~2"
set "TFM=%~3"

REM Remove trailing backslash from SOURCE if applicable
if "%SOURCE:~-1%"=="\" set "SOURCE=%SOURCE:~0,-1%"

REM Resolve the built assembly
set "SRCFILE=%SOURCE%\%NAME%"
if not exist "%SRCFILE%" (
    echo ERROR: Source not found: %SRCFILE%
    exit /b 1
)

REM Route by target framework:
REM   net4x  (.NET Framework) -> Pulsar\Legacy\Local
REM   others (.NET 5+)        -> Pulsar\Interim\Local (only if Pulsar\Interim exists)
set "PULSAR=%AppData%\Pulsar"
set "EDITION=Interim"
echo(%TFM% | findstr /b /i "net4" >nul && set "EDITION=Legacy"
if "%TFM%"=="" set "EDITION=Legacy"

if /i "%EDITION%"=="Interim" (
    REM Only deploy the .NET build when the Interim Pulsar edition is installed
    if not exist "%PULSAR%\Interim" (
        echo Pulsar Interim not installed, skipping %TFM% deploy: %PULSAR%\Interim
        exit /b 0
    )
    set "PLUGIN_DIR=%PULSAR%\Interim\Local"
    if not exist "!PLUGIN_DIR!" mkdir "!PLUGIN_DIR!"
) else (
    set "PLUGIN_DIR=%PULSAR%\Legacy\Local"
    if not exist "!PLUGIN_DIR!" (
        echo Missing Local plugin folder: !PLUGIN_DIR!
        echo Pulsar not installed?
        exit /b 2
    )
)

REM Copy the plugin into the plugin directory
echo Copying "%SRCFILE%" to "!PLUGIN_DIR!\"
copy /y "%SRCFILE%" "!PLUGIN_DIR!\"
if !ERRORLEVEL! NEQ 0 (
    echo ERROR: Could not copy "%NAME%", make sure the game does not run and try again.
    exit /b 1
)

exit /b 0
