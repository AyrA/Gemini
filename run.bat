@ECHO OFF
SETLOCAL
PUSHD "%~dp0"
CD Gemini.Web
SET OUT=Bin\Release\net6.0
IF EXIST "%OUT%" RD /S /Q "%OUT%\.."
dotnet build -c Release
IF NOT EXIST "%OUT%\Gemini.Web.exe" GOTO NOBUILD
ROBOCOPY /S /E wwwroot "%OUT%\wwwroot"
CD "%OUT%"
GOTO RNDPORT

:RNDPORT
SET PORT=%RANDOM%
IF %PORT% LSS 10000 GOTO RNDPORT

Gemini.Web.exe --urls http://localhost:%PORT%
GOTO END

:NOBUILD
ECHO Unable to build the application.
PAUSE
GOTO END

:END
POPD
