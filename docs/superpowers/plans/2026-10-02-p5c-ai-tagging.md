# P5c KI-Tagging – Plan

Spec: `docs/superpowers/specs/2026-10-02-p5-automation-design.md` (P5c); Sharedien-Vorbild: PropertyExtractionDefinition (Eingabe-Rendition, Asset-Typen, Anbieter, Schwelle, Sprache, Mapping Tag → Listenwert), nur Auto-Tagging.

- `ExtractionDefinition` je Mandant: Anbieter OpenAI-kompatibel (Chat Completions mit Bild), Anthropic (Messages API, Standardmodell `claude-opus-5-5`), Azure AI Vision (Image Analysis 4.0 tags/caption); Endpoint nur https; API-Schlüssel per Data Protection verschlüsselt, nie ausgeliefert (`HasApiKey`). Modus Tags oder Beschreibung, Prompt-Zusatz, Eingabe-Rendition, Asset-Typen, Ziel-Property (ohne Kontext), Mappings, Schwelle 0..1, Sprache, Auto-Übernahme.
- Lauf als `DataJob` (Art extraction) im Namen des Startenden: lesbare Assets mit fertiger Rendition; frische Uploads ohne Rendition laufen in einem später eingeplanten Folgelauf weiter (neu: `IBackgroundJobs.Schedule` in Coworkee), statt einen Worker zu blockieren. Tags über der Schwelle → Mapping → Listenwert (Key oder Name) → sonst frei, wenn erlaubt; Mehrfach-Properties bekommen alle, einfache den besten (Text: kommagetrennt); Beschreibung → ein Wert in der Datensprache.
- Ergebnis: Auto-Übernahme über `SetNodeMetadata` (Quelle `ai`, löst keine Regeln aus) oder `ExtractionSuggestion` (eine offene je Knoten und Definition) zur Prüfung; Übernehmen schreibt als prüfender User (Merge), Ablehnen verwirft.
- Regel-Aktion `ExtractionId`: startet die Extraktion für den Knoten (live und im Regel-Job).
- Rechte `Automation.Ai` (Konfiguration) und `Automation.Ai.Run` (Läufe, Vorschläge).
- UI: `/admin/ai` (Definitionen, Vorschläge prüfen), Browse-Auswahl „AI tagging“, Regel-Editor-Aktion „AI: …“, Läufe in `/data-jobs`.
- Tests mit einem Fake-HTTP-Handler für die drei Anbieter.
