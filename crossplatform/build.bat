@echo off
REM Build script for Windows standalone binary

echo Installing build dependencies...
pip install -q -r requirements-build.txt

echo Building standalone binary with PyInstaller...
pyinstaller --name=SpaceHavenEditor ^
    --onefile ^
    --windowed ^
    --add-data="README.md;." ^
    --hidden-import=lxml ^
    --hidden-import=lxml.etree ^
    --hidden-import=lxml._elementpath ^
    --collect-all PySide6 ^
    --noconfirm ^
    main.py

echo.
echo Build complete! Binary should be in: dist\SpaceHavenEditor.exe
echo.

