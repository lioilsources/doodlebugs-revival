"""Write Unity .meta files next to generated textures so import settings are
deterministic (point filter, no compression, Read/Write where the runtime
needs GetPixels32) instead of whatever the editor's defaults happen to be.

A .meta that already exists is never touched: its guid is what scenes and
prefabs reference, and Unity's own re-serialisation would only churn it.

kinds:
  sprite   Sprite (2D and UI), PPU 100, pivot centre, readable
  texture  Default texture, readable, sRGB (skin swatches - colour data)
  mask     Default texture, readable, linear, alpha not transparency (data)
  audio    AudioClip, 44.1 kHz, decompress on load (the Resources/Sfx one-shots)
  folder   folder asset

write_meta(path, "sprite", pivot=(0.6, 0.5)) switches the importer to a custom
pivot (alignment 9); omitting `pivot` keeps the centred default byte for byte,
which is what tools/planes and tools/skins already ship.
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
  alignment: {alignment}
  spritePivot: {{x: {pivot_x}, y: {pivot_y}}}
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

_AUDIO = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 0
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


def write_meta(asset_path, kind, pivot=None):
    """Create <asset>.meta if missing. Returns True when a file was written.

    pivot: None (default) keeps the centred pivot / alignment 0 that every
    existing caller ships; a (x, y) pair in 0..1 sprite space switches the
    importer to Custom alignment - projectile bombs want the pivot near the
    nose so Bullet's tumble rotates around the fuse, not the middle."""
    asset_path = Path(asset_path)
    meta = asset_path.with_name(asset_path.name + ".meta")
    if meta.exists():
        return False
    if kind == "folder":
        meta.write_text(_FOLDER.format(guid=_guid()))
        return True
    if kind == "audio":
        meta.write_text(_AUDIO.format(guid=_guid()))
        return True
    spec = dict(KINDS[kind])
    px, py = (0.5, 0.5) if pivot is None else pivot
    spec.update(alignment=0 if pivot is None else 9, pivot_x=px, pivot_y=py)
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
