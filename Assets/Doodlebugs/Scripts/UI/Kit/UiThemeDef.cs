using UnityEngine;

public enum UiPanelShape { Rounded, Chamfer }
public enum UiShadowKind { None, Hard, Soft, Glow }
public enum UiBarStyle { Solid, Line, Blocks, Led, Tube, Hairline, Pill, Beam, Slant, Bamboo, Leaves }
public enum UiBadgeStyle { Disc, Ring, Square, Plate, Orb, Squircle, Halo, Hex, Ringed }
public enum UiBackdropFilter { None, Mono, Posterize, Grayscale, Blur }
public enum UiOverlay { None, Scanlines, Vignette, Bokeh, Facets, Blobs, Aurora, Grid, Vines }

/// <summary>Extra treatment painted on top of a panel face.</summary>
[System.Flags]
public enum UiFaceFx { None = 0, Gloss = 1, Clay = 2, Bevel = 4, Planks = 8 }

/// <summary>
/// One UI theme (Prompts/31): palette, fonts, shape and style switches.
/// Field for field the offline mirror is tools/ui_themes/tokens.py, where the
/// contrast gate runs - change a colour there first, then copy it here.
/// Ids are append-only (persisted, sent in telemetry); array index == id.
/// </summary>
public class UiThemeDef
{
    public int Id;
    public string Key;
    public string Name;
    public string LocalizedName => Loc.T("theme." + Key, Name);

    // --- colours. A "B" colour with alpha 0 = no gradient. ------------------
    public Color Line;          // outlines and hard shadows
    public Color Paper, PaperB;  // card face (inner display when CardInset > 0)
    public Color PaperDim;
    public Color Text, TextDim;  // text on Paper
    public Color Frame, FrameB;  // slots, gear, card rim when inset
    public Color FrameText;
    public Color OnBackdrop;     // text straight on the arena (title, pills, plane name)
    public Color BackdropShadow; // its drop shadow; alpha 0 = none
    public Color Weapon, Gadget, Special, Highlight, Danger;
    public Color Band, BandB;    // alpha 0 = band takes the category colour
    public Color BandText;
    public Color Confirm, ConfirmB, OnConfirm;
    public Color TabOn, TabOnB, TabOnText;
    public Color Glass, GlassLight;
    public Color VeilTop, VeilBottom;
    public Color Pick, OnPick;   // face of a selected / owned tile
    public Color BarFill;
    public Color ArtTint = Color.white;   // multiplies the plane and the icon glyphs

    // --- type -----------------------------------------------------------------
    public string FontDisplay, FontLabel, FontBody, FontStrong;
    public float ScaleDisplay = 1f, ScaleLabel = 1f, ScaleBody = 1f;
    public float TrackDisplay, TrackLabel;   // em

    // --- shape ----------------------------------------------------------------
    public UiPanelShape Shape = UiPanelShape.Rounded;
    public float RadiusScale = 1f;   // x Sky Poster radii (0 = square)
    public float Outline = 3f;
    public UiShadowKind Shadow = UiShadowKind.Hard;
    public float ShadowSkew;         // hard shadow x offset per unit of depth
    public Color ShadowColor;
    public UiFaceFx FaceFx;
    public float CardInset;          // > 0: card = Frame rim + Paper display inside
    public bool ArchCards;
    public float SelectLift = 8f, SelectTilt = -1.2f, SelectBob = 3f, SelectScale = 1f;

    public UiBarStyle Bar = UiBarStyle.Solid;
    public UiBadgeStyle Badge = UiBadgeStyle.Disc;
    public UiBackdropFilter Filter = UiBackdropFilter.None;
    public Color FilterTint = Color.white;
    public int FilterLevels = 5;
    public UiOverlay Overlay = UiOverlay.None;
    public Color OverlayA, OverlayB, OverlayC;

    /// <summary>Resources folder of the generated art (backdrop, wordmark, tiles).</summary>
    public string ArtPath => "Sprites/UiThemes/" + Key + "/";

    public bool HasGradient(Color b) => b.a > 0f;
}
