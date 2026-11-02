// SPDX-License-Identifier: GPL-3.0-or-later
// WinKnife — Windows port of Vorssaint (https://github.com/vorssaint/vorssaint-utils)

using WinKnife.Core;

namespace WinKnife.Features.Capture;

/// <summary>English strings of the recorder editor (verbatim from RecorderFeatureStrings / RecorderExportStrings).</summary>
internal static class RS
{
    public static  string Look => Loc.T("Look");
    public static  string LookRaw => Loc.T("Original");
    public static  string LookClean => Loc.T("Smooth");
    public static  string LookStudio => Loc.T("Studio");
    public static  string Pointer => Loc.T("Pointer");
    public static  string PointerShow => Loc.T("Draw the pointer");
    public static  string PointerSmoothing => Loc.T("Smoothing");
    public static  string SmoothingOff => Loc.T("None");
    public static  string SmoothingLight => Loc.T("Light");
    public static  string SmoothingSmooth => Loc.T("Smooth");
    public static  string SmoothingCinematic => Loc.T("Cinematic");
    public static  string PointerSize => Loc.T("Size");
    public static  string ClickRing => Loc.T("Mark where you click");
    public static  string Zoom => Loc.T("Zoom");
    public static  string ZoomToggle => Loc.T("Zoom in on every click");
    public static  string ZoomAmount => Loc.T("How close");
    public static  string Background => Loc.T("Background");
    public static  string Shape => Loc.T("Shape");
    public static  string ShapeOriginal => Loc.T("Original");
    public static  string ShapeWide => Loc.T("Wide");
    public static  string ShapeSquare => Loc.T("Square");
    public static  string ShapeVertical => Loc.T("Tall");
    public static  string NoPointerNote => Loc.T("This recording has no pointer track, so there is nothing to smooth. Zooms placed by hand still work.");
    public static  string ZoomLaneEmptyHint => Loc.T("Click here to add a zoom");
    public static  string AddZoom => Loc.T("Add a zoom");
    public static  string ThisZoom => Loc.T("This zoom");
    public static  string ZoomWhere => Loc.T("Where it looks");
    public static  string ZoomFollows => Loc.T("Follows the pointer");
    public static  string ZoomPickSpot => Loc.T("Pick a spot");
    public static  string ZoomPickSpotHint => Loc.T("Click the picture to aim it");
    public static  string RegenerateZooms => Loc.T("Back to one per click");
    public static  string BackToOptions => Loc.T("All options");
    public static  string CutOut => Loc.T("Cut out");
    public static  string AddText => Loc.T("Add text");
    public static  string TextLaneEmptyHint => Loc.T("Click here to add text");
    public static  string ThisText => Loc.T("This text");
    public static  string TextPlaceholder => Loc.T("Your text here");
    public static  string TextLabel => Loc.T("Text");
    public static  string Size => Loc.T("Size");
    public static  string Position => Loc.T("Position");
    public static  string Colour => Loc.T("Colour");
    public static  string Remove => Loc.T("Remove");
    public static  string PresetsButton => Loc.T("Presets");
    public static  string SavePreset => Loc.T("Save current preset…");
    public static  string PresetNamePlaceholder => Loc.T("Preset name");
    public static  string RemovePreset => Loc.T("Remove preset");
    public static  string Save => Loc.T("Save");
    public static  string Cancel => Loc.T("Cancel");
    public static  string Done => Loc.T("Done");
    public static  string ZoomEmptyTitle => Loc.T("No zooms yet");
    public static  string ZoomEmptyCaption => Loc.T("Create them from your clicks or add one on the timeline.");
    public static  string CreateAutomaticZooms => Loc.T("Create automatic zooms");
    public static  string TypingZoomToggle => Loc.T("Keep zoomed in while typing");
    public static  string TypingZoomCaption => Loc.T("After a click, typing keeps the automatic zoom on that spot.");
    public static  string BlurLane => Loc.T("Blur");
    public static  string AddBlur => Loc.T("Blur an area");
    public static  string BlurLaneEmptyHint => Loc.T("Click here to add a blur");
    public static  string ThisBlur => Loc.T("This blur");
    public static  string BlurPickArea => Loc.T("Choose the area");
    public static  string BlurPickAreaHint => Loc.T("Drag over what should stay hidden");
    public static  string BlurCaption => Loc.T("Hidden for as long as its block lasts on the timeline.");
    public static  string BlurStrength => Loc.T("Blur strength");
    public static  string AddImage => Loc.T("Add image");
    public static  string ImageLane => Loc.T("Image");
    public static  string ImageLaneEmptyHint => Loc.T("Click here to add an image");
    public static  string ThisImage => Loc.T("This image");
    public static  string ImageSize => Loc.T("Size");
    public static  string ImageOpacity => Loc.T("Opacity");
    public static  string ImageImportFailed => Loc.T("Couldn’t add this image.");
    public static  string Margin => Loc.T("Margin");
    public static  string Corners => Loc.T("Corners");
    public static  string Blur => Loc.T("Blur");
    public static  string Quality => Loc.T("Quality");
    public static  string QualitySmall => Loc.T("Small file");
    public static  string QualityBalanced => Loc.T("Balanced");
    public static  string QualityHigh => Loc.T("High");
    public static  string ExportSpeed => Loc.T("Export speed");
    public static  string CustomSpeed => Loc.T("Custom speed");
    public static  string ExportDuration => Loc.T("Export duration");
    public static  string SpeedNote => Loc.T("Applies to video, GIF and shared links. The editing preview stays at 1×; the original recording is unchanged.");
    public static  string SystemAudioTrack => Loc.T("PC sound");
    public static  string MicrophoneTrack => Loc.T("Microphone");
    public static  string Volume => Loc.T("Volume");
    public static  string Restore => Loc.T("Restore");
    public static  string None => Loc.T("None");
    public static  string RevealInExplorer => Loc.T("Reveal in File Explorer");
}
