from __future__ import annotations
import zipfile
import zlib
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Optional, Dict, Tuple
from dataclasses import dataclass

from PySide6.QtGui import QPixmap, QImage, QColor
from PySide6.QtCore import QByteArray


@dataclass
class TextureRegion:
    """Represents a region in a texture atlas."""
    name: str
    texture_index: int
    x: int
    y: int
    width: int
    height: int
    asset_id: str
    pixmap: Optional[QPixmap] = None


class AssetLoader:
    """Loader for Space Haven game assets from JAR file."""
    
    def __init__(self, jar_path: Optional[str] = None):
        """Initialize asset loader.
        
        Args:
            jar_path: Path to spacehaven.jar file. If None, will try to find it.
        """
        self.jar_path = jar_path or self._find_jar_path()
        self.jar_file: Optional[zipfile.ZipFile] = None
        self.texture_regions: Dict[str, TextureRegion] = {}
        self.texture_cache: Dict[int, QPixmap] = {}  # Cache for texture index -> pixmap
        self.asset_id_to_region: Dict[str, TextureRegion] = {}
        self.texture_sizes: Dict[int, Tuple[int, int]] = {}  # Cache texture dimensions
        
    def _find_jar_path(self) -> Optional[str]:
        """Try to find spacehaven.jar in common locations."""
        possible_paths = [
            Path.home() / ".local" / "share" / "Steam" / "steamapps" / "common" / "SpaceHaven" / "spacehaven.jar",
            Path("/usr/share/games/SpaceHaven/spacehaven.jar"),
            Path("/opt/SpaceHaven/spacehaven.jar"),
        ]
        for path in possible_paths:
            if path.exists():
                return str(path)
        return None
    
    def open_jar(self) -> bool:
        """Open the JAR file for reading."""
        if not self.jar_path or not Path(self.jar_path).exists():
            return False
        try:
            self.jar_file = zipfile.ZipFile(self.jar_path, 'r')
            return True
        except Exception:
            return False
    
    def close_jar(self):
        """Close the JAR file."""
        if self.jar_file:
            self.jar_file.close()
            self.jar_file = None
    
    def load_texture_regions(self) -> bool:
        """Load texture regions from library/textures XML."""
        if not self.jar_file:
            if not self.open_jar():
                return False
        
        try:
            # Read textures XML
            textures_data = self.jar_file.read("library/textures")
            root = ET.fromstring(textures_data)
            
            # Parse texture sizes first
            textures_el = root.find(".//textures")
            if textures_el is not None:
                for t_el in textures_el.findall("t"):
                    try:
                        idx = int(t_el.get("i", "0"))
                        w = int(t_el.get("w", "0"))
                        h = int(t_el.get("h", "0"))
                        if w > 0 and h > 0:
                            self.texture_sizes[idx] = (w, h)
                    except (ValueError, AttributeError):
                        continue
            
            # Parse regions
            regions_el = root.find(".//regions")
            if regions_el is None:
                return False
            
            for re_el in regions_el.findall("re"):
                region = TextureRegion(
                    name=re_el.get("n", ""),
                    texture_index=int(re_el.get("t", "0")),
                    x=int(re_el.get("x", "0")),
                    y=int(re_el.get("y", "0")),
                    width=int(re_el.get("w", "0")),
                    height=int(re_el.get("h", "0")),
                    asset_id=re_el.get("id", "")
                )
                # Store by name and asset ID
                self.texture_regions[region.name] = region
                if region.asset_id:
                    self.asset_id_to_region[region.asset_id] = region
            
            return True
        except Exception as e:
            print(f"Error loading texture regions: {e}")
            return False
    
    def load_texture(self, texture_index: int) -> Optional[QPixmap]:
        """Load a texture from a .cim file.
        
        Args:
            texture_index: Index of the texture (maps to library/{index}.cim)
            
        Returns:
            QPixmap of the texture, or None if failed
        """
        if texture_index in self.texture_cache:
            return self.texture_cache[texture_index]
        
        if not self.jar_file:
            if not self.open_jar():
                return None
        
        try:
            # Read compressed image
            cim_path = f"library/{texture_index}.cim"
            if cim_path not in self.jar_file.namelist():
                return None
            
            compressed_data = self.jar_file.read(cim_path)
            
            # Decompress
            decompressed_data = zlib.decompress(compressed_data)
            
            # Get texture dimensions from XML
            if texture_index in self.texture_sizes:
                width, height = self.texture_sizes[texture_index]
            else:
                # Fallback: calculate from data size
                # RGBA = 4 bytes per pixel
                total_pixels = len(decompressed_data) // 4
                import math
                side = int(math.sqrt(total_pixels))
                width, height = side, side
            
            # Calculate expected RGBA data size
            expected_rgba_size = width * height * 4
            
            # CIM files have a header (typically 12 bytes) followed by raw RGBA data
            # The pixel data is at the END of the decompressed data
            if len(decompressed_data) >= expected_rgba_size:
                # Take the last expected_rgba_size bytes (this skips any header)
                pixel_data = decompressed_data[-expected_rgba_size:]
            else:
                # Data is smaller than expected, use what we have
                pixel_data = decompressed_data
                expected_rgba_size = len(pixel_data)
            
            # Try loading as PNG/JPG first (in case it's embedded)
            image = QImage.fromData(QByteArray(decompressed_data))
            if image.isNull():
                # Load as raw RGBA data
                if len(pixel_data) >= expected_rgba_size:
                    try:
                        # Ensure we have bytes (not QByteArray)
                        pixel_bytes = bytes(pixel_data[:expected_rgba_size]) if isinstance(pixel_data, QByteArray) else pixel_data[:expected_rgba_size]
                        
                        # Create QImage from raw RGBA data
                        # Space Haven uses RGBA8888 format
                        image = QImage(pixel_bytes, width, height, QImage.Format_RGBA8888)
                        if image.isNull():
                            # Try ARGB32 (BGRA ordering)
                            image = QImage(pixel_bytes, width, height, QImage.Format_ARGB32)
                            if image.isNull():
                                # Try RGB32 (no alpha)
                                image = QImage(pixel_bytes[:width*height*3], width, height, QImage.Format_RGB888)
                        if not image.isNull():
                            print(f"Successfully loaded texture {texture_index} ({width}x{height})")
                    except Exception as e:
                        print(f"Error creating image from raw data for texture {texture_index}: {e}")
                        import traceback
                        traceback.print_exc()
                        pass
                else:
                    print(f"Warning: Pixel data size {len(pixel_data)} < expected {expected_rgba_size} for texture {texture_index}")
            
            if image.isNull():
                return None
            
            pixmap = QPixmap.fromImage(image)
            self.texture_cache[texture_index] = pixmap
            return pixmap
        except Exception as e:
            print(f"Error loading texture {texture_index}: {e}")
            return None
    
    def get_region_pixmap(self, asset_id: str) -> Optional[QPixmap]:
        """Get the pixmap for a specific asset ID.
        
        Args:
            asset_id: Asset/material ID (as string)
            
        Returns:
            QPixmap of the region, or None if not found
        """
        region = self.asset_id_to_region.get(asset_id)
        if not region:
            return None
        
        # Load the base texture if not cached
        if region.texture_index not in self.texture_cache:
            texture = self.load_texture(region.texture_index)
            if not texture or texture.isNull():
                print(f"Warning: Failed to load texture {region.texture_index} for asset {asset_id}")
                return None
        
        texture = self.texture_cache[region.texture_index]
        
        # Validate region bounds
        if region.x + region.width > texture.width() or region.y + region.height > texture.height():
            print(f"Warning: Region {asset_id} bounds ({region.x}+{region.width}, {region.y}+{region.height}) exceed texture {region.texture_index} size ({texture.width()}x{texture.height()})")
            return None
        
        # Extract the region
        try:
            region_pixmap = texture.copy(region.x, region.y, region.width, region.height)
            if region_pixmap.isNull():
                print(f"Warning: Failed to extract region for asset {asset_id}")
                return None
            region.pixmap = region_pixmap
            return region_pixmap
        except Exception as e:
            print(f"Error extracting region for asset {asset_id}: {e}")
            return None
    
    def get_region_info(self, asset_id: str) -> Optional[TextureRegion]:
        """Get texture region info for an asset ID."""
        return self.asset_id_to_region.get(asset_id)
    
    def __enter__(self):
        """Context manager entry."""
        self.open_jar()
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit."""
        self.close_jar()


# Global asset loader instance
_asset_loader: Optional[AssetLoader] = None


def get_asset_loader(jar_path: Optional[str] = None, force_reload: bool = False) -> Optional[AssetLoader]:
    """Get or create the global asset loader.
    
    Args:
        jar_path: Optional path to spacehaven.jar. If None, will auto-detect.
        force_reload: If True, reload even if loader already exists.
        
    Returns:
        AssetLoader instance or None if failed to load.
    """
    global _asset_loader
    if _asset_loader is None or force_reload:
        if force_reload and _asset_loader:
            _asset_loader.close_jar()
        _asset_loader = AssetLoader(jar_path)
        if _asset_loader.open_jar():
            if _asset_loader.load_texture_regions():
                print(f"Loaded {len(_asset_loader.texture_regions)} texture regions")
                print(f"Mapped {len(_asset_loader.asset_id_to_region)} asset IDs")
            else:
                print("Warning: Failed to load texture regions")
        else:
            print(f"Warning: Could not open JAR file: {_asset_loader.jar_path}")
            return None
    return _asset_loader

