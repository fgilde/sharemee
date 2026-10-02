# P5 Automatisierung und Datenoperationen – Design

Ziel (Architektur-Spec §7): „Regeln laufen live und als Job mit Report“; Sharedien-Parität für Rules-Engine, Mass-Edit, Import/Export, KI-Tagging/Extraktion, Jobs-Dashboard und Restriktionen (Feld/Wert/Derivat).

## 1. Teilphasen

| Phase | Inhalt |
|---|---|
| **P5a Rules-Engine** | `Rule` je Mandant: Name, Knotentypen, Aktiv, Filter (Vorbedingungen), Trigger, Aktionen mit Then-/Else-Zweig. Operatoren: equals, not equals, greater/less (or equal), contains, regex (mit Timeout), has value, value changed, is new version, is new node, has relation. Auslösung live nach Metadaten-Änderung, neuer Version und neuem Knoten (Domain-Events, Outbox), keine Rekursion (von Regeln geschriebene Änderungen lösen keine Regeln aus). Aktion: Ziel-Property mit Template (`{var.year}` … `{var.second}`, `{fnc.find(key)}`, `{fnc.findInAscendants(key)}`, `{fnc.regexExtract(key, pattern)}`, Text), später KI-Extraktion (P5c). Regel-Job: Regeln nachträglich auf ausgewählte Knoten oder einen Ordner anwenden, mit Report (Treffer, ausgeführt, übersprungen, Fehler je Knoten). Admin-UI mit Editor und Probelauf an einem Knoten. |
| **P5b Mass-Edit, Import, Export** | Mass-Edit-Job über ausgewählte Assets oder ein Suchergebnis: Mutationen je Property mit Strategie (keep, overwrite, merge, delete), Rollenlimit für die Anzahl, Report je Asset. Export der Metadaten ausgewählter Assets oder eines Ordners als XLSX/CSV (eine Spalte je Property und Sprache). Import aus XLSX/CSV (Zuordnung über Asset-Id oder Dateiname im Ordner), Prüfung wie beim Speichern, Meldungen je Zeile/Spalte, Probelauf. |
| **P5c KI-Tagging** | `ExtractionDefinition`: Eingabe-Rendition, Anbieter (OpenAI-kompatibel, Anthropic, Azure AI Vision; Schlüssel als verschlüsselte Settings), Prompt/Modus (Tags, Beschreibung), Ziel-Properties und Mapping auf Listenwerte, Konfidenzschwelle, Sprache. Job über ausgewählte Assets oder als Regel-Aktion, Ergebnisse mit Konfidenz, Import automatisch oder nach Prüfung. Tests mit einem Fake-Anbieter. |
| **P5d Restriktionen und Jobs-Dashboard** | Rollenbasierte Blacklists: Property nicht lesen/schreiben, Assets mit einem Listenwert nicht sehen (Suche, Listen, Zugriff), Rendition-Typ nicht abrufen. Jobs-Dashboard: alle Shareme-Läufe (Entpacken, Mass-Edit, Import, Regel-Job, KI, Reindex) mit Status, Zählern, Report-Link; Link aufs Hangfire-Dashboard für Admins. |

## 2. Grundsätze

- Jobs laufen im Namen des auslösenden Users (Rechte gelten), schreiben Reports in eigene Tabellen und melden Fortschritt per Realtime.
- Regeln und Mass-Edit nutzen dieselbe Metadaten-Validierung wie die API; Fehler einzelner Knoten stoppen den Job nicht.
- Regex überall mit Timeout; Templates ohne Code-Ausführung.

## 3. Nicht in P5

DRM (P6), Wiki (P7), MCP/Assistent (P8).
