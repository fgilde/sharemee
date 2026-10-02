# P3b Derivat-Typen und Wasserzeichen – Plan

Spec: `docs/superpowers/specs/2026-10-02-p3-processing-design.md` Abschnitt P3b.

- `DerivationType` je Mandant: Schlüssel (nicht `thumbnail`, `preview`, `media`, `still`), maximale Breite/Höhe, Format WebP/JPEG/PNG, Qualität, Asset-Typen (leer = alle), Modi „immer“ (`IsDefault`, beim Planen jeder neuen Version), „auf Anfrage“ (`POST /assets/{id}/derivations/{key}`, Download-Recht) und „von Hand“ (`PUT …/{key}`, Update-Recht, nur PNG/JPEG/WebP nach Signatur, bleibt beim Rerender erhalten).
- `WatermarkDefinition`: PNG, Position (Gravity), Deckkraft, Größe in Prozent der Breite, optional Auslöser Property + Wert (Listenschlüssel oder `true`); wird im Render-Job über die Rendition gelegt.
- Nicht-Bild-Quellen legen ihr Standbild als internes Derivat `still` (PNG) ab; spätere Renditions (auf Anfrage) brauchen weder Original noch Werkzeug. `still` wird nie ausgeliefert oder gelistet.
- Renditions sind für Leser abrufbar (Wasserzeichen schützen sie), `?download=true` liefert als Anhang.
- Originale sicherer Typen (`InlineTypes`: PDF, Rasterbilder, MP4/WebM, Audio, Text/CSV) per `GET /assets/{id}/view` inline mit `nosniff` und CSP `sandbox; default-src 'none' …`; im Asset-Detail per `MudExFileDisplay` (PDF und Text standardmäßig, Bilder/Video per Umschalter). `AssetDto.CanDownload` steuert Download-Button und Ansicht.
- Admin-Seite `/admin/renditions` (Node-Admin), Tab „Renditions“ im Asset-Detail.
