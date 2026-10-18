@echo off
set "DIR=%~dp0"
"%DIR%runtime\bin\javaw.exe" --enable-native-access=javafx.graphics -cp "%DIR%lib\*" pt.yaku.App %*
