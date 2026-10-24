@echo off
set "DIR=%~dp0"
"%DIR%runtime\bin\javaw.exe" --enable-native-access=javafx.graphics,javafx.media,ALL-UNNAMED -cp "%DIR%lib\*" pt.yaku.App %*
