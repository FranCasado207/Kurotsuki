@echo off
setlocal
cd /d "%~dp0\.."

if "%JAVA_HOME%"=="" (
    echo JAVA_HOME nao definido. Aponta para um JDK 25.
    exit /b 1
)

call mvn -q clean package -DskipTests dependency:copy-dependencies -DoutputDirectory=target\lib -DincludeScope=runtime || exit /b 1

if not exist target\javafx mkdir target\javafx
move /y target\lib\javafx-*.jar target\javafx\ >nul

for /f %%v in ('call mvn -q help:evaluate -Dexpression^=project.version -DforceStdout') do set "VERSION=%%v"
set "APP_JAR=target\kurotsuki-%VERSION%.jar"

set "MODULES="
for /f "delims=" %%m in ('"%JAVA_HOME%\bin\jdeps" --multi-release 25 --ignore-missing-deps --print-module-deps --class-path "target\lib\*" %APP_JAR%') do set "MODULES=%%m"
set "MODULES=%MODULES%,javafx.base,javafx.graphics,javafx.controls,javafx.media"
echo Modules: %MODULES%

if exist target\runtime rmdir /s /q target\runtime
"%JAVA_HOME%\bin\jlink" --module-path "%JAVA_HOME%\jmods;target\javafx" --add-modules "%MODULES%" --strip-debug --no-header-files --no-man-pages --compress zip-6 --output target\runtime || exit /b 1

set "DIST=target\dist\kurotsuki"
if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%\lib"
xcopy /e /i /q target\runtime "%DIST%\runtime" >nul
copy /y %APP_JAR% "%DIST%\lib\" >nul
copy /y target\lib\*.jar "%DIST%\lib\" >nul
copy /y scripts\kurotsuki.bat "%DIST%\kurotsuki.bat" >nul

echo Build concluido: %DIST%\kurotsuki.bat
endlocal
