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
GOTO RUN

:RUN
ECHO Starting application...
ECHO If your browser won't open,
ECHO you can manually go to http://localhost:%PORT%

Gemini.Web.exe --urls http://localhost:%PORT%
GOTO END

:NOBUILD
ECHO Unable to build the application.
ECHO Most likely cause is that the .NET 6 SDK is missing.
ECHO To build this from source, you cannot use the runtime,
ECHO and must download the SDK version.
ECHO Go to https://dotnet.microsoft.com/en-us/download to get it.
PAUSE
GOTO END

:END
POPD
