# Loader UI design contract

## Status

- Active baseline: the last verified compact loader UI.
- Scope: frontend only. Launching, Steam discovery, payload handling, and other backend behavior stay unchanged.
- Target: .NET 8 WPF, `win-x86`, fixed desktop window.

## Layout

- Window host: fixed transparent `520 × 430`; the visible shell starts at `220 × 220` and expands around its center to `496 × 406` inside the host.
- Root card: 12 px outer margin, 20 px corner radius, restrained dark shadow.
- Final composition: a top-aligned brand/status rail on the left; a centered NL/GS selector, two stacked selectable game cards, and the launch action on the right.
- The established compact alignment and control sizes from the working build are the source of truth.

## Visual language

- NL uses near-black midnight navy, cyan accent, and a restrained cool blue glow.
- GS uses near-black graphite/emerald surfaces, green accent, and a restrained emerald glow.
- Typography: Segoe UI Variable Display with Segoe UI fallback.
- Controls use 8–12 px radii; the window uses 20 px.
- The phoenix remains the only prominent brand mark.
- Discord uses the original white Discord glyph without a text label and opens `https://discord.gg/infinixleague`.

## Motion

- Startup shows only a compact 58 px ring spinner in the small shell, with no center dot; the phoenix is never shown during loading.
- After initialization, a single C# `Storyboard` animates the visible shell width and height together around the fixed center; the native transparent window is never resized.
- The loading overlay begins a 240 ms fade 120 ms after expansion starts and disappears well before the shell reaches its final size.
- Main content fades and scales in near the end of the expansion, after enough layout space exists, without directional translation or clipping.
- Two blurred ambient glows drift slowly behind the interface without affecting layout.
- Theme changes interpolate brushes smoothly.
- The profile indicator is always 94 px wide and only translates horizontally; it never animates its width.
- Primary and Discord buttons use subtle hover scaling only.
- Startup motion changes only the inner shell; theme/profile motion never changes window or page layout measurements.

## Explicit exclusions

- No acrylic, Mica, desktop capture, or native backdrop APIs.
- No repeated, profile-driven, or state-driven window resizing after the one startup expansion.
- No debug labels, sidebars, build metadata, extra branding, or diagnostic dashboards.
- No new UI dependencies.

## Verification

- Release build has no errors.
- Core smoke tests pass.
- Published single-file executable opens at the expected size with all controls visible and unclipped.
