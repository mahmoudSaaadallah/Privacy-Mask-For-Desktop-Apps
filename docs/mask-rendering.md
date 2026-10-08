# Mask Rendering

PrivacyMask treats privacy protection and visual appearance as separate
concerns. The overlay must hide protected pixels first; color and texture are
then drawn on top of that opaque surface.

## Security invariants

- Every protective style renders with overlay opacity `1.0` at every intensity.
- Surface intensity never reveals more of the protected application.
- Frosted glass is generated procedurally. PrivacyMask does not capture,
  sample, blur, or retain pixels from WhatsApp or Telegram.
- Only an explicit hover/hold reveal cutout, paused protection, or application
  exit can expose content through the overlay.
- Panic mode remains an opaque black full-window mask with reveal disabled.

These rules are enforced centrally by `MaskAppearancePolicy` and covered by
tests for all mask styles and representative intensity values.

## Surface intensity

The user-facing scale is 0–100%. Internally it is stored as `0.00–1.00`.
Intensity changes the procedural surface, not its transparency:

| Intensity | Frosted-glass appearance | Privacy opacity |
| --- | --- | --- |
| 0% | Light, soft frost with broad texture | 100% |
| 50% | Balanced color, shadow, and grain | 100% |
| 100% | Stronger contrast and finer frost | 100% |

A smoothstep curve removes abrupt changes near either end of the slider. In
particular, 100% no longer switches to a different solid-color renderer.

## Rendering and resource use

The frosted surface uses a small deterministic BGRA tile containing an opaque
base, three radial color clouds, and fine grain. The tile is generated in
memory, verified as opaque by automated tests, and frozen into a WPF image
brush. It has no animation, external bitmap, screen-capture buffer, or
per-frame noise generation. The tile and brush are rebuilt only when the
existing overlay render-state cache detects a relevant state change.

The reference wet-glass image is visual direction only. It is not included in
the repository or application. A richer water-droplet preset can be added later
as another procedural, fully opaque style after profiling its GPU and memory
cost on supported Windows versions.

## Settings compatibility

Settings schema version 7 converts legacy 0.15–2.40 darkness values to the
0.00–1.00 surface scale. The numeric enum value formerly named `Blur` is kept,
so existing JSON settings load as `Frosted glass` without user action.
