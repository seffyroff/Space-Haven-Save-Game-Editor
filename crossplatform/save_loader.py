from __future__ import annotations
from typing import List, Optional
from lxml import etree

try:
    from .models import SaveData, Ship, Character, DataProp, RelationshipInfo, StorageContainer, StorageItem
    from .id_collections import DefaultSkillIDs, DefaultTraitIDs, DefaultAttributeIDs, DefaultStorageIDs, ConditionsIDs
except ImportError:
    # Fallback for when running as standalone (PyInstaller)
    from models import SaveData, Ship, Character, DataProp, RelationshipInfo, StorageContainer, StorageItem
    from id_collections import DefaultSkillIDs, DefaultTraitIDs, DefaultAttributeIDs, DefaultStorageIDs, ConditionsIDs


def load_save(path: str) -> SaveData:
    parser = etree.XMLParser(remove_blank_text=False)
    xml_doc = etree.parse(path, parser)
    root = xml_doc.getroot()
    if root.tag != "game":
        raise ValueError("Invalid Space Haven save: missing root <game> element")

    save = SaveData(path=path, xml_doc=xml_doc)

    # Globals
    bank = root.find("playerBank")
    if bank is not None and bank.get("ca") is not None:
        try:
            save.credits = int(float(bank.get("ca")))
        except ValueError:
            save.credits = 0

    settings = root.find("settings")
    if settings is not None:
        diff = settings.find("diff")
        if diff is not None and diff.get("sandbox") is not None:
            save.sandbox = diff.get("sandbox").lower() == "true"

    try:
        ql1 = root.find("questLines")
        ql2 = ql1.find("questLines") if ql1 is not None else None
        if ql2 is not None:
            for l in ql2.findall("l"):
                if l.get("type") == "ExodusFleet":
                    pp = l.get("playerPrestigePoints")
                    if pp is not None:
                        save.prestige_points = int(pp)
                    break
    except Exception:
        pass

    # Ships
    ships: List[Ship] = []
    for ship_el in root.findall(".//ship"):
        try:
            sid = int(ship_el.get("sid", "0"))
        except ValueError:
            continue
        if sid == 0:
            continue
        sname = ship_el.get("sname") or "Unnamed Ship"
        sx = int(ship_el.get("sx", "0") or 0)
        sy = int(ship_el.get("sy", "0") or 0)
        if not any(s.sid == sid for s in ships):
            ships.append(Ship(sid=sid, sname=sname, sx=sx, sy=sy))
    save.ships = ships

    # Characters
    characters: List[Character] = []
    for ship_el in root.findall(".//ship"):
        ship_sid = int(ship_el.get("sid", "0") or 0)
        chars_parent = ship_el.find("characters")
        if chars_parent is None:
            continue
        for c in chars_parent.findall("c"):
            name = c.get("name") or "Unknown"
            try:
                ent_id = int(c.get("entId", "0"))
            except ValueError:
                ent_id = 0
            if ent_id == 0 or any(ch.entity_id == ent_id for ch in characters):
                continue
            ch = Character(name=name, entity_id=ent_id, ship_sid=ship_sid)

            pers = c.find("pers")
            # Skills
            skills_el = pers.find("skills") if pers is not None else None
            if skills_el is not None:
                for s in skills_el.findall("s"):
                    try:
                        sk = int(s.get("sk", "0"))
                        lvl = int(s.get("level", "0"))
                    except ValueError:
                        continue
                    ch.skills.append(DataProp(id=sk, name=DefaultSkillIDs.get(sk, f"Skill {sk}"), value=lvl))
            # Traits
            traits_el = pers.find("traits") if pers is not None else None
            if traits_el is not None:
                for t in traits_el.findall("t"):
                    try:
                        tid = int(t.get("id", "0"))
                    except ValueError:
                        continue
                    ch.traits.append(DataProp(id=tid, name=DefaultTraitIDs.get(tid, f"Trait {tid}")))
            # Attributes
            attrs_el = pers.find("attr") if pers is not None else None
            if attrs_el is not None:
                for a in attrs_el.findall("a"):
                    try:
                        aid = int(a.get("id", "0"))
                        pts = int(a.get("points", "0"))
                    except ValueError:
                        continue
                    ch.attributes.append(DataProp(id=aid, name=DefaultAttributeIDs.get(aid, f"Attr {aid}"), value=pts))
            # Conditions
            conds_el = pers.find("conditions") if pers is not None else None
            if conds_el is not None:
                for ce in conds_el.findall("c"):
                    try:
                        cid = int(ce.get("id", "0"))
                    except ValueError:
                        continue
                    if cid in ConditionsIDs:
                        ch.conditions.append(DataProp(id=cid, name=ConditionsIDs[cid]))
            # Relationships
            sociality = pers.find("sociality") if pers is not None else None
            rels = sociality.find("relationships") if sociality is not None else None
            if rels is not None:
                for le in rels.findall("l"):
                    try:
                        target_id = int(le.get("targetId", "0"))
                        friendship = int(le.get("friendship", "0"))
                        attraction = int(le.get("attraction", "0"))
                        compatibility = int(le.get("compatibility", "0"))
                    except ValueError:
                        continue
                    if target_id:
                        target = next((c2 for c2 in characters if c2.entity_id == target_id), None)
                        target_name = target.name if target else f"Unknown ID ({target_id})"
                        ch.relationships.append(RelationshipInfo(target_id, target_name, friendship, attraction, compatibility))

            characters.append(ch)

    save.characters = characters
    return save


