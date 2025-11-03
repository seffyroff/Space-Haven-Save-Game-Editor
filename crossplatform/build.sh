#!/bin/bash
# Build script for Space Haven Save Editor standalone binary
# Uses Python 3.12 venv to avoid Python 3.13 compatibility issues with PyInstaller

set -e

echo "Creating Python 3.12 virtual environment..."
rm -rf build_venv
python3.12 -m venv build_venv

echo "Installing build dependencies..."
build_venv/bin/python -m pip install --upgrade pip -q
build_venv/bin/python -m pip install -q pyinstaller lxml PySide6

echo "Building standalone binary with PyInstaller..."
build_venv/bin/python -m PyInstaller --name="SpaceHavenEditor" \
    --onefile \
    --windowed \
    --hidden-import=lxml \
    --hidden-import=lxml.etree \
    --hidden-import=lxml._elementpath \
    --hidden-import=PySide6.QtCore \
    --hidden-import=PySide6.QtGui \
    --hidden-import=PySide6.QtWidgets \
    --noconfirm \
    main.py

echo ""
echo "Build complete! Binary location:"
ls -lh dist/SpaceHavenEditor
echo ""
echo "To test: ./dist/SpaceHavenEditor"

