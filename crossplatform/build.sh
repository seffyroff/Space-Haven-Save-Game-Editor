#!/bin/bash
# Build script for Space Haven Save Editor standalone binary

set -e

# Create venv if it doesn't exist
if [ ! -d "venv" ]; then
    echo "Creating virtual environment..."
    python -m venv venv
fi

# Activate venv
echo "Activating virtual environment..."
source venv/bin/activate

echo "Installing build dependencies..."
pip install -q -r requirements-build.txt

echo "Building standalone binary with PyInstaller..."
# Note: If you encounter isolation errors with Python 3.13, try:
# export PYINSTALLER_DISABLE_ISOLATION=1
# or use Python 3.11/3.12 instead

pyinstaller --name="SpaceHavenEditor" \
    --onefile \
    --windowed \
    --add-data="README.md:." \
    --hidden-import=lxml \
    --hidden-import=lxml.etree \
    --hidden-import=lxml._elementpath \
    --hidden-import=PySide6.QtCore \
    --hidden-import=PySide6.QtGui \
    --hidden-import=PySide6.QtWidgets \
    --noconfirm \
    main.py

echo ""
echo "Build complete! Binary should be in: dist/SpaceHavenEditor"
echo ""
echo "To test: ./dist/SpaceHavenEditor"