def load_storage_containers(save: SaveData, ship_sid: int) -> List[StorageContainer]:
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    ship_el = root.find(f".//ship[@sid='{ship_sid}']")
    if ship_el is None:
        return []

    containers: List[StorageContainer] = []
    feats = ship_el.findall(".//feat[@eatAllowed]")
    idx = 0
    for feat in feats:
        inv = feat.find(".//inv")
        if inv is None:
            continue

        # Friendly name
        parent_e = feat.getparent()
        display = f"Storage Bay - {idx + 1}"
        parent_ent = None
        parent_obj = None
        if parent_e is not None and parent_e.tag == "e":
            ent_attr = parent_e.get("entId")
            obj_attr = parent_e.get("objId")
            try:
                if ent_attr:
                    parent_ent = int(ent_attr)
            except ValueError:
                parent_ent = None
            parent_obj = obj_attr
            if parent_ent:
                display = f"Container (ID: {parent_ent})"
            elif parent_obj:
                display = f"Storage (Type: {parent_obj}) - {idx + 1}"

        cont = StorageContainer(display_name=display, feat_element=feat, parent_ent_id=parent_ent, parent_obj_id=parent_obj)
        for s in inv.findall("s"):
            try:
                item_id = int(s.get("elementaryId", "0"))
                qty = int(s.get("inStorage", "0"))
            except ValueError:
                continue
            if qty > 0:
                cont.items.append(StorageItem(element_id=item_id, quantity=qty))
        if cont.items:
            containers.append(cont)
        idx += 1
    return containers


def update_globals_in_memory(save: SaveData, credits: Optional[int], sandbox: Optional[bool], prestige_points: Optional[int]) -> None:
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    if credits is not None:
        bank = root.find("playerBank")
        if bank is not None:
            bank.set("ca", str(credits))
            save.credits = credits
    if sandbox is not None:
        settings = root.find("settings")
        if settings is not None:
            diff = settings.find("diff")
            if diff is not None:
                diff.set("sandbox", "true" if sandbox else "false")
                save.sandbox = sandbox
    if prestige_points is not None:
        ql1 = root.find("questLines")
        ql2 = ql1.find("questLines") if ql1 is not None else None
        if ql2 is not None:
            for l in ql2.findall("l"):
                if l.get("type") == "ExodusFleet":
                    l.set("playerPrestigePoints", str(prestige_points))
                    save.prestige_points = prestige_points
                    break


def add_item_to_container(save: SaveData, container: StorageContainer, item_id: int, qty: int) -> None:
    assert save.xml_doc is not None
    inv = container.feat_element.find(".//inv")
    if inv is None:
        inv = etree.Element("inv")
        container.feat_element.append(inv)
    s = next((e for e in inv.findall("s") if e.get("elementaryId") == str(item_id)), None)
    if s is None:
        s = etree.Element("s")
        s.set("elementaryId", str(item_id))
        s.set("inStorage", str(qty))
        s.set("onTheWayIn", "0")
        s.set("onTheWayOut", "0")
        inv.append(s)
    else:
        current = int(s.get("inStorage", "0") or 0)
        s.set("inStorage", str(current + qty))

    found = next((i for i in container.items if i.element_id == item_id), None)
    if found:
        found.quantity += qty
    else:
        container.items.append(StorageItem(element_id=item_id, quantity=qty))


def delete_item_from_container(save: SaveData, container: StorageContainer, item_id: int) -> None:
    assert save.xml_doc is not None
    inv = container.feat_element.find(".//inv")
    if inv is None:
        return
    s = next((e for e in inv.findall("s") if e.get("elementaryId") == str(item_id)), None)
    if s is not None:
        inv.remove(s)
    container.items = [i for i in container.items if i.element_id != item_id]


