# Logos

Track currently draws simplified stand-in marks for Claude, Codex and Copilot (see
`Shell/Logos.cs`, same shapes as `design/track-mockup-v2.html`).

To use the official logos for your own copy:

1. Put single-color SVGs here as `claude.svg`, `codex.svg` and `copilot.svg` (16 × 16 viewBox works best).
2. Copy each SVG's path data into the matching method in `Shell/Logos.cs` (`Claude()`, `Codex()`,
   `Copilot()`), keeping it a single filled `Geometry` so it can be tinted.
3. Rebuild and run `Track.exe --render-icons <folder>` to check them at 16, 24 and 32 px.

The logos belong to Anthropic, OpenAI and GitHub. Keep them for personal use; do not redistribute them.
