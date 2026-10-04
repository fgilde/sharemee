# P8 KI und MCP – Design

Ziel (Architektur-Spec §7): MCP-Server mit Tools aus allen Modulen, Admin-KI-Konfiguration, Assistent-Chat mit Tool-Aufrufen, Audit der Tool-Aufrufe, Paket `Coworkee.Ai`. Abnahme: der Assistent legt per Chat Ordner an, sucht Assets, setzt Metadaten – nur im Rahmen der Rechte des Users.

## 1. Teilphasen

| Phase | Inhalt |
|---|---|
| **P8a Coworkee.Ai** (Coworkee) | Werkzeug-Registry: Module tragen `AiTool`s bei (Name, Beschreibung, JSON-Schema aus dem Request-Typ via `JsonSchemaExporter`, optional benötigtes Recht); ein Tool ist meist ein Dispatcher-Request (Command/Query), sodass dieselben Rechte- und Ressourcenprüfungen gelten wie in der API. Einstellungen (Settings-Modul, global/Mandant): KI an/aus, Modell (Standard `claude-opus-5-5`), API-Schlüssel (Secret), maximale Tool-Runden. Chat-Endpunkt `POST /api/v1/ai/chat` (Recht `Ai.Chat`): Verlauf aus Text-Turns, Tool-Schleife mit dem offiziellen Anthropic-C#-SDK (manuelle Schleife, alle Tool-Ergebnisse eines Turns in einer User-Nachricht, Fehler als `is_error`, `refusal`/`max_tokens` behandelt), Antwort mit Text und Liste der Tool-Aufrufe. Audit `AiToolCall` (User, Mandant, Kanal chat/mcp, Tool, Eingabe gekürzt, Erfolg, Fehler, Dauer, Zeit) mit Abfrage `GET /api/v1/ai/tool-calls` (Recht `Ai.Audit`). MCP-Server unter `/mcp` (offizielles `ModelContextProtocol.AspNetCore`, Streamable HTTP): `tools/list` zeigt nur Tools, deren Recht der User hat; `tools/call` läuft als der angemeldete User, auditiert mit Kanal mcp. |
| **P8b Shareme-Tools und Chat-UI** (Shareme) | Tools: Suche (Inhalte), Knoten lesen, Kinder auflisten, Ordner anlegen, Metadaten lesen/setzen, Seite lesen/anlegen, News-Feed, Personen suchen, Lizenzangebote (wenn erlaubt). Chat-Seite `/assistant` mit Verlauf (im Browser), Anzeige der Tool-Aufrufe und Links auf angelegte/gefundene Knoten; Audit-Ansicht für Admins. |

## 2. Grundsätze

- Der Assistent kann nichts, was der User nicht selbst kann: jedes Tool läuft über den Dispatcher als der User (Rechte, Restriktionen, Validierung, Audit wie bei direkten API-Aufrufen).
- Tool-Ergebnisse sind Daten, keine Anweisungen; der Systemprompt sagt das, Ergebnisse werden gekürzt (Größenlimit).
- Schlüssel liegen verschlüsselt in den Settings und verlassen den Server nie.

## 3. Nicht in P8

MCP-Resources und -Prompts (folgen, wenn ein Client sie braucht), Streaming der Chat-Antwort, serverseitig gespeicherte Unterhaltungen.