def update_item_quantity(save: SaveData, container: StorageContainer, item_id: int, qty: int) -> None:
    assert save.xml_doc is not None
    inv = container.feat_element.find(".//inv")
    if inv is None:
        return
    s = next((e for e in inv.findall("s") if e.get("elementaryId") == str(item_id)), None)
    if qty <= 0:
        if s is not None:
            inv.remove(s)
        container.items = [i for i in container.items if i.element_id != item_id]
        return
    if s is None:
        s = etree.Element("s")
        s.set("elementaryId", str(item_id))
        inv.append(s)
    s.set("inStorage", str(qty))
    found = next((i for i in container.items if i.element_id == item_id), None)
    if found:
        found.quantity = qty
    else:
        container.items.append(StorageItem(element_id=item_id, quantity=qty))


def update_ship_size(save: SaveData, ship: Ship, squares_w: int, squares_h: int) -> None:
    assert save.xml_doc is not None
    sx = squares_w * 28
    sy = squares_h * 28
    root = save.xml_doc.getroot()
    ship_el = root.find(f".//ship[@sid='{ship.sid}']")
    if ship_el is None:
        return
    ship_el.set("sx", str(sx))
    ship_el.set("sy", str(sy))
    ship.sx = sx
    ship.sy = sy


def update_character_attribute(save: SaveData, character: Character, attr_id: int, value: int) -> None:
    """Update a character's attribute value in XML."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    attrs = pers.find("attr")
    if attrs is None:
        attrs = etree.Element("attr")
        pers.append(attrs)
    a_el = attrs.find(f"a[@id='{attr_id}']")
    if a_el is None:
        a_el = etree.Element("a")
        a_el.set("id", str(attr_id))
        attrs.append(a_el)
    a_el.set("points", str(value))
    # Update in-memory model
    attr = next((a for a in character.attributes if a.id == attr_id), None)
    if attr:
        attr.value = value
    else:
        character.attributes.append(DataProp(id=attr_id, name=DefaultAttributeIDs.get(attr_id, f"Attr {attr_id}"), value=value))


def update_character_skill(save: SaveData, character: Character, skill_id: int, level: int) -> None:
    """Update a character's skill level in XML."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    skills = pers.find("skills")
    if skills is None:
        skills = etree.Element("skills")
        pers.append(skills)
    s_el = skills.find(f"s[@sk='{skill_id}']")
    if s_el is None:
        s_el = etree.Element("s")
        s_el.set("sk", str(skill_id))
        skills.append(s_el)
    s_el.set("level", str(level))
    # Update in-memory model
    skill = next((s for s in character.skills if s.id == skill_id), None)
    if skill:
        skill.value = level
    else:
        character.skills.append(DataProp(id=skill_id, name=DefaultSkillIDs.get(skill_id, f"Skill {skill_id}"), value=level))


def add_character_trait(save: SaveData, character: Character, trait_id: int) -> None:
    """Add a trait to a character."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    traits = pers.find("traits")
    if traits is None:
        traits = etree.Element("traits")
        pers.append(traits)
    # Check if already exists
    if traits.find(f"t[@id='{trait_id}']") is not None:
        return
    t_el = etree.Element("t")
    t_el.set("id", str(trait_id))
    traits.append(t_el)
    # Update in-memory model
    if not any(t.id == trait_id for t in character.traits):
        character.traits.append(DataProp(id=trait_id, name=DefaultTraitIDs.get(trait_id, f"Trait {trait_id}")))


def remove_character_trait(save: SaveData, character: Character, trait_id: int) -> None:
    """Remove a trait from a character."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    traits = pers.find("traits")
    if traits is not None:
        t_el = traits.find(f"t[@id='{trait_id}']")
        if t_el is not None:
            traits.remove(t_el)
    # Update in-memory model
    character.traits = [t for t in character.traits if t.id != trait_id]


