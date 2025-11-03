from __future__ import annotations
import sys
from pathlib import Path
from typing import Optional, List

from PySide6.QtCore import Qt
from PySide6.QtGui import QAction
from PySide6.QtWidgets import (
    QApplication,
    QMainWindow,
    QFileDialog,
    QWidget,
    QVBoxLayout,
    QHBoxLayout,
    QLabel,
    QLineEdit,
    QPushButton,
    QCheckBox,
    QComboBox,
    QTabWidget,
    QListWidget,
    QListWidgetItem,
    QSpinBox,
    QTableWidget,
    QTableWidgetItem,
    QMessageBox,
    QDialog,
    QDialogButtonBox,
    QTextEdit,
    QGroupBox,
)

from .models import SaveData, Ship, StorageContainer, Character, DataProp
from .save_loader import (
    load_save,
    save_to_disk,
    load_storage_containers,
    update_globals_in_memory,
    add_item_to_container,
    delete_item_from_container,
    update_item_quantity,
    update_ship_size,
    update_character_attribute,
    update_character_skill,
    add_character_trait,
    remove_character_trait,
    remove_character_condition,
    update_character_relationship,
    create_new_crew_member,
)
from .id_collections import DefaultStorageIDs, DefaultAttributeIDs, DefaultSkillIDs, DefaultTraitIDs


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("Space Haven Save Editor (Cross-Platform)")
        self.resize(1200, 900)

        self.save: Optional[SaveData] = None
        self.current_containers: list[StorageContainer] = []
        self.backup_enabled: bool = False

        self._init_menu()

        central = QWidget()
        self.setCentralWidget(central)
        root = QVBoxLayout(central)

        # Global settings card
        globals_box = QHBoxLayout()
        self.txt_credits = QLineEdit()
        self.txt_prestige = QLineEdit()
        self.chk_sandbox = QCheckBox("Sandbox Mode")
        btn_update_globals = QPushButton("Update Global Settings")
        btn_update_globals.clicked.connect(self._on_update_globals)
        globals_box.addWidget(QLabel("Player Credits:"))
        globals_box.addWidget(self.txt_credits)
        globals_box.addWidget(QLabel("Prestige Points:"))
        globals_box.addWidget(self.txt_prestige)
        globals_box.addWidget(self.chk_sandbox)
        globals_box.addWidget(btn_update_globals)
        root.addLayout(globals_box)

        # Ship selection card
        ship_box = QHBoxLayout()
        self.cmb_ships = QComboBox()
        self.cmb_ships.currentIndexChanged.connect(self._on_ship_changed)
        self.lbl_owner = QLabel("Owner:")
        self.lbl_ship_size = QLabel("Size:")
        self.lbl_canvas_size = QLabel("Canvas Size:")
        self.spin_w = QSpinBox()
        self.spin_h = QSpinBox()
        self.spin_w.setRange(1, 8)
        self.spin_h.setRange(1, 8)
        btn_update_size = QPushButton("Update Size")
        btn_update_size.clicked.connect(self._on_update_size)
        ship_box.addWidget(QLabel("Selected Ship:"))
        ship_box.addWidget(self.cmb_ships)
        ship_box.addWidget(self.lbl_owner)
        ship_box.addWidget(self.lbl_ship_size)
        ship_box.addWidget(self.lbl_canvas_size)
        ship_box.addWidget(QLabel("W (squares):"))
        ship_box.addWidget(self.spin_w)
        ship_box.addWidget(QLabel("H (squares):"))
        ship_box.addWidget(self.spin_h)
        ship_box.addWidget(btn_update_size)
        root.addLayout(ship_box)

        # Tabs
        self.tabs = QTabWidget()
        root.addWidget(self.tabs)

        # Crew tab with full editing
        crew_tab = QWidget()
        crew_layout = QHBoxLayout(crew_tab)
        
        # Left: Crew list
        left_panel = QWidget()
        left_layout = QVBoxLayout(left_panel)
        left_layout.addWidget(QLabel("Crew Members"))
        self.lbl_crew_count = QLabel("Total Crew: 0")
        left_layout.addWidget(self.lbl_crew_count)
        self.lst_crew = QListWidget()
        self.lst_crew.currentRowChanged.connect(self._on_crew_selected)
        left_layout.addWidget(self.lst_crew)
        btn_add_crew = QPushButton("Create New Crew Member...")
        btn_add_crew.clicked.connect(self._on_add_new_crew)
        left_layout.addWidget(btn_add_crew)
        left_layout.setStretch(2, 1)
        crew_layout.addWidget(left_panel)
        
        # Right: Tabs for editing
        self.crew_tabs = QTabWidget()
        
        # Attributes tab
        attrs_widget = QWidget()
        attrs_layout = QVBoxLayout(attrs_widget)
        btn_set_all_attrs = QPushButton("Set All Attributes to 5")
        btn_set_all_attrs.clicked.connect(self._on_set_all_attributes)
        attrs_layout.addWidget(btn_set_all_attrs)
        self.tbl_attributes = QTableWidget(0, 3)
        self.tbl_attributes.setHorizontalHeaderLabels(["Attr ID", "Attr Name", "Value"])
        self.tbl_attributes.itemChanged.connect(self._on_attribute_changed)
        attrs_layout.addWidget(self.tbl_attributes)
        self.crew_tabs.addTab(attrs_widget, "Attributes")
        
        # Skills tab
        skills_widget = QWidget()
        skills_layout = QVBoxLayout(skills_widget)
        btn_set_all_skills = QPushButton("Set All Skills to 8")
        btn_set_all_skills.clicked.connect(self._on_set_all_skills)
        skills_layout.addWidget(btn_set_all_skills)
        self.tbl_skills = QTableWidget(0, 3)
        self.tbl_skills.setHorizontalHeaderLabels(["Skill ID", "Skill Name", "Level"])
        self.tbl_skills.itemChanged.connect(self._on_skill_changed)
        skills_layout.addWidget(self.tbl_skills)
        self.crew_tabs.addTab(skills_widget, "Skills")
        
        # Traits tab
        traits_widget = QWidget()
        traits_layout = QVBoxLayout(traits_widget)
        traits_layout.addWidget(QLabel("Current Traits:"))
        self.tbl_traits = QTableWidget(0, 1)
        self.tbl_traits.setHorizontalHeaderLabels(["Trait Name"])
        self.tbl_traits.setEditTriggers(QTableWidget.NoEditTriggers)
        traits_layout.addWidget(self.tbl_traits)
        manage_layout = QHBoxLayout()
        manage_layout.addWidget(QLabel("Add Trait:"))
        self.cmb_add_trait = QComboBox()
        for tid, name in sorted(DefaultTraitIDs.items(), key=lambda x: x[1]):
            self.cmb_add_trait.addItem(name, tid)
        manage_layout.addWidget(self.cmb_add_trait)
        btn_add_trait = QPushButton("Add Trait")
        btn_add_trait.clicked.connect(self._on_add_trait)
        btn_del_trait = QPushButton("Delete Trait")
        btn_del_trait.clicked.connect(self._on_delete_trait)
        manage_layout.addWidget(btn_add_trait)
        manage_layout.addWidget(btn_del_trait)
        traits_layout.addLayout(manage_layout)
        self.crew_tabs.addTab(traits_widget, "Traits")
        
        # Conditions tab
        conditions_widget = QWidget()
        conditions_layout = QVBoxLayout(conditions_widget)
        conditions_layout.addWidget(QLabel("Current Conditions:"))
        self.lst_conditions = QListWidget()
        conditions_layout.addWidget(self.lst_conditions)
        btn_del_condition = QPushButton("Delete Selected Condition")
        btn_del_condition.clicked.connect(self._on_delete_condition)
        conditions_layout.addWidget(btn_del_condition)
        self.crew_tabs.addTab(conditions_widget, "Conditions")
        
        # Relationships tab
        rels_widget = QWidget()
        rels_layout = QVBoxLayout(rels_widget)
        rels_layout.addWidget(QLabel("Current Relationships:"))
        self.tbl_relationships = QTableWidget(0, 4)
        self.tbl_relationships.setHorizontalHeaderLabels(["Target", "Friendship", "Attraction", "Compatibility"])
        self.tbl_relationships.itemChanged.connect(self._on_relationship_changed)
        rels_layout.addWidget(self.tbl_relationships)
        paging_layout = QHBoxLayout()
        self.btn_rel_prev = QPushButton("< Previous")
        self.btn_rel_prev.clicked.connect(self._on_rel_prev)
        self.btn_rel_next = QPushButton("Next >")
        self.btn_rel_next.clicked.connect(self._on_rel_next)
        self.lbl_rel_page = QLabel("Page 0 of 0")
        paging_layout.addWidget(self.btn_rel_prev)
        paging_layout.addWidget(self.lbl_rel_page)
        paging_layout.addWidget(self.btn_rel_next)
        paging_layout.addStretch()
        rels_layout.addLayout(paging_layout)
        self.crew_tabs.addTab(rels_widget, "Relationships")
        
        crew_layout.addWidget(self.crew_tabs)
        crew_layout.setStretch(1, 1)
        
        self.current_character: Optional[Character] = None
        self.relationships_page = 1
        self.relationships_page_size = 20
        
        self.tabs.addTab(crew_tab, "Crew")

        # Storage tab
        storage_tab = QWidget()
        storage_layout = QVBoxLayout(storage_tab)
        top_row = QHBoxLayout()
        top_row.addWidget(QLabel("Select Container:"))
        self.cmb_containers = QComboBox()
        self.cmb_containers.currentIndexChanged.connect(self._on_container_changed)
        top_row.addWidget(self.cmb_containers)
        self.lbl_total_items = QLabel("(No Items)")
        top_row.addWidget(self.lbl_total_items)
        storage_layout.addLayout(top_row)

        self.tbl_storage = QTableWidget(0, 3)
        self.tbl_storage.setHorizontalHeaderLabels(["Item Name", "Quantity", "Item ID"])
        self.tbl_storage.itemChanged.connect(self._on_storage_item_changed)
        storage_layout.addWidget(self.tbl_storage)

        add_row = QHBoxLayout()
        add_row.addWidget(QLabel("Add Item:"))
        self.cmb_add_item = QComboBox()
        # Populate once (static ids)
        for item_id, name in sorted(DefaultStorageIDs.items(), key=lambda x: x[1]):
            self.cmb_add_item.addItem(name, item_id)
        add_row.addWidget(self.cmb_add_item)
        add_row.addWidget(QLabel("Quantity:"))
        self.txt_add_qty = QLineEdit("1")
        add_row.addWidget(self.txt_add_qty)
        btn_add = QPushButton("Add to Container")
        btn_add.clicked.connect(self._on_add_item)
        btn_del = QPushButton("Delete Selected")
        btn_del.clicked.connect(self._on_delete_selected)
        add_row.addWidget(btn_add)
        add_row.addWidget(btn_del)
        storage_layout.addLayout(add_row)

        self.tabs.addTab(storage_tab, "Storage")

    def _init_menu(self) -> None:
        m = self.menuBar()
        file_menu = m.addMenu("File")
        act_open = QAction("Open", self)
        act_open.triggered.connect(self._on_open)
        act_save = QAction("Save", self)
        act_save.triggered.connect(self._on_save)
        act_exit = QAction("Exit", self)
        act_exit.triggered.connect(self.close)
        file_menu.addAction(act_open)
        file_menu.addAction(act_save)
        file_menu.addSeparator()
        file_menu.addAction(act_exit)
        
        edit_menu = m.addMenu("Edit")
        act_settings = QAction("Settings", self)
        act_settings.triggered.connect(self._on_settings)
        edit_menu.addAction(act_settings)
        
        help_menu = m.addMenu("Help")
        act_help = QAction("Help / Instructions", self)
        act_help.triggered.connect(self._on_help)
        act_about = QAction("About", self)
        act_about.triggered.connect(self._on_about)
        help_menu.addAction(act_help)
        help_menu.addSeparator()
        help_menu.addAction(act_about)

    def _on_open(self) -> None:
        dlg = QFileDialog(self)
        dlg.setNameFilters(["Space Haven Save (game *sav)", "All Files (*)"])
        dlg.setFileMode(QFileDialog.ExistingFile)
        if dlg.exec() != QFileDialog.Accepted:
            return
        path = dlg.selectedFiles()[0]
        try:
            self.save = load_save(path)
        except Exception as ex:
            QMessageBox.critical(self, "Load Error", str(ex))
            return
        self._populate_after_load()

    def _populate_after_load(self) -> None:
        assert self.save is not None
        # Globals
        self.txt_credits.setText(str(self.save.credits))
        self.txt_prestige.setText(str(self.save.prestige_points))
        self.chk_sandbox.setChecked(self.save.sandbox)
        # Ships
        self.cmb_ships.blockSignals(True)
        self.cmb_ships.clear()
        for s in sorted(self.save.ships, key=lambda s: s.sname):
            self.cmb_ships.addItem(s.sname, s.sid)
        self.cmb_ships.blockSignals(False)
        if self.cmb_ships.count() > 0:
            self.cmb_ships.setCurrentIndex(0)
            self._on_ship_changed(0)

    def _on_save(self) -> None:
        if not self.save:
            QMessageBox.warning(self, "Save", "No file loaded.")
            return
        try:
            save_to_disk(self.save)
            QMessageBox.information(self, "Save", "File saved successfully.")
        except Exception as ex:
            QMessageBox.critical(self, "Save Error", str(ex))

    def _on_update_globals(self) -> None:
        if not self.save:
            QMessageBox.warning(self, "Globals", "No file loaded.")
            return
        credits = None
        prestige = None
        try:
            credits = int(float(self.txt_credits.text()))
        except Exception:
            pass
        try:
            prestige = int(self.txt_prestige.text())
        except Exception:
            pass
        update_globals_in_memory(self.save, credits, self.chk_sandbox.isChecked(), prestige)
        QMessageBox.information(self, "Globals", "Global settings updated in memory. Use File -> Save to persist.")

    def _current_ship(self) -> Optional[Ship]:
        if not self.save or self.cmb_ships.currentIndex() < 0:
            return None
        sid = self.cmb_ships.currentData()
        return next((s for s in self.save.ships if s.sid == sid), None)

    def _on_ship_changed(self, idx: int) -> None:
        ship = self._current_ship()
        if not ship:
            self.lbl_owner.setText("Owner:")
            self.lbl_ship_size.setText("Size:")
            self.lbl_canvas_size.setText("Canvas Size:")
            self.lst_crew.clear()
            self.cmb_containers.clear()
            self.tbl_storage.setRowCount(0)
            return
        # Owner
        # Minimal owner view: pull <settings owner="..."> if present
        owner = "Unknown"
        try:
            root = self.save.xml_doc.getroot() if self.save and self.save.xml_doc is not None else None
            if root is not None:
                ship_el = root.find(f".//ship[@sid='{ship.sid}']")
                if ship_el is not None:
                    settings = ship_el.find("settings")
                    if settings is not None and settings.get("owner"):
                        owner = settings.get("owner")
        except Exception:
            pass
        self.lbl_owner.setText(f"Owner: {owner}")
        self.lbl_ship_size.setText(f"Size: {ship.sx}x{ship.sy}")
        self.lbl_canvas_size.setText(f"Canvas Size: {ship.sx // 28} W x {ship.sy // 28} H squares")
        self.spin_w.setValue(max(1, ship.sx // 28))
        self.spin_h.setValue(max(1, ship.sy // 28))

        # Crew list for ship
        self.lst_crew.blockSignals(True)
        self.lst_crew.clear()
        if self.save:
            ship_crew = [c for c in self.save.characters if c.ship_sid == ship.sid]
            self.lst_crew.addItems([c.name for c in sorted(ship_crew, key=lambda c: c.name)])
            self.lbl_crew_count.setText(f"Total Crew: {len(ship_crew)}")
            # Store character references
            for i, char in enumerate(sorted(ship_crew, key=lambda c: c.name)):
                item = self.lst_crew.item(i)
                if item:
                    item.setData(Qt.UserRole, char.entity_id)
        else:
            self.lbl_crew_count.setText("Total Crew: 0")
        self.lst_crew.blockSignals(False)
        self.current_character = None
        self._clear_crew_editors()

        # Storage containers
        self.current_containers = load_storage_containers(self.save, ship.sid)
        self.cmb_containers.blockSignals(True)
        self.cmb_containers.clear()
        for c in self.current_containers:
            self.cmb_containers.addItem(c.display_name)
        self.cmb_containers.blockSignals(False)
        if self.cmb_containers.count() > 0:
            self.cmb_containers.setCurrentIndex(0)
            self._on_container_changed(0)
        else:
            self.tbl_storage.setRowCount(0)
            self.lbl_total_items.setText("(No Items)")

    def _current_container(self) -> Optional[StorageContainer]:
        if not self.current_containers or self.cmb_containers.currentIndex() < 0:
            return None
        return self.current_containers[self.cmb_containers.currentIndex()]

    def _on_container_changed(self, idx: int) -> None:
        cont = self._current_container()
        self.tbl_storage.blockSignals(True)
        self.tbl_storage.setRowCount(0)
        total = 0
        if cont:
            rows = len(cont.items)
            self.tbl_storage.setRowCount(rows)
            for r, it in enumerate(sorted(cont.items, key=lambda i: DefaultStorageIDs.get(i.element_id, str(i.element_id)))):
                name = DefaultStorageIDs.get(it.element_id, f"Unknown Item ({it.element_id})")
                self.tbl_storage.setItem(r, 0, QTableWidgetItem(name))
                qty_item = QTableWidgetItem(str(it.quantity))
                qty_item.setData(Qt.UserRole, it.element_id)
                self.tbl_storage.setItem(r, 1, qty_item)
                self.tbl_storage.setItem(r, 2, QTableWidgetItem(str(it.element_id)))
                total += it.quantity
        self.tbl_storage.blockSignals(False)
        self.lbl_total_items.setText(f"Total Items: {total}" if total > 0 else "(No Items)")

    def _on_add_item(self) -> None:
        if not self.save:
            QMessageBox.warning(self, "Storage", "No file loaded.")
            return
        cont = self._current_container()
        if not cont:
            QMessageBox.warning(self, "Storage", "Select a container first.")
            return
        item_id = self.cmb_add_item.currentData()
        try:
            qty = int(self.txt_add_qty.text())
        except Exception:
            QMessageBox.warning(self, "Storage", "Enter a valid positive quantity.")
            return
        if qty <= 0:
            QMessageBox.warning(self, "Storage", "Quantity must be positive.")
            return
        add_item_to_container(self.save, cont, item_id, qty)
        self._on_container_changed(self.cmb_containers.currentIndex())
        QMessageBox.information(self, "Storage", "Item added/updated in memory. Use File -> Save to persist.")

    def _on_delete_selected(self) -> None:
        if not self.save:
            QMessageBox.warning(self, "Storage", "No file loaded.")
            return
        cont = self._current_container()
        if not cont:
            QMessageBox.warning(self, "Storage", "Select a container first.")
            return
        row = self.tbl_storage.currentRow()
        if row < 0:
            QMessageBox.warning(self, "Storage", "Select a row to delete.")
            return
        try:
            item_id = int(self.tbl_storage.item(row, 2).text())
        except Exception:
            return
        delete_item_from_container(self.save, cont, item_id)
        self._on_container_changed(self.cmb_containers.currentIndex())

    def _on_storage_item_changed(self, item: QTableWidgetItem) -> None:
        if item.column() != 1:
            return
        cont = self._current_container()
        if not self.save or not cont:
            return
        try:
            item_id = int(self.tbl_storage.item(item.row(), 2).text())
            qty = int(item.text())
        except Exception:
            return
        update_item_quantity(self.save, cont, item_id, qty)
        self._on_container_changed(self.cmb_containers.currentIndex())

    def _on_update_size(self) -> None:
        ship = self._current_ship()
        if not self.save or not ship:
            return
        w = self.spin_w.value()
        h = self.spin_h.value()
        update_ship_size(self.save, ship, w, h)
        # Refresh labels
        self._on_ship_changed(self.cmb_ships.currentIndex())

    def _current_crew_member(self) -> Optional[Character]:
        """Get the currently selected crew member."""
        if not self.save or self.lst_crew.currentRow() < 0:
            return None
        item = self.lst_crew.currentItem()
        if item is None:
            return None
        ent_id = item.data(Qt.UserRole)
        return next((c for c in self.save.characters if c.entity_id == ent_id), None)

    def _on_crew_selected(self, row: int) -> None:
        """Handle crew member selection."""
        self.current_character = self._current_crew_member()
        if not self.current_character:
            self._clear_crew_editors()
            return
        
        # Attributes
        self.tbl_attributes.blockSignals(True)
        self.tbl_attributes.setRowCount(0)
        # Ensure all attributes exist (fill with defaults if missing)
        attrs_to_show = []
        for aid, name in sorted(DefaultAttributeIDs.items()):
            attr = next((a for a in self.current_character.attributes if a.id == aid), None)
            if attr:
                attrs_to_show.append(attr)
            else:
                attrs_to_show.append(DataProp(id=aid, name=name, value=0))
        self.tbl_attributes.setRowCount(len(attrs_to_show))
        for r, attr in enumerate(attrs_to_show):
            self.tbl_attributes.setItem(r, 0, QTableWidgetItem(str(attr.id)))
            self.tbl_attributes.setItem(r, 1, QTableWidgetItem(attr.name))
            val_item = QTableWidgetItem(str(attr.value))
            val_item.setData(Qt.UserRole, attr.id)
            self.tbl_attributes.setItem(r, 2, val_item)
        self.tbl_attributes.blockSignals(False)
        
        # Skills
        self.tbl_skills.blockSignals(True)
        self.tbl_skills.setRowCount(0)
        skills_to_show = []
        for sid, name in sorted(DefaultSkillIDs.items()):
            skill = next((s for s in self.current_character.skills if s.id == sid), None)
            if skill:
                skills_to_show.append(skill)
            else:
                skills_to_show.append(DataProp(id=sid, name=name, value=0))
        self.tbl_skills.setRowCount(len(skills_to_show))
        for r, skill in enumerate(skills_to_show):
            self.tbl_skills.setItem(r, 0, QTableWidgetItem(str(skill.id)))
            self.tbl_skills.setItem(r, 1, QTableWidgetItem(skill.name))
            lvl_item = QTableWidgetItem(str(skill.value))
            lvl_item.setData(Qt.UserRole, skill.id)
            self.tbl_skills.setItem(r, 2, lvl_item)
        self.tbl_skills.blockSignals(False)
        
        # Traits
        self.tbl_traits.blockSignals(True)
        self.tbl_traits.setRowCount(len(self.current_character.traits))
        for r, trait in enumerate(self.current_character.traits):
            self.tbl_traits.setItem(r, 0, QTableWidgetItem(trait.name))
            self.tbl_traits.item(r, 0).setData(Qt.UserRole, trait.id)
        self.tbl_traits.blockSignals(False)
        
        # Conditions
        self.lst_conditions.clear()
        for cond in self.current_character.conditions:
            item = QListWidgetItem(cond.name)
            item.setData(Qt.UserRole, cond.id)
            self.lst_conditions.addItem(item)
        
        # Relationships - load first page
        self.relationships_page = 1
        self._load_relationships_page()

    def _clear_crew_editors(self) -> None:
        """Clear all crew editing widgets."""
        self.tbl_attributes.setRowCount(0)
        self.tbl_skills.setRowCount(0)
        self.tbl_traits.setRowCount(0)
        self.lst_conditions.clear()
        self.tbl_relationships.setRowCount(0)
        self.lbl_rel_page.setText("Page 0 of 0")
        self.btn_rel_prev.setEnabled(False)
        self.btn_rel_next.setEnabled(False)

    def _load_relationships_page(self) -> None:
        """Load the current page of relationships."""
        if not self.current_character:
            return
        rels = sorted(self.current_character.relationships, key=lambda r: r.target_name)
        total = len(rels)
        total_pages = max(1, (total + self.relationships_page_size - 1) // self.relationships_page_size)
        self.relationships_page = max(1, min(self.relationships_page, total_pages))
        
        start = (self.relationships_page - 1) * self.relationships_page_size
        end = start + self.relationships_page_size
        page_rels = rels[start:end]
        
        self.tbl_relationships.blockSignals(True)
        self.tbl_relationships.setRowCount(len(page_rels))
        for r, rel in enumerate(page_rels):
            self.tbl_relationships.setItem(r, 0, QTableWidgetItem(rel.target_name))
            self.tbl_relationships.item(r, 0).setData(Qt.UserRole, rel.target_id)
            self.tbl_relationships.item(r, 0).setFlags(Qt.ItemIsEnabled)  # Read-only
            f_item = QTableWidgetItem(str(rel.friendship))
            f_item.setData(Qt.UserRole, rel.target_id)
            self.tbl_relationships.setItem(r, 1, f_item)
            a_item = QTableWidgetItem(str(rel.attraction))
            a_item.setData(Qt.UserRole, rel.target_id)
            self.tbl_relationships.setItem(r, 2, a_item)
            c_item = QTableWidgetItem(str(rel.compatibility))
            c_item.setData(Qt.UserRole, rel.target_id)
            self.tbl_relationships.setItem(r, 3, c_item)
        self.tbl_relationships.blockSignals(False)
        
        self.lbl_rel_page.setText(f"Page {self.relationships_page} of {total_pages}")
        self.btn_rel_prev.setEnabled(self.relationships_page > 1)
        self.btn_rel_next.setEnabled(self.relationships_page < total_pages)

    def _on_attribute_changed(self, item: QTableWidgetItem) -> None:
        """Handle attribute value change."""
        if not self.save or not self.current_character or item.column() != 2:
            return
        try:
            attr_id = item.data(Qt.UserRole)
            value = int(item.text())
        except Exception:
            return
        update_character_attribute(self.save, self.current_character, attr_id, value)

    def _on_skill_changed(self, item: QTableWidgetItem) -> None:
        """Handle skill level change."""
        if not self.save or not self.current_character or item.column() != 2:
            return
        try:
            skill_id = item.data(Qt.UserRole)
            level = int(item.text())
        except Exception:
            return
        update_character_skill(self.save, self.current_character, skill_id, level)

    def _on_add_trait(self) -> None:
        """Add a trait to the current character."""
        if not self.save or not self.current_character:
            QMessageBox.warning(self, "Traits", "Select a crew member first.")
            return
        trait_id = self.cmb_add_trait.currentData()
        if trait_id is None:
            return
        add_character_trait(self.save, self.current_character, trait_id)
        # Refresh traits table
        self.tbl_traits.blockSignals(True)
        self.tbl_traits.setRowCount(len(self.current_character.traits))
        for r, trait in enumerate(self.current_character.traits):
            self.tbl_traits.setItem(r, 0, QTableWidgetItem(trait.name))
            self.tbl_traits.item(r, 0).setData(Qt.UserRole, trait.id)
        self.tbl_traits.blockSignals(False)

    def _on_delete_trait(self) -> None:
        """Delete the selected trait."""
        if not self.save or not self.current_character:
            QMessageBox.warning(self, "Traits", "Select a crew member first.")
            return
        row = self.tbl_traits.currentRow()
        if row < 0:
            QMessageBox.warning(self, "Traits", "Select a trait to delete.")
            return
        item = self.tbl_traits.item(row, 0)
        if item is None:
            return
        trait_id = item.data(Qt.UserRole)
        remove_character_trait(self.save, self.current_character, trait_id)
        # Refresh
        self.tbl_traits.blockSignals(True)
        self.tbl_traits.setRowCount(len(self.current_character.traits))
        for r, trait in enumerate(self.current_character.traits):
            self.tbl_traits.setItem(r, 0, QTableWidgetItem(trait.name))
            self.tbl_traits.item(r, 0).setData(Qt.UserRole, trait.id)
        self.tbl_traits.blockSignals(False)

    def _on_delete_condition(self) -> None:
        """Delete the selected condition."""
        if not self.save or not self.current_character:
            QMessageBox.warning(self, "Conditions", "Select a crew member first.")
            return
        item = self.lst_conditions.currentItem()
        if item is None:
            QMessageBox.warning(self, "Conditions", "Select a condition to delete.")
            return
        cond_id = item.data(Qt.UserRole)
        remove_character_condition(self.save, self.current_character, cond_id)
        # Refresh
        self.lst_conditions.takeItem(self.lst_conditions.currentRow())

    def _on_relationship_changed(self, item: QTableWidgetItem) -> None:
        """Handle relationship value change."""
        if not self.save or not self.current_character or item.column() == 0:
            return
        try:
            target_id = item.data(Qt.UserRole)
            col = item.column()
            if col == 1:  # Friendship
                friendship = int(item.text())
                update_character_relationship(self.save, self.current_character, target_id, friendship=friendship)
            elif col == 2:  # Attraction
                attraction = int(item.text())
                update_character_relationship(self.save, self.current_character, target_id, attraction=attraction)
            elif col == 3:  # Compatibility
                compatibility = int(item.text())
                update_character_relationship(self.save, self.current_character, target_id, compatibility=compatibility)
        except Exception:
            pass

    def _on_rel_prev(self) -> None:
        """Go to previous relationships page."""
        if self.relationships_page > 1:
            self.relationships_page -= 1
            self._load_relationships_page()

    def _on_rel_next(self) -> None:
        """Go to next relationships page."""
        if not self.current_character:
            return
        total = len(self.current_character.relationships)
        total_pages = max(1, (total + self.relationships_page_size - 1) // self.relationships_page_size)
        if self.relationships_page < total_pages:
            self.relationships_page += 1
            self._load_relationships_page()

    def _on_set_all_attributes(self) -> None:
        """Set all attributes to 5 for the current character."""
        if not self.save or not self.current_character:
            QMessageBox.warning(self, "Attributes", "Select a crew member first.")
            return
        # Set all known attributes to 5
        for attr_id in DefaultAttributeIDs.keys():
            update_character_attribute(self.save, self.current_character, attr_id, 5)
        # Refresh table
        self.tbl_attributes.blockSignals(True)
        for r in range(self.tbl_attributes.rowCount()):
            item = self.tbl_attributes.item(r, 2)
            if item:
                item.setText("5")
        self.tbl_attributes.blockSignals(False)

    def _on_set_all_skills(self) -> None:
        """Set all skills to 8 for the current character."""
        if not self.save or not self.current_character:
            QMessageBox.warning(self, "Skills", "Select a crew member first.")
            return
        # Set all known skills to 8
        for skill_id in DefaultSkillIDs.keys():
            update_character_skill(self.save, self.current_character, skill_id, 8)
        # Refresh table
        self.tbl_skills.blockSignals(True)
        for r in range(self.tbl_skills.rowCount()):
            item = self.tbl_skills.item(r, 2)
            if item:
                item.setText("8")
        self.tbl_skills.blockSignals(False)

    def _on_add_new_crew(self) -> None:
        """Open dialog to create a new crew member."""
        if not self.save:
            QMessageBox.warning(self, "Crew", "Load a save file first.")
            return
        ship = self._current_ship()
        if not ship:
            QMessageBox.warning(self, "Crew", "Select a ship first.")
            return
        
        dlg = NewCrewDialog(self)
        if dlg.exec() == QDialog.Accepted:
            try:
                create_new_crew_member(
                    self.save, ship.sid, dlg.name,
                    dlg.attributes, dlg.skills, dlg.traits
                )
                # Refresh crew list
                self._on_ship_changed(self.cmb_ships.currentIndex())
                QMessageBox.information(self, "Crew", f"Crew member '{dlg.name}' added (in memory). Use File -> Save to persist.")
            except Exception as ex:
                QMessageBox.critical(self, "Error", f"Error creating crew: {ex}")

    def _on_settings(self) -> None:
        """Open settings dialog."""
        dlg = SettingsDialog(self)
        dlg.set_backup(self.backup_enabled)
        if dlg.exec() == QDialog.Accepted:
            self.backup_enabled = dlg.backup_enabled()
            QMessageBox.information(self, "Settings", f"Backup on open setting: {'Enabled' if self.backup_enabled else 'Disabled'}. Change takes effect next time you open a file.")

    def _on_help(self) -> None:
        """Open help window."""
        help_text = self._generate_help_text()
        dlg = HelpDialog(help_text, self)
        dlg.exec()

    def _on_about(self) -> None:
        """Open about dialog."""
        dlg = AboutDialog(self)
        dlg.exec()

    def _generate_help_text(self) -> str:
        """Generate help text content."""
        lines = [
            "=== Moragar's Space Haven Save Editor - Help & Instructions ===",
            "",
            "*** DISCLAIMER ***",
            "Use this tool at your own risk. Editing save files can lead to unexpected",
            "issues or corrupted saves. The creator is not responsible for any damage",
            "to your save games, even if the backup feature is enabled.",
            "Always keep manual backups of important saves!",
            "*** DISCLAIMER ***",
            "",
            "--- Getting Started ---",
            "- File -> Open: Use this to load your Space Haven save game.",
            "- Navigate to your save game folder. The typical path is:",
            "  Steam\\steamapps\\common\\SpaceHaven\\savegames\\[YourSaveGameName]\\save\\",
            "- Select the file named 'game' (it usually has no file extension).",
            "- Backups: If enabled in Settings, a timestamped backup will be created",
            "  automatically when opening saves.",
            "- File -> Save: IMPORTANT! Click this after making edits to permanently write",
            "  your changes back to the 'game' file.",
            "",
            "--- Editing Your Save ---",
            "- Global Settings (Top Section):",
            "  - Player Credits: Enter the desired amount of credits.",
            "  - Sandbox Mode: Check or uncheck to enable/disable sandbox mode.",
            "  - Player Prestige Points: Enter the desired amount of Exodus Fleet Prestige Points",
            "  - NOTE: Changes here are applied to memory when you click 'Update Global Settings'.",
            "",
            "- Ship Selection (Middle Section):",
            "  - Use the dropdown to select the specific ship you want to view or edit.",
            "  - Basic info like Owner and Size is shown below the dropdown.",
            "  - Update Size Button: Allows changing the selected ship's dimensions.",
            "  - Max recommended Canvas Size is 8W x 8H squares.",
            "",
            "--- Crew Tab Details ---",
            "- Crew List (Left): Select a crew member. The list shows names; total count is above.",
            "- Create New Crew Member: Button below the list opens a window to add a new character.",
            "- Editing Tabs (Right - Attributes, Skills, Traits, Conditions, Relationships):",
            "  - Attributes/Skills: Double-click a cell in the 'Value' or 'Level' column to edit.",
            "  - Use the 'Set All...' buttons for quick presets.",
            "  - Traits: Select a trait from the dropdown, click 'Add Trait'. To remove, select a trait",
            "      in the grid above, then click 'Delete Trait'.",
            "  - Conditions: Shows current status effects/injuries. Select one and click",
            "      'Delete Selected Condition' to remove it.",
            "  - Relationships: Shows how the selected character feels about others. Edit the",
            "      Friendship, Attraction, or Compatibility values by editing the cells.",
            "",
            "--- Storage Tab Details ---",
            "- Container Selection: Choose a specific storage container from the dropdown.",
            "- Item Grid: Shows items in the selected container.",
            "- Edit Quantity: Double-click a cell in the 'Quantity' column, type the new amount.",
            "- Add Item: Below the grid, select an item, enter quantity and click 'Add to Container'.",
            "- Delete Item Stack: Select a row in the grid and click 'Delete Selected'.",
            "",
            "--- Final Reminder ---",
            "- Don't forget File -> Save! Most edits only change the data in memory until saved.",
            "- Keep backups of your original save files just in case!",
        ]
        return "\n".join(lines)


class NewCrewDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Create New Crew Member")
        self.resize(800, 600)
        
        layout = QVBoxLayout(self)
        
        # Name
        name_layout = QHBoxLayout()
        name_layout.addWidget(QLabel("Name:"))
        self.txt_name = QLineEdit("New Recruit")
        name_layout.addWidget(self.txt_name)
        layout.addLayout(name_layout)
        
        # Tabs for Attributes, Skills, Traits
        tabs = QTabWidget()
        
        # Attributes tab
        attrs_widget = QWidget()
        attrs_layout = QVBoxLayout(attrs_widget)
        btn_set_attrs = QPushButton("Set All Attributes to 5")
        btn_set_attrs.clicked.connect(lambda: self._set_all_attrs(5))
        attrs_layout.addWidget(btn_set_attrs)
        self.tbl_attrs = QTableWidget(0, 3)
        self.tbl_attrs.setHorizontalHeaderLabels(["Attr ID", "Attr Name", "Value"])
        attrs_layout.addWidget(self.tbl_attrs)
        tabs.addTab(attrs_widget, "Attributes")
        
        # Skills tab
        skills_widget = QWidget()
        skills_layout = QVBoxLayout(skills_widget)
        btn_set_skills = QPushButton("Set All Skills to 8")
        btn_set_skills.clicked.connect(lambda: self._set_all_skills(8))
        skills_layout.addWidget(btn_set_skills)
        self.tbl_skills = QTableWidget(0, 3)
        self.tbl_skills.setHorizontalHeaderLabels(["Skill ID", "Skill Name", "Level"])
        skills_layout.addWidget(self.tbl_skills)
        tabs.addTab(skills_widget, "Skills")
        
        # Traits tab
        traits_widget = QWidget()
        traits_layout = QVBoxLayout(traits_widget)
        traits_layout.addWidget(QLabel("Current Traits:"))
        self.lst_traits = QListWidget()
        traits_layout.addWidget(self.lst_traits)
        trait_manage = QHBoxLayout()
        trait_manage.addWidget(QLabel("Add Trait:"))
        self.cmb_trait = QComboBox()
        for tid, name in sorted(DefaultTraitIDs.items(), key=lambda x: x[1]):
            self.cmb_trait.addItem(name, tid)
        trait_manage.addWidget(self.cmb_trait)
        btn_add = QPushButton("Add")
        btn_add.clicked.connect(self._add_trait)
        btn_remove = QPushButton("Remove")
        btn_remove.clicked.connect(self._remove_trait)
        trait_manage.addWidget(btn_add)
        trait_manage.addWidget(btn_remove)
        traits_layout.addLayout(trait_manage)
        tabs.addTab(traits_widget, "Traits")
        
        layout.addWidget(tabs)
        
        # Buttons
        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self._validate_and_accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)
        
        # Initialize data
        self._init_attributes()
        self._init_skills()
        self.traits_list: List[DataProp] = []
        
    def _init_attributes(self) -> None:
        self.tbl_attrs.setRowCount(len(DefaultAttributeIDs))
        for r, (aid, name) in enumerate(sorted(DefaultAttributeIDs.items())):
            self.tbl_attrs.setItem(r, 0, QTableWidgetItem(str(aid)))
            self.tbl_attrs.setItem(r, 1, QTableWidgetItem(name))
            val_item = QTableWidgetItem("1")
            val_item.setData(Qt.UserRole, aid)
            self.tbl_attrs.setItem(r, 2, val_item)
    
    def _init_skills(self) -> None:
        self.tbl_skills.setRowCount(len(DefaultSkillIDs))
        for r, (sid, name) in enumerate(sorted(DefaultSkillIDs.items())):
            self.tbl_skills.setItem(r, 0, QTableWidgetItem(str(sid)))
            self.tbl_skills.setItem(r, 1, QTableWidgetItem(name))
            lvl_item = QTableWidgetItem("0")
            lvl_item.setData(Qt.UserRole, sid)
            self.tbl_skills.setItem(r, 2, lvl_item)
    
    def _set_all_attrs(self, value: int) -> None:
        for r in range(self.tbl_attrs.rowCount()):
            item = self.tbl_attrs.item(r, 2)
            if item:
                item.setText(str(value))
    
    def _set_all_skills(self, value: int) -> None:
        for r in range(self.tbl_skills.rowCount()):
            item = self.tbl_skills.item(r, 2)
            if item:
                item.setText(str(value))
    
    def _add_trait(self) -> None:
        tid = self.cmb_trait.currentData()
        if tid is None:
            return
        if any(t.id == tid for t in self.traits_list):
            return
        name = DefaultTraitIDs[tid]
        self.traits_list.append(DataProp(id=tid, name=name))
        self._refresh_traits_list()
    
    def _remove_trait(self) -> None:
        row = self.lst_traits.currentRow()
        if row < 0:
            return
        item = self.lst_traits.item(row)
        if item:
            tid = item.data(Qt.UserRole)
            self.traits_list = [t for t in self.traits_list if t.id != tid]
            self._refresh_traits_list()
    
    def _refresh_traits_list(self) -> None:
        self.lst_traits.clear()
        for trait in self.traits_list:
            item = QListWidgetItem(trait.name)
            item.setData(Qt.UserRole, trait.id)
            self.lst_traits.addItem(item)
    
    def _validate_and_accept(self) -> None:
        if not self.txt_name.text().strip():
            QMessageBox.warning(self, "Validation", "Please enter a name.")
            return
        # Validate attributes
        for r in range(self.tbl_attrs.rowCount()):
            item = self.tbl_attrs.item(r, 2)
            if item:
                try:
                    val = int(item.text())
                    if val < 0:
                        raise ValueError()
                except ValueError:
                    QMessageBox.warning(self, "Validation", f"Invalid attribute value in row {r+1}.")
                    return
        # Validate skills
        for r in range(self.tbl_skills.rowCount()):
            item = self.tbl_skills.item(r, 2)
            if item:
                try:
                    val = int(item.text())
                    if val < 0:
                        raise ValueError()
                except ValueError:
                    QMessageBox.warning(self, "Validation", f"Invalid skill value in row {r+1}.")
                    return
        self.accept()
    
    @property
    def name(self) -> str:
        return self.txt_name.text().strip()
    
    @property
    def attributes(self) -> List[DataProp]:
        attrs = []
        for r in range(self.tbl_attrs.rowCount()):
            item = self.tbl_attrs.item(r, 2)
            if item:
                aid = item.data(Qt.UserRole)
                name_item = self.tbl_attrs.item(r, 1)
                name = name_item.text() if name_item else f"Attr {aid}"
                attrs.append(DataProp(id=aid, name=name, value=int(item.text())))
        return attrs
    
    @property
    def skills(self) -> List[DataProp]:
        skills = []
        for r in range(self.tbl_skills.rowCount()):
            item = self.tbl_skills.item(r, 2)
            if item:
                sid = item.data(Qt.UserRole)
                name_item = self.tbl_skills.item(r, 1)
                name = name_item.text() if name_item else f"Skill {sid}"
                skills.append(DataProp(id=sid, name=name, value=int(item.text())))
        return skills
    
    @property
    def traits(self) -> List[DataProp]:
        return self.traits_list.copy()


class SettingsDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.resize(400, 150)
        
        layout = QVBoxLayout(self)
        self.chk_backup = QCheckBox("Enable automatic backup when opening saves")
        layout.addWidget(self.chk_backup)
        
        buttons = QDialogButtonBox(QDialogButtonBox.Ok | QDialogButtonBox.Cancel)
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)
    
    def set_backup(self, enabled: bool) -> None:
        self.chk_backup.setChecked(enabled)
    
    def backup_enabled(self) -> bool:
        return self.chk_backup.isChecked()


class HelpDialog(QDialog):
    def __init__(self, help_text: str, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Help / Instructions")
        self.resize(700, 600)
        
        layout = QVBoxLayout(self)
        text = QTextEdit()
        text.setReadOnly(True)
        text.setPlainText(help_text)
        layout.addWidget(text)
        
        buttons = QDialogButtonBox(QDialogButtonBox.Ok)
        buttons.accepted.connect(self.accept)
        layout.addWidget(buttons)


class AboutDialog(QDialog):
    def __init__(self, parent=None) -> None:
        super().__init__(parent)
        self.setWindowTitle("About")
        self.resize(400, 200)
        
        layout = QVBoxLayout(self)
        layout.addWidget(QLabel("Moragar's Space Haven Save Editor"))
        layout.addWidget(QLabel("Cross-Platform Version"))
        layout.addWidget(QLabel(""))
        layout.addWidget(QLabel("Built for Space Haven Alpha 20"))
        layout.addWidget(QLabel(""))
        layout.addWidget(QLabel("Port by: Cross-Platform Implementation"))
        layout.addWidget(QLabel("Original by: Moragar"))
        
        buttons = QDialogButtonBox(QDialogButtonBox.Ok)
        buttons.accepted.connect(self.accept)
        layout.addWidget(buttons)


def main() -> None:
    app = QApplication(sys.argv)
    win = MainWindow()
    win.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()


