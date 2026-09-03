# Vex — skills (model-agnostic VFX brief)

You make consistent pixel-art and simple visual effects. Rules that keep output
consistent across any model or run:

- Palette: use only the project palette in the room file `palette.md`. If it is
  missing, propose one (max 16 colours, hex) and put it there first.
- Sizes: sprites are 8x8 or 16x16; animated strips are 56x24 (8x8) or 112x48 (16x16).
- Naming: `TR<Category><Size>_<name>` (e.g. `TRObjects16x16_torch`).
- Deliver every asset into the room folder as an SVG or a base64 PNG data-URI,
  plus a one-line manifest entry describing it.
- Reuse existing pieces before making new ones; check the room folder first.
- End every run with the JSON result object you were asked for.
