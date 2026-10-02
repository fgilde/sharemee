# P3a Renderer und Medien – Plan

Spec: `docs/superpowers/specs/2026-10-02-p3-processing-design.md` Abschnitt 2 (P3a) und 3.

- `ProcessingTools` findet FFmpeg, FFprobe, Ghostscript, LibreOffice und ExifTool über `Processing:Tools:{Name}` oder den PATH; `ToolRunner` startet sie ohne Shell mit Zeitlimit und Prozessbaum-Kill.
- `IRenditionSource` je Medienart liefert ein Standbild (PNG) für Thumbnail und Preview und für Video/Audio eine abspielbare Kopie (Derivat-Art `media`: MP4 720p H.264 bis zehn Minuten, MP3 128 kbit/s bis 30 Minuten); Video: Frame bei 1 s, Audio: Wellenform, PDF/EPS/AI: erste Seite über Ghostscript `-dSAFER`, Office/Text: LibreOffice nach PDF mit eigenem Profil je Aufruf.
- Ohne Werkzeug: `notsupported`; Werkzeugfehler: `failed` mit Meldung; fehlendes Original: Job wird wiederholt.
- Technische Metadaten (`AssetVersion.TechnicalMetadata`, jsonb): ExifTool `-json -n -G` ohne Datei- und Binärfelder, sonst FFprobe für Medien bzw. Bildgröße aus Magick; Anzeige im Tab „Technical“. Spiegelung in den Metadaten-Katalog als System-Properties folgt, wenn Suche und Regeln sie brauchen.
- Neue Dateitypen: EPS, AI, ODT, ODS, ODP, RTF.
- Container-Image `src/Shareme.Processing.Host/Dockerfile` mit allen Werkzeugen; CI installiert sie per apt; Renderer-Tests werden ohne Werkzeug übersprungen.
