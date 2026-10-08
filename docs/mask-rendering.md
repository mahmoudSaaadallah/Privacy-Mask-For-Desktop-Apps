# Mask Rendering

PrivacyMask offers progressive live blur for everyday shoulder-surfing
protection and opaque modes for cases where no underlying detail may remain.

## Behavior guarantees

- Live blur samples the selected application window directly. Lower strength
  intentionally reveals progressively clearer shapes and colors, while higher
  strength removes more detail.
- Wet glass follows the same progressive blur scale and adds transparent
  condensation droplets. It is an appearance option, not a stronger security
  boundary.
- Captured frames remain in process memory, are never written to disk or sent
  over the network, and are released when the overlay is hidden or closed.
- Solid redact remains fully opaque when progressive visibility is unsuitable.
- Only an explicit hover/hold reveal cutout, paused protection, or application
  exit exposes an entirely unfiltered region.
- Panic mode remains an opaque black full-window mask with reveal disabled.
- If target-window capture fails, the live-blur region uses a seamless opaque
  gradient rather than exposing the application.

Appearance mapping is centralized in `MaskAppearancePolicy` and covered by
tests for all mask styles and representative strength values.

## Blur strength

The user-facing scale is 0–100%. Internally it is stored as `0.00–1.00`.
Strength controls the retained spatial detail and adds a restrained tint:

| Strength | Live-blur appearance | Typical use |
| --- | --- | --- |
| 0% | Nearly clear window snapshot with a 4% tint | Visual continuity; minimal concealment |
| 50% | Strong blur with recognizable broad colors and shapes | Everyday privacy |
| 100% | Heavily downsampled color blocks with a 36% tint | Maximum blur; use solid redact for zero detail |

A smoothstep curve maps the slider continuously from 1x to 32x downsampling,
so there is no renderer switch or abrupt color jump at either endpoint.

## Capture, rendering, and resource use

`PrintWindow` captures the protected HWND itself, so an unrelated window above
it is not copied into the blur frame. Capture runs away from the UI thread at a
maximum cadence of one frame per 250 ms. The retained long edge is capped at
1600 pixels before applying the strength-dependent downsampling factor. WPF
then scales the result with high-quality interpolation and clips it to the same
privacy-zone, reveal, and desktop-occlusion geometry as the other styles.

The renderer updates the source of existing image elements between frames
instead of rebuilding the overlay tree. It skips capture while one unrelated
window fully covers the target. Native bitmap handles are released after each
frame, and hiding or closing an overlay discards the retained frame.

Wet glass generates a deterministic transparent 640x448 condensation texture
once in memory. More than one hundred small droplets and a few short streaks
are rasterized into that single shared image, so WPF does not retain a large
vector tree or expose visible tile seams. The reference image remains visual
direction only and is not included in the repository or application. Wet-glass
captures refresh every 500 ms; ordinary live blur remains at 250 ms.

## Settings compatibility

Settings schema version 7 converts legacy 0.15–2.40 darkness values to the
0.00–1.00 strength scale. The numeric enum value for `Blur` is unchanged, so
existing JSON settings load as `Live blur` without user action.