def remove_character_condition(save: SaveData, character: Character, condition_id: int) -> None:
    """Remove a condition from a character."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    conditions = pers.find("conditions")
    if conditions is not None:
        c_el = conditions.find(f"c[@id='{condition_id}']")
        if c_el is not None:
            conditions.remove(c_el)
    # Update in-memory model
    character.conditions = [c for c in character.conditions if c.id != condition_id]


def update_character_relationship(save: SaveData, character: Character, target_id: int, friendship: Optional[int] = None, attraction: Optional[int] = None, compatibility: Optional[int] = None) -> None:
    """Update a character's relationship values."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    char_el = next((c for c in root.findall(".//c") if c.get("entId") == str(character.entity_id)), None)
    if char_el is None:
        return
    pers = char_el.find("pers")
    if pers is None:
        return
    sociality = pers.find("sociality")
    if sociality is None:
        sociality = etree.Element("sociality")
        pers.append(sociality)
    relationships = sociality.find("relationships")
    if relationships is None:
        relationships = etree.Element("relationships")
        sociality.append(relationships)
    l_el = relationships.find(f"l[@targetId='{target_id}']")
    if l_el is None:
        l_el = etree.Element("l")
        l_el.set("targetId", str(target_id))
        relationships.append(l_el)
    if friendship is not None:
        l_el.set("friendship", str(friendship))
    if attraction is not None:
        l_el.set("attraction", str(attraction))
    if compatibility is not None:
        l_el.set("compatibility", str(compatibility))
    # Update in-memory model
    rel = next((r for r in character.relationships if r.target_id == target_id), None)
    if rel:
        if friendship is not None:
            rel.friendship = friendship
        if attraction is not None:
            rel.attraction = attraction
        if compatibility is not None:
            rel.compatibility = compatibility
    else:
        # Need target name
        target_char = next((c for c in save.characters if c.entity_id == target_id), None)
        target_name = target_char.name if target_char else f"Unknown ID ({target_id})"
        character.relationships.append(RelationshipInfo(
            target_id, target_name,
            friendship if friendship is not None else 0,
            attraction if attraction is not None else 0,
            compatibility if compatibility is not None else 0
        ))


def create_new_crew_member(save: SaveData, ship_sid: int, name: str, attributes: List[DataProp], skills: List[DataProp], traits: List[DataProp]) -> Character:
    """Create a new crew member and add it to the XML and in-memory list."""
    assert save.xml_doc is not None
    root = save.xml_doc.getroot()
    
    # Get next ID from idCounter
    id_counter_attr = root.get("idCounter")
    if id_counter_attr is None:
        raise ValueError("Cannot find 'idCounter' attribute on root <game> element.")
    try:
        next_id = int(id_counter_attr) + 1
    except ValueError:
        raise ValueError(f"Cannot parse 'idCounter' value '{id_counter_attr}'.")
    root.set("idCounter", str(next_id))
    
    # Find ship and characters node
    ship_el = next((s for s in root.findall(".//ship") if s.get("sid") == str(ship_sid)), None)
    if ship_el is None:
        raise ValueError(f"Ship with SID {ship_sid} not found.")
    characters_node = ship_el.find("characters")
    if characters_node is None:
        raise ValueError(f"<characters> node not found for ship SID {ship_sid}.")
    
    # Find template character (first existing crew member)
    template = characters_node.find("c")
    if template is None:
        raise ValueError(f"No existing crew members to use as template for ship SID {ship_sid}.")
    
    # Clone template (lxml safe way)
    new_char_el = etree.fromstring(etree.tostring(template))
    
    # Update basic info
    new_char_el.set("name", name)
    new_char_el.set("entId", str(next_id))
    state = new_char_el.find("state")
    if state is not None:
        state.set("bedLink", "")
    
    # Update pers node
    pers = new_char_el.find("pers")
    if pers is not None:
        # Attributes
        attrs_el = pers.find("attr")
        if attrs_el is not None:
            attrs_el.clear()
            for attr in attributes:
                a_el = etree.Element("a")
                a_el.set("id", str(attr.id))
                a_el.set("points", str(attr.value))
                attrs_el.append(a_el)
        
        # Skills
        skills_el = pers.find("skills")
        if skills_el is not None:
            skills_el.clear()
            for skill in skills:
                s_el = etree.Element("s")
                s_el.set("sk", str(skill.id))
                s_el.set("level", str(skill.value))
                skills_el.append(s_el)
        
        # Traits
        traits_el = pers.find("traits")
        if traits_el is not None:
            traits_el.clear()
            for trait in traits:
                t_el = etree.Element("t")
                t_el.set("id", str(trait.id))
                traits_el.append(t_el)
        
        # Clear conditions and relationships
        conds_el = pers.find("conditions")
        if conds_el is not None:
            conds_el.clear()
        sociality = pers.find("sociality")
        if sociality is not None:
            rels = sociality.find("relationships")
            if rels is not None:
                rels.clear()
    
    # Add to XML
    characters_node.append(new_char_el)
    
    # Create in-memory character
    new_char = Character(
        name=name,
        entity_id=next_id,
        ship_sid=ship_sid,
        attributes=[DataProp(id=a.id, name=a.name, value=a.value) for a in attributes],
        skills=[DataProp(id=s.id, name=s.name, value=s.value) for s in skills],
        traits=[DataProp(id=t.id, name=t.name) for t in traits],
        conditions=[],
        relationships=[]
    )
    save.characters.append(new_char)
    return new_char


def save_to_disk(save: SaveData) -> None:
    assert save.xml_doc is not None
    save.xml_doc.write(save.path, encoding="utf-8", pretty_print=False)


