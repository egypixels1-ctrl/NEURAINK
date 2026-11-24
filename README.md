# NEURAINK — الخطاط الرقمي

A Windows app that learns Arabic calligraphy from a library of professional pieces and, step by step, composes new logos and phrases — output as editable vectors for Adobe Illustrator.

## Roadmap
1. **Library triage** — `tools/diwani_triage.py` sorts the library, cleans every usable piece to pure black on white, and writes PNG + SVG + `report.csv`. ✓
2. **Letter segmentation** — in the app: outline each letter, its dots and its entry / exit joins; every letter is cut into the **letter library**. ← now
3. **Composer** — assemble names from the letter library.
4. **Neural networks** — encoder, generator, layout and critic, trained on the letter library and on the user's choices.
5. **Illustrator output** — pure black on pure white AI files.

## Folders
```
<Library>\            made by tools/diwani_triage.py
  report.csv  clean_png\  svg\  thumbs\
<Workspace>\          (Settings › Workspace folder, default "NEURAINK Data" next to the library)
  annotations\00042.json        the segmentation of piece 42
  letters\0645\00042_01.png     a cut-out of «م» (transparent PNG)
  letters\0645\00042_01.json    its form, joins, polygons, source
```

## App
- WPF (.NET 10, Fluent theme), Arabic / English, dark (default) / light, accent colour.
- Settings are in `%APPDATA%\NEURAINK\settings.json`.

## Build
Every push to `main` builds the installer on GitHub Actions and publishes a release (**NEURAINK-Setup-x.y.z.exe**).
Screenshots of every page are pushed to the `screenshots` branch.

Local build: install the .NET 10 SDK, then `dotnet run --project src/Neuraink.App`.
