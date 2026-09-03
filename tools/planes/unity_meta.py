"""Write Unity .meta files next to generated textures so import settings are
deterministic (point filter, no compression, Read/Write where the runtime
needs GetPixels32) instead of whatever the editor's defaults happen to be.

A .meta that already exists is never touched: its guid is what scenes and
prefabs reference, and Unity's own re-serialisation would only churn it.

kinds:
  sprite   Sprite (2D and UI), PPU 100, pivot centre, readable
  texture  Default texture, readable, sRGB (skin swatches - colour data)
  mask     Default texture, readable, linear, alpha not transparency (data)
  folder   folder asset
"""
import uuid
from pathlib import Path

_TEXTURE = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 11
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
  isReadable: 1
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 256
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: -1
    mipBias: -100
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: {sprite_mode}
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: {fallback_physics}
  alphaUsage: 1
  alphaIsTransparency: {alpha_transparency}
  spriteTessellationDetail: -1
  textureType: {texture_type}
  textureShape: 1
  singleChannelComponent: 0
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  applyGammaDecoding: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 256
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    physicsShape: []
    bones: []
    spriteID: {sprite_id}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
  spritePackingTag:
  pSDRemoveMatte: 0
  pSDShowRemoveMatteOption: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""

_FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
"""

KINDS = {
    "sprite": dict(texture_type=8, sprite_mode=1, srgb=1, alpha_transparency=1, fallback_physics=1),
    "texture": dict(texture_type=0, sprite_mode=0, srgb=1, alpha_transparency=1, fallback_physics=0),
    "mask": dict(texture_type=0, sprite_mode=0, srgb=0, alpha_transparency=0, fallback_physics=0),
}


def _guid():
    return uuid.uuid4().hex


def write_meta(asset_path, kind):
    """Create <asset>.meta if missing. Returns True when a file was written."""
    asset_path = Path(asset_path)
    meta = asset_path.with_name(asset_path.name + ".meta")
    if meta.exists():
        return False
    if kind == "folder":
        meta.write_text(_FOLDER.format(guid=_guid()))
        return True
    spec = KINDS[kind]
    meta.write_text(_TEXTURE.format(guid=_guid(), sprite_id=_guid(), **spec))
    return True


def ensure_folder(path):
    """mkdir -p plus a folder .meta for every directory created under Assets/."""
    path = Path(path)
    missing = []
    p = path
    while not p.exists():
        missing.append(p)
        p = p.parent
    path.mkdir(parents=True, exist_ok=True)
    for d in reversed(missing):
        if "Assets" in d.parts:
            write_meta(d, "folder")
