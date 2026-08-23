@echo off
REM Launches the Ten21 backend (dotnet watch) and frontend (Angular dev server) in
REM separate windows so both hot-reload independently during local development, then
REM opens the app in your default browser once the frontend is actually serving.
REM Each window uses /k (stays open) rather than /c, so a crash's output is still
REM visible instead of the window vanishing.

set ROOT=%~dp0

echo Starting Postgres (docker compose)...
docker compose -f "%ROOT%docker-compose.yml" up -d

echo Starting Ten21 API (dotnet watch run) on http://localhost:5080 ...
start "Ten21 API" cmd /k "cd /d "%ROOT%src\Ten21.Api" && dotnet watch run"

echo Starting Ten21 Frontend (ng serve) on http://localhost:4200 ...
start "Ten21 Frontend" cmd /k "cd /d "%ROOT%frontend" && npm start"

echo Waiting for the frontend to finish compiling...
set TRIES=0
:waitloop
set /a TRIES+=1
curl -s -o NUL -w "%%{http_code}" http://localhost:4200 > "%TEMP%\ten21_status.txt" 2>NUL
set /p STATUS=<"%TEMP%\ten21_status.txt"
if "%STATUS%"=="200" goto opened
if %TRIES% GEQ 90 goto giveup
timeout /t 1 /nobreak >NUL
goto waitloop

:opened
start http://localhost:4200
goto end

:giveup
echo Frontend didn't respond after 90s -- check the "Ten21 Frontend" window for errors.
echo Once it says "Application bundle generation complete", open http://localhost:4200 yourself.

:end
