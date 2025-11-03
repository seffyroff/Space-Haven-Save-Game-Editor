# Space Haven Save Game Editor

A cross-platform desktop application for editing Space Haven save game files.

## Features

- **Global Save Settings** - Edit game-wide settings and preferences
- **Ship Management** - View and modify ship configurations
- **Crew Management** - Full editing capabilities for all crew members:
  - Attributes (Strength, Endurance, Intelligence, etc.)
  - Skills (all skill levels)
  - Traits (add/remove character traits)
  - Conditions (view and remove conditions)
  - Relationships (edit relationship values between crew members)
  - Create new crew members
- **Storage Management** - Edit storage containers and items
- **Ship Size Modification** - Change ship dimensions

## Platforms

- **Linux** (x86_64)
- **macOS** (Intel and Apple Silicon)
- **Windows** (x86_64)

## Quick Start

### Download Pre-built Binaries

Download the latest release from the [Releases](https://github.com/seffyroff/Space-Haven-Save-Game-Editor/releases) page. Binaries are available for all supported platforms.

**Linux:**
```bash
chmod +x SpaceHavenEditor
./SpaceHavenEditor
```

**macOS:**
```bash
chmod +x SpaceHavenEditor
./SpaceHavenEditor
```

**Windows:**
Double-click `SpaceHavenEditor.exe`

### Build from Source

#### Prerequisites

- Python 3.11 or 3.12 (PyInstaller has compatibility issues with Python 3.13)
- pip

#### Linux/macOS

```bash
cd crossplatform
./build.sh
```

The binary will be created in `crossplatform/dist/SpaceHavenEditor`

#### Windows

```bash
cd crossplatform
build.bat
```

The binary will be created in `crossplatform/dist/SpaceHavenEditor.exe`

#### Manual Build

```bash
cd crossplatform
python3.12 -m venv build_venv
build_venv/bin/python -m pip install pyinstaller lxml PySide6
build_venv/bin/python -m PyInstaller --name="SpaceHavenEditor" \
  --onefile --windowed \
  --hidden-import=lxml --hidden-import=PySide6.QtCore \
  --hidden-import=PySide6.QtGui --hidden-import=PySide6.QtWidgets \
  main.py
```

### Run from Source (Development)

```bash
cd crossplatform
pip install -r requirements.txt
python -m crossplatform.main
```

## Project Structure

```
/
├── crossplatform/          # Cross-platform Python/PySide6 implementation
│   ├── main.py            # Main application and UI
│   ├── models.py          # Data models
│   ├── save_loader.py     # Save file loading/saving
│   ├── id_collections.py  # ID mappings
│   └── build.sh           # Build script (Linux/macOS)
├── legacy-vb/             # Original VB.NET code (archived)
├── .github/workflows/     # GitHub Actions for automated builds
└── README.md              # This file
```

## Automated Builds

This project uses GitHub Actions to automatically build binaries for all platforms when:
- A tag is pushed (format: `v*`)
- Manual workflow dispatch
- Pull requests to main (for testing)

Binaries are automatically attached to releases when tags are pushed.

## Usage

1. **Open a Save File**: Use File → Open to load your Space Haven save game
2. **Backup**: The application automatically creates backups (`.bak` files)
3. **Edit Settings**: Navigate through the tabs to edit:
   - Global settings
   - Ship configurations
   - Crew attributes, skills, traits, conditions, and relationships
   - Storage containers and items
4. **Save**: Use File → Save to write changes to disk

## Technical Details

### Technology Stack

- **Python 3.11/3.12** - Programming language
- **PySide6** - Cross-platform GUI framework
- **lxml** - XML parsing and manipulation
- **PyInstaller** - Binary packaging

### Save File Format

Space Haven saves are XML files. The editor:
- Parses the XML structure
- Allows editing of game data
- Maintains XML structure and formatting
- Creates backups before modification

## Development

### Requirements

Development dependencies are listed in `crossplatform/requirements.txt`:
- PySide6==6.7.2
- lxml==5.2.1

### Code Structure

- `main.py` - Main window, UI components, and event handlers
- `models.py` - Data classes for save game structures
- `save_loader.py` - XML parsing, save loading, and data modification functions
- `id_collections.py` - Mappings between game IDs and human-readable names

## Legacy Code

The original VB.NET/WPF implementation has been moved to `legacy-vb/` for reference purposes. The new cross-platform implementation provides the same functionality and works on all major platforms.

## License

See [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

## Notes

- **Python 3.13 Compatibility**: PyInstaller 6.16.0 has known issues with Python 3.13's isolation mechanism. Use Python 3.11 or 3.12 for building binaries.
- **Backups**: Always back up your save files before editing. The application creates automatic backups, but it's good practice to keep your own backups as well.
