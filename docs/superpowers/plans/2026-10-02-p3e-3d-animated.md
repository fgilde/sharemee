# P3e 3D-Modelle und animierte Video-Thumbnails – Plan

Nachtrag zu P3a (Spec Abschnitt 2: „3D (Blender headless, optional)“, Sharedien `Thumbnail_Video_Animated`, `Thumbnail_3D`/`Screen_3D`).

- Neuer Asset-Typ `model` mit Dateitypen glb, gltf, stl, obj, fbx, 3mf, ply.
- `ModelSource`: Blender `-b --factory-startup` mit eingebettetem Skript (Szene leeren, importieren mit Fallback auf ältere Operator-Namen, Kamera auf die Meshes, Sonne, Cycles CPU 16 Samples, 1600 × 1200 PNG transparent) liefert das Standbild für Thumbnail, Preview und eigene Typen. Ohne Blender `notsupported`; CI installiert Blender, das Container-Image bleibt ohne (Größe).
- Originale von glb, stl, obj, 3mf, ply sind `InlineTypes` und öffnen im Asset-Detail standardmäßig im MudEx-3D-Viewer (`MudExFileDisplay`).
- Video bekommt die System-Rendition `animated`: GIF, je Sekunde der ersten 16 Sekunden ein Bild, 0,75 s Anzeige, 240 px, eigene Palette.
