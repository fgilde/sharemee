# P3 – Verarbeitung komplett (Detail-Spec)

Status: Arbeitsstand, abgeleitet aus Master-Spec 5.3, 7 (P3) und `docs/analysis/sharedien-backend.md` 11.2.

## 1. Ziel und Definition of Done

Alle Sharedien-Renditions: Bilder, Video, Audio, Office, PDF, EPS, 3D; Wasserzeichen-Derivate; Zuschnitt (Crop) als Derivat und Variante; On-Demand- und manuell hochgeladene Derivate; technische Metadaten aus Dateien (Exif, IPTC, XMP, Video-Streams); Archiv-Entpacken in Ordner; Dateinamensregeln für Downloads; Zip-Download mehrerer Assets; Kontaktabzug (PDF); ein Processing-Container-Image mit allen Werkzeugen.

## 2. Schnitt

| Teil | Inhalt |
|---|---|
| P3a Renderer-Gerüst und Medien | `IRenditionRenderer` je Medienart mit Werkzeugpfaden aus Settings (`Processing:Tools:*`), fehlendes Werkzeug ⇒ `notsupported`; Video (FFmpeg: Poster-Frame als Thumbnail/Preview, MP4-H.264-Vorschau mit Größenlimit), Audio (FFmpeg: MP3-Vorschau, Wellenform-PNG), PDF/EPS/AI (Ghostscript: erste Seite), Office (LibreOffice ⇒ PDF ⇒ Ghostscript), 3D (Blender headless, optional); technische Metadaten (ExifTool/FFprobe ⇒ System-Properties `sys.*` im Katalog, nicht editierbar); Dockerfile für `shareme-processing` |
| P3b Derivat-Typen und Wasserzeichen | Derivat-Typen als Katalog (`DerivationType { Key, Name, Kind, Width, Height, Format, Quality, IsDefault, IsOnDemand, AllowManualUpload, Watermark? }`), On-Demand anfordern (`POST /assets/{id}/derivations/{type}`), manuell hochladen, Wasserzeichen-Definition (Bild + Position + Deckkraft, ausgelöst über Property/Listenwert), Download einer Rendition statt Original (`download?derivation=`), `AssetType.DownloadOriginalAlways` |
| P3c Crop und Varianten | Crop-Editor im Client (MudEx-Bildansicht mit Auswahlrahmen, feste Seitenverhältnisse), Crop als Derivat oder als neues Asset mit Edge `variant`, Metadaten der Quelle übernehmen |
| P3d Pakete und Archive | Zip-Download (Job, Dateinamensregel, Fortschritt per Realtime, ablaufender Link), Dateinamensregeln (`FileNamingDefinition` mit Tokens `{meta:key}`, `{name}`, `{ext}`, `{seq}`, `{date}`), Kontaktabzug als PDF, Archiv-Entpacken (Zip ⇒ Ordnerstruktur, Filter für `__MACOSX`, `.DS_Store`, `Thumbs.db`) |

## 3. Grundsätze

- Werkzeuge werden als Prozesse mit Zeitlimit, begrenzten Argumenten (keine Shell) und temporärem Arbeitsverzeichnis je Job gestartet; Eingaben kommen aus dem Storage in eine Temp-Datei, Ausgaben gehen als Blob zurück.
- Renderer melden `Supports(mimeType)`; der Job wählt den ersten passenden Renderer. Ohne Werkzeug kein Fehler, sondern `notsupported`, damit Installationen ohne LibreOffice funktionieren.
- Tests: Renderer-Tests laufen nur, wenn das Werkzeug gefunden wird (sonst übersprungen); die CI installiert FFmpeg, Ghostscript, ExifTool und LibreOffice.

## 4. Nicht in P3

KI-Tagging (P5), DRM-Wasserzeichen (P6), Video-Transcoding in mehrere Qualitätsstufen/Streaming (später).
