Space Haven Save Editor (Cross-Platform)

Run on Linux/macOS/Windows using Python + PySide6.

Prerequisites
- Python 3.10+
- pip

Setup
1) Create venv (recommended)
   - python -m venv .venv
   - source .venv/bin/activate  # Windows: .venv\\Scripts\\activate
2) Install deps
   - pip install -r requirements.txt

Run
- python -m crossplatform.main

Usage
- File -> Open: select the Space Haven save file named "game" in your save folder.
- Update Global Settings: updates credits, sandbox, prestige in memory. Use File -> Save to persist.
- Select ship: shows owner and size, lets you set size in grid squares (1-8). Click Update Size, then Save.
- Storage tab: pick a container, edit quantities inline, add items, delete selected. Click Save to persist.

Notes
- Crew editing UI is minimal initially (names list). The XML mapping for crew, attributes, skills, traits, conditions, and relationships is implemented and ready to extend with editors.
- Backups are not automatic yet—make a manual copy of your save folder before editing.


