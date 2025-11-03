# Building Standalone Binary

## Python 3.13 Compatibility Issue

PyInstaller 6.16.0 has known issues with Python 3.13's isolation subprocess mechanism.
The build will fail with: `SubprocessDiedError: Child process died calling discover_hook_directories()`

## Solutions:

### Option 1: Use Python 3.11 or 3.12 (Recommended)
```bash
# Install Python 3.12 dependencies
python3.12 -m pip install --user pyinstaller lxml PySide6

# Build with Python 3.12
python3.12 -m PyInstaller --name="SpaceHavenEditor" --onefile --windowed \
  --hidden-import=lxml --hidden-import=PySide6.QtCore \
  --hidden-import=PySide6.QtGui --hidden-import=PySide6.QtWidgets \
  main.py
```

### Option 2: Use Docker or Virtual Machine
Build in an environment with Python 3.11 or 3.12 installed.

### Option 3: Wait for PyInstaller Update
This issue may be fixed in future PyInstaller versions.

## Current Status
The build scripts are ready, but require Python 3.11 or 3.12 to successfully create binaries.
