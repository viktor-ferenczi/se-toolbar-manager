@echo off
if [%1]==[] goto usage

SET name=ToolbarManager
SET version=%~1
SET p7z="C:\Program Files\7-Zip\7z.exe"

mkdir %version%

REM SET harmony=ToolbarManager\bin\Release\0Harmony.dll

SET client_bin=ToolbarManager\bin\Release\%name%.dll
SET client_pkg=%name%-%version%

mkdir "%client_pkg%"

REM copy /y "%harmony%" "%client_pkg%\"
copy /y "%client_bin%" "%client_pkg%\"

SET zip_name=%name%-%version%.zip

%p7z% a -tzip "%zip_name%" "%client_pkg%"

rd /s /q "%client_pkg%"

REM Why the folder cannot be deleted from here?!
rmdir "%client_pkg%"

echo %zip_name%
echo Done

goto :eof

:usage
@echo Usage: %0 VERSION

:eof
cd ..
pause