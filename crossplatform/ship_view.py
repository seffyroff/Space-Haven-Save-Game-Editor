from __future__ import annotations
from typing import Optional, List, Dict, Tuple
from PySide6.QtCore import Qt, QPoint, QRect, QSize, Signal
from PySide6.QtGui import QPainter, QColor, QBrush, QPen, QMouseEvent, QWheelEvent, QKeyEvent, QPixmap, QTransform
from PySide6.QtWidgets import QWidget, QScrollArea, QVBoxLayout, QHBoxLayout, QPushButton, QLabel, QSlider

try:
    from .models import ShipObject
    from .asset_loader import get_asset_loader
except ImportError:
    from models import ShipObject
    from asset_loader import get_asset_loader


# Material ID to color mapping (common Space Haven materials)
MATERIAL_COLORS: Dict[str, QColor] = {
    # Walls and floors
    "-2": QColor(40, 40, 50),  # Dark background
    
    # Common materials
    "46": QColor(100, 100, 120),  # Grey walls/floors
    "2942": QColor(200, 200, 220),  # Light grey
    "61504": QColor(30, 30, 40),  # Dark hull
    "4160": QColor(60, 60, 80),  # Medium hull
    
    # Equipment colors
    "32": QColor(100, 150, 200),  # Blue equipment
    "144": QColor(120, 120, 140),  # Grey equipment
    "191": QColor(80, 80, 100),  # Dark grey
    
    # Default colors
    "default": QColor(150, 150, 150),  # Default grey
}


def get_material_color(material_id: Optional[str]) -> QColor:
    """Get color for a material ID."""
    if material_id is None:
        return MATERIAL_COLORS.get("default", QColor(150, 150, 150))
    return MATERIAL_COLORS.get(material_id, MATERIAL_COLORS.get("default", QColor(150, 150, 150)))


class ShipViewWidget(QWidget):
    """Custom widget for rendering ship layout visually."""
    
    # Signals
    object_selected = Signal(object)  # Emits ShipObject
    object_moved = Signal(object, int, int)  # Emits ShipObject, grid_x, grid_y
    object_context_menu = Signal(object, QPoint)  # Emits ShipObject, screen_pos
    empty_space_context_menu = Signal(Tuple[int, int], QPoint)  # Emits (grid_x, grid_y), screen_pos
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMinimumSize(800, 600)
        self.setMouseTracking(True)
        self.setFocusPolicy(Qt.StrongFocus)
        
        # View settings
        self.zoom_level = 1.0
        self.pan_offset = QPoint(0, 0)
        self.grid_size = 28  # Pixels per grid square (base size)
        
        # Isometric rendering settings
        self.isometric_tile_width = 56  # Width of isometric tile
        self.isometric_tile_height = 28  # Height of isometric tile (half width for diamond shape)
        
        # Ship data
        self.ship_objects: List[ShipObject] = []
        self.ship_width = 0  # Grid width
        self.ship_height = 0  # Grid height
        
        # Selection and interaction
        self.selected_object: Optional[ShipObject] = None
        self.dragging = False
        self.drag_start_pos: Optional[QPoint] = None
        self.drag_object: Optional[ShipObject] = None
        self.hovered_object: Optional[ShipObject] = None
        
        # Background color
        self.bg_color = QColor(20, 20, 30)  # Dark space-like background
        
        # Asset loader for sprites
        self.asset_loader = get_asset_loader()
        self.sprite_cache: Dict[str, QPixmap] = {}  # Cache sprites by material ID
        
        # Debug: Print asset loader status
        if self.asset_loader:
            print(f"Asset loader initialized: {len(self.asset_loader.texture_regions)} regions, {len(self.asset_loader.asset_id_to_region)} asset IDs")
        else:
            print("Warning: Asset loader is None")
        
    def set_ship_data(self, objects: List[ShipObject], width: int, height: int):
        """Set the ship data to render."""
        self.ship_objects = objects
        self.ship_width = width
        self.ship_height = height
        # Update widget size to match ship (isometric view needs more space)
        if width > 0 and height > 0:
            # Calculate isometric bounding box
            top_left = self._grid_to_isometric(0, 0)
            top_right = self._grid_to_isometric(width, 0)
            bottom_left = self._grid_to_isometric(0, height)
            bottom_right = self._grid_to_isometric(width, height)
            
            min_x = min(top_left[0], top_right[0], bottom_left[0], bottom_right[0])
            max_x = max(top_left[0], top_right[0], bottom_left[0], bottom_right[0])
            min_y = min(top_left[1], top_right[1], bottom_left[1], bottom_right[1])
            max_y = max(top_left[1], top_right[1], bottom_left[1], bottom_right[1])
            
            iso_width = max_x - min_x + 100  # Add padding
            iso_height = max_y - min_y + 100
            self.setMinimumSize(iso_width, iso_height)
        self.update()
    
    def set_selected_object(self, obj: Optional[ShipObject]):
        """Set the selected object."""
        self.selected_object = obj
        self.update()
    
    def get_object_at_position(self, pos: QPoint) -> Optional[ShipObject]:
        """Get the object at a given screen position."""
        # Convert screen position to grid coordinates
        grid_x = int((pos.x() - self.pan_offset.x()) / (self.grid_size * self.zoom_level))
        grid_y = int((pos.y() - self.pan_offset.y()) / (self.grid_size * self.zoom_level))
        
        # Check each object
        for obj in self.ship_objects:
            obj_grid_x = obj.x // 28
            obj_grid_y = obj.y // 28
            if (obj_grid_x <= grid_x < obj_grid_x + obj.width and
                obj_grid_y <= grid_y < obj_grid_y + obj.height):
                return obj
        return None
    
    def paintEvent(self, event):
        """Paint the ship layout."""
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)
        
        # Fill background
        painter.fillRect(self.rect(), self.bg_color)
        
        # Apply zoom and pan
        painter.translate(self.pan_offset)
        painter.scale(self.zoom_level, self.zoom_level)
        
        # Draw grid
        self._draw_grid(painter)
        
        # Draw objects
        for obj in self.ship_objects:
            self._draw_object(painter, obj)
        
        # Draw selection highlight
        if self.selected_object:
            self._draw_selection(painter, self.selected_object)
        
        # Draw hover highlight
        if self.hovered_object and self.hovered_object != self.selected_object:
            self._draw_hover(painter, self.hovered_object)
    
    def _grid_to_isometric(self, grid_x: int, grid_y: int) -> Tuple[int, int]:
        """Convert grid coordinates to isometric screen coordinates.
        
        Args:
            grid_x: Grid X coordinate
            grid_y: Grid Y coordinate
            
        Returns:
            (screen_x, screen_y) tuple
        """
        # Isometric projection: diamond-shaped tiles
        iso_x = (grid_x - grid_y) * (self.isometric_tile_width // 2)
        iso_y = (grid_x + grid_y) * (self.isometric_tile_height // 2)
        return (iso_x, iso_y)
    
    def _isometric_to_grid(self, screen_x: int, screen_y: int) -> Tuple[int, int]:
        """Convert isometric screen coordinates to grid coordinates.
        
        Args:
            screen_x: Screen X coordinate
            screen_y: Screen Y coordinate
            
        Returns:
            (grid_x, grid_y) tuple
        """
        # Reverse isometric projection
        # Account for zoom and pan
        adjusted_x = (screen_x - self.pan_offset.x()) / self.zoom_level
        adjusted_y = (screen_y - self.pan_offset.y()) / self.zoom_level
        
        grid_x = int((adjusted_x / (self.isometric_tile_width // 2) + adjusted_y / (self.isometric_tile_height // 2)) / 2)
        grid_y = int((adjusted_y / (self.isometric_tile_height // 2) - adjusted_x / (self.isometric_tile_width // 2)) / 2)
        return (grid_x, grid_y)
    
    def _draw_grid(self, painter: QPainter):
        """Draw the isometric grid lines."""
        pen = QPen(QColor(50, 50, 60), 1)
        painter.setPen(pen)
        
        # Draw diagonal lines going from top-left to bottom-right
        for x in range(0, self.ship_width + 1):
            for y in range(0, self.ship_height + 1):
                px, py = self._grid_to_isometric(x, y)
                # Draw lines to adjacent tiles
                if x < self.ship_width:
                    px2, py2 = self._grid_to_isometric(x + 1, y)
                    painter.drawLine(px, py, px2, py2)
                if y < self.ship_height:
                    px2, py2 = self._grid_to_isometric(x, y + 1)
                    painter.drawLine(px, py, px2, py2)
        
        # Also draw the diamond outline for each cell
        for x in range(self.ship_width):
            for y in range(self.ship_height):
                px, py = self._grid_to_isometric(x, y)
                px1, py1 = self._grid_to_isometric(x + 1, y)
                px2, py2 = self._grid_to_isometric(x + 1, y + 1)
                px3, py3 = self._grid_to_isometric(x, y + 1)
                
                # Draw diamond shape
                painter.drawLine(px, py, px1, py1)  # Top-right
                painter.drawLine(px1, py1, px2, py2)  # Right-bottom
                painter.drawLine(px2, py2, px3, py3)  # Bottom-left
                painter.drawLine(px3, py3, px, py)  # Left-top
    
    def _get_sprite(self, material_id: Optional[str]) -> Optional[QPixmap]:
        """Get sprite for a material ID, using cache or loading from assets."""
        if material_id is None:
            return None
        
        # Check cache first
        if material_id in self.sprite_cache:
            cached = self.sprite_cache[material_id]
            if cached and not cached.isNull():
                return cached
        
        # Try to load from asset loader
        if self.asset_loader:
            try:
                sprite = self.asset_loader.get_region_pixmap(material_id)
                if sprite and not sprite.isNull():
                    self.sprite_cache[material_id] = sprite
                    return sprite
                else:
                    # Debug: check if region exists but failed to load
                    region = self.asset_loader.get_region_info(material_id)
                    if region:
                        print(f"Warning: Sprite for material {material_id} exists but failed to load (region: {region.name}, texture: {region.texture_index})")
            except Exception as e:
                print(f"Error loading sprite for material {material_id}: {e}")
        
        return None
    
    def _draw_object(self, painter: QPainter, obj: ShipObject):
        """Draw a single object in isometric view."""
        grid_x = obj.x // 28
        grid_y = obj.y // 28
        
        # Convert to isometric coordinates
        px, py = self._grid_to_isometric(grid_x, grid_y)
        
        # Calculate object size in isometric space
        # For multi-tile objects, calculate the bounding box
        if obj.width > 1 or obj.height > 1:
            # Get corners of the object's bounding box
            px1, py1 = self._grid_to_isometric(grid_x, grid_y)
            px2, py2 = self._grid_to_isometric(grid_x + obj.width - 1, grid_y)
            px3, py3 = self._grid_to_isometric(grid_x + obj.width - 1, grid_y + obj.height - 1)
            px4, py4 = self._grid_to_isometric(grid_x, grid_y + obj.height - 1)
            
            # Find bounding box
            min_x = min(px1, px2, px3, px4)
            max_x = max(px1, px2, px3, px4)
            min_y = min(py1, py2, py3, py4)
            max_y = max(py1, py2, py3, py4)
            
            px = min_x
            py = min_y
            width = max_x - min_x
            height = max_y - min_y
        else:
            # Single tile - use standard isometric tile size
            width = self.isometric_tile_width
            height = self.isometric_tile_height
        
        # Try to get sprite
        sprite = self._get_sprite(obj.obj_id)
        
        # Debug output for first few objects
        if len(self.sprite_cache) < 5 and obj.obj_id:
            print(f"Loading sprite for material {obj.obj_id}: {'SUCCESS' if sprite and not sprite.isNull() else 'FAILED'}")
        
        if sprite and not sprite.isNull():
            # Draw sprite
            # Save current transform
            painter.save()
            
            # Apply rotation if needed
            if obj.rotation != 0:
                center_x = px + width / 2
                center_y = py + height / 2
                painter.translate(center_x, center_y)
                painter.rotate(obj.rotation)
                painter.translate(-center_x, -center_y)
            
            # Scale sprite to fit grid square size
            scaled_sprite = sprite.scaled(
                width, height,
                Qt.IgnoreAspectRatio,
                Qt.SmoothTransformation
            )
            painter.drawPixmap(px, py, scaled_sprite)
            
            # Restore transform
            painter.restore()
            
            # Draw object ID label on top if space permits
            if width > 50 and height > 50:
                painter.setPen(QPen(Qt.white, 1))
                painter.setBrush(QBrush(QColor(0, 0, 0, 128)))  # Semi-transparent background
                font_rect = QRect(px + 2, py + 2, min(60, width - 4), 20)
                painter.drawRect(font_rect)
                painter.drawText(font_rect, Qt.AlignCenter, str(obj.entity_id))
        else:
            # Fallback to colored rectangle if no sprite
            color = get_material_color(obj.obj_id)
            brush = QBrush(color)
            painter.setBrush(brush)
            pen = QPen(QColor(100, 100, 100), 1)
            painter.setPen(pen)
            
            # Apply rotation if needed
            if obj.rotation != 0:
                center_x = px + width / 2
                center_y = py + height / 2
                painter.save()
                painter.translate(center_x, center_y)
                painter.rotate(obj.rotation)
                painter.translate(-center_x, -center_y)
                painter.drawRect(px, py, width, height)
                painter.restore()
            else:
                painter.drawRect(px, py, width, height)
            
            # Draw object ID label
            if width > 40 and height > 40:
                painter.setPen(QPen(Qt.white))
                painter.drawText(QRect(px, py, width, height), Qt.AlignCenter, str(obj.entity_id))
    
    def _draw_selection(self, painter: QPainter, obj: ShipObject):
        """Draw selection highlight in isometric view."""
        grid_x = obj.x // 28
        grid_y = obj.y // 28
        
        # Convert to isometric coordinates
        if obj.width > 1 or obj.height > 1:
            # Draw outline around multi-tile object
            corners = [
                self._grid_to_isometric(grid_x, grid_y),
                self._grid_to_isometric(grid_x + obj.width - 1, grid_y),
                self._grid_to_isometric(grid_x + obj.width - 1, grid_y + obj.height - 1),
                self._grid_to_isometric(grid_x, grid_y + obj.height - 1),
            ]
            pen = QPen(QColor(255, 255, 0), 3)  # Yellow highlight
            painter.setPen(pen)
            painter.setBrush(QBrush(Qt.NoBrush))
            # Draw polygon outline
            from PySide6.QtGui import QPolygon
            polygon = QPolygon([QPoint(int(c[0]), int(c[1])) for c in corners])
            painter.drawPolygon(polygon)
        else:
            # Single tile - draw diamond outline
            px, py = self._grid_to_isometric(grid_x, grid_y)
            px1, py1 = self._grid_to_isometric(grid_x + 1, grid_y)
            px2, py2 = self._grid_to_isometric(grid_x + 1, grid_y + 1)
            px3, py3 = self._grid_to_isometric(grid_x, grid_y + 1)
            
            pen = QPen(QColor(255, 255, 0), 3)  # Yellow highlight
            painter.setPen(pen)
            painter.setBrush(QBrush(Qt.NoBrush))
            painter.drawLine(px, py, px1, py1)
            painter.drawLine(px1, py1, px2, py2)
            painter.drawLine(px2, py2, px3, py3)
            painter.drawLine(px3, py3, px, py)
    
    def _draw_hover(self, painter: QPainter, obj: ShipObject):
        """Draw hover highlight in isometric view."""
        grid_x = obj.x // 28
        grid_y = obj.y // 28
        
        # Convert to isometric coordinates - similar to selection but with orange
        if obj.width > 1 or obj.height > 1:
            corners = [
                self._grid_to_isometric(grid_x, grid_y),
                self._grid_to_isometric(grid_x + obj.width - 1, grid_y),
                self._grid_to_isometric(grid_x + obj.width - 1, grid_y + obj.height - 1),
                self._grid_to_isometric(grid_x, grid_y + obj.height - 1),
            ]
            pen = QPen(QColor(255, 165, 0), 2)  # Orange highlight
            painter.setPen(pen)
            painter.setBrush(QBrush(Qt.NoBrush))
            from PySide6.QtGui import QPolygon
            polygon = QPolygon([QPoint(int(c[0]), int(c[1])) for c in corners])
            painter.drawPolygon(polygon)
        else:
            px, py = self._grid_to_isometric(grid_x, grid_y)
            px1, py1 = self._grid_to_isometric(grid_x + 1, grid_y)
            px2, py2 = self._grid_to_isometric(grid_x + 1, grid_y + 1)
            px3, py3 = self._grid_to_isometric(grid_x, grid_y + 1)
            
            pen = QPen(QColor(255, 165, 0), 2)  # Orange highlight
            painter.setPen(pen)
            painter.setBrush(QBrush(Qt.NoBrush))
            painter.drawLine(px, py, px1, py1)
            painter.drawLine(px1, py1, px2, py2)
            painter.drawLine(px2, py2, px3, py3)
            painter.drawLine(px3, py3, px, py)
    
    def mousePressEvent(self, event: QMouseEvent):
        """Handle mouse press."""
        if event.button() == Qt.LeftButton:
            obj = self.get_object_at_position(event.pos())
            if obj:
                self.selected_object = obj
                self.dragging = True
                self.drag_start_pos = event.pos()
                self.drag_object = obj
                self.update()
                # Emit signal for parent to handle
                self.object_selected.emit(obj)
            else:
                self.selected_object = None
                self.update()
        elif event.button() == Qt.RightButton:
            obj = self.get_object_at_position(event.pos())
            if obj:
                # Show context menu (handled by parent)
                self.object_context_menu.emit(obj, event.pos())
            else:
                # Show add menu for empty space
                grid_pos = self._screen_to_grid(event.pos())
                self.empty_space_context_menu.emit(grid_pos, event.pos())
    
    def mouseMoveEvent(self, event: QMouseEvent):
        """Handle mouse move."""
        obj = self.get_object_at_position(event.pos())
        
        if self.dragging and self.drag_object and self.drag_start_pos:
            # Calculate new position
            delta = event.pos() - self.drag_start_pos
            grid_delta_x = int(delta.x() / (self.grid_size * self.zoom_level))
            grid_delta_y = int(delta.y() / (self.grid_size * self.zoom_level))
            
            if grid_delta_x != 0 or grid_delta_y != 0:
                old_grid_x = self.drag_object.x // 28
                old_grid_y = self.drag_object.y // 28
                new_grid_x = max(0, min(self.ship_width - self.drag_object.width, old_grid_x + grid_delta_x))
                new_grid_y = max(0, min(self.ship_height - self.drag_object.height, old_grid_y + grid_delta_y))
                
                # Update position (will be saved when mouse is released)
                self.drag_object.x = new_grid_x * 28
                self.drag_object.y = new_grid_y * 28
                self.drag_start_pos = event.pos()
                self.update()
        
        # Update hover
        if obj != self.hovered_object:
            self.hovered_object = obj
            self.update()
    
    def mouseReleaseEvent(self, event: QMouseEvent):
        """Handle mouse release."""
        if event.button() == Qt.LeftButton and self.dragging:
            if self.drag_object:
                # Emit signal for parent to save new position
                grid_x = self.drag_object.x // 28
                grid_y = self.drag_object.y // 28
                self.object_moved.emit(self.drag_object, grid_x, grid_y)
            self.dragging = False
            self.drag_start_pos = None
    
    def wheelEvent(self, event: QWheelEvent):
        """Handle mouse wheel for zooming."""
        delta = event.angleDelta().y()
        zoom_factor = 1.1 if delta > 0 else 0.9
        self.zoom_level = max(0.1, min(5.0, self.zoom_level * zoom_factor))
        self.update()
    
    def keyPressEvent(self, event: QKeyEvent):
        """Handle keyboard for panning."""
        step = 50
        if event.key() == Qt.Key_Left:
            self.pan_offset += QPoint(step, 0)
        elif event.key() == Qt.Key_Right:
            self.pan_offset += QPoint(-step, 0)
        elif event.key() == Qt.Key_Up:
            self.pan_offset += QPoint(0, step)
        elif event.key() == Qt.Key_Down:
            self.pan_offset += QPoint(0, -step)
        else:
            return
        self.update()
    
    def _screen_to_grid(self, screen_pos: QPoint) -> Tuple[int, int]:
        """Convert screen position to grid coordinates (isometric)."""
        return self._isometric_to_grid(screen_pos.x(), screen_pos.y())
    
    def fit_to_view(self):
        """Fit the entire ship in view."""
        if self.ship_width == 0 or self.ship_height == 0:
            return
        
        view_width = self.width()
        view_height = self.height()
        
        ship_pixel_width = self.ship_width * self.grid_size
        ship_pixel_height = self.ship_height * self.grid_size
        
        zoom_x = view_width / ship_pixel_width if ship_pixel_width > 0 else 1.0
        zoom_y = view_height / ship_pixel_height if ship_pixel_height > 0 else 1.0
        
        self.zoom_level = min(zoom_x, zoom_y, 1.0) * 0.9  # 90% to add padding
        self.pan_offset = QPoint(0, 0)
        self.update()
    
    def reset_view(self):
        """Reset zoom and pan."""
        self.zoom_level = 1.0
        self.pan_offset = QPoint(0, 0)
        self.update()

