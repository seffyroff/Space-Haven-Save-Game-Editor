from __future__ import annotations
from dataclasses import dataclass, field
from typing import List, Optional


@dataclass
class DataProp:
    id: int
    name: str
    value: int = 0
    max_value: Optional[int] = None


@dataclass
class RelationshipInfo:
    target_id: int
    target_name: str
    friendship: int
    attraction: int
    compatibility: int


@dataclass
class Character:
    name: str
    entity_id: int
    ship_sid: int
    stats: List[DataProp] = field(default_factory=list)
    attributes: List[DataProp] = field(default_factory=list)
    skills: List[DataProp] = field(default_factory=list)
    traits: List[DataProp] = field(default_factory=list)
    conditions: List[DataProp] = field(default_factory=list)
    relationships: List[RelationshipInfo] = field(default_factory=list)


@dataclass
class StorageItem:
    element_id: int
    quantity: int


@dataclass
class StorageContainer:
    display_name: str
    feat_element: object  # lxml element reference
    items: List[StorageItem] = field(default_factory=list)
    parent_ent_id: Optional[int] = None
    parent_obj_id: Optional[str] = None


@dataclass
class Ship:
    sid: int
    sname: str
    sx: int
    sy: int
    storage_items: List[StorageItem] = field(default_factory=list)


@dataclass
class SaveData:
    path: str
    credits: int = 0
    sandbox: bool = False
    prestige_points: int = 0
    ships: List[Ship] = field(default_factory=list)
    characters: List[Character] = field(default_factory=list)
    current_ship_sid: Optional[int] = None
    xml_doc: Optional[object] = None  # lxml.etree._ElementTree


