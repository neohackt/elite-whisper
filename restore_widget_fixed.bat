@echo off
REM Script to properly restore working widget from GitHub

echo ========================================
echo Saving ALL current work
echo ========================================

cd /d D:\Personal\voiceapp

echo Staging all changes...
git add .

echo Committing all work...
git commit -m "WIP: All current work before restore"

echo Creating backup branch...
git branch dev/new-features-backup

echo.
echo ========================================
echo Force resetting to GitHub main
echo ========================================

echo Fetching from GitHub...
git fetch origin

echo Resetting to origin/main...
git reset --hard origin/main

echo.
echo ========================================
echo DONE! Code restored from GitHub.
echo ========================================
echo.
echo Your work is saved in: dev/new-features-backup
echo Widget should now work properly.
echo.
echo Run: npm run tauri dev
echo.
pause
