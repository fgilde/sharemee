# P2 – Suche (Detail-Spec)

Status: Arbeitsstand, abgeleitet aus Master-Spec 2.5, 4.2 (`Coworkee.Search.Elasticsearch`), 7 (P2) und `docs/analysis/sharedien-backend.md` 11.1.

## 1. Ziel und Definition of Done

Suchseite mit Volltext, Filtern (13 Operatoren), Facetten, Sortierung, Cursor-Paging und Type-ahead über alle Knoten, die der Nutzer lesen darf; gespeicherte und teilbare Suchen; Reindex als Job mit Report. Index bleibt nach jeder Änderung (Knoten, Metadaten, Versionen, Rechte, Verschieben, Papierkorb) aktuell.

## 2. Schnitt

| Teil | Inhalt | Ort |
|---|---|---|
| P2a Suchindex | `Coworkee.Search`: `ISearchIndex` (Index anlegen mit Mapping, Bulk upsert/delete, Query, Suggest, Alias-Reindex), Query-Modell (Filter mit 13 Operatoren, Facetten, Sort, `search_after`-Cursor, Principal-Filter), Provider `Coworkee.Search.Elasticsearch`, Aspire-Ressource, Contract-Tests mit Testcontainers | coworkee |
| P2b Knotensuche | `Shareme.Search`: Dokument-Projektion je Knoten, Rechte-Materialisierung (Leser-Principals je Knoten), Indexierung über Outbox-Events und Queue `search`, Reindex-Job mit Report, `POST /api/v1/search`, `POST /api/v1/search/suggest`, Saved Searches mit Ressourcenrechten, Suchseite mit Facetten | shareme |

## 3. Index-Dokument (Knoten)

`id, tenantId, type, semantic, name, names.{lang}, parentId, pathIds[], depth, createdAt, modifiedAt, createdBy, readers[]` (Principal-Schlüssel `u:{userId}`, `g:{groupId}`), Asset-Felder `fileName, extension, mimeType, assetType, size, sha256, status, versionNumber, thumbnail`, Metadaten `meta.{key}` je Definition mit typisiertem Feld (`keyword` + `text` für Strings/Listen, `date`, `long`, `double`, `boolean`), Sprache als Suffix `meta.{key}.{lang}` für übersetzbare Felder, `fulltext` (Name, Dateiname, Volltext-Metadaten; Dokumentinhalt ab P3).

Rechte: Ein Knoten ist für einen Principal lesbar, wenn er auf einem Knoten der Vererbungskette eine Rolle mit `Nodes.Read` hat. Globale Leser (`Nodes.Read` ohne Ressource) sehen alles im Tenant, der Filter entfällt. Änderungen an Grants, Vererbung, Verschieben und Rollen-Permissions lösen eine Neuberechnung für den betroffenen Teilbaum aus.

## 4. Query

`SearchRequest { Term, Exact, Operator (And/Or), Filters[{Field, Operator, Values[], Lang?}], Facets[Field], Sort[{Field, Desc}], FolderId + Recursive, Types[], Size, Cursor }` mit Operatoren `HasValue, HasNoValue, Equals, NotEquals, Contains, DoesNotContain, FuzzyEquals, FuzzyContains, GreaterThanOrEquals, LessThanOrEquals, Between, IdentifierEquals, IdentifierNotEquals`. Antwort `{ Items[NodeDto + highlights], Total, Facets{field: [{value, count}]}, Cursor }`. Suggest: Präfix/fuzzy auf Name und Dateiname, 10 Vorschläge.

## 5. Indexierung

- Outbox-Handler auf `NodeChanged`, `AssetVersionCreated`, Rendition-Tag-Änderungen und Rechte-Events planen `IndexNodes(ids)` bzw. `IndexSubtree(rootId)` auf Queue `search`; Papierkorb/Purge entfernt Dokumente.
- `Reindex(tenant?)` baut einen neuen Index, befüllt ihn in Batches und schwenkt den Alias; Report mit Zählern und Fehlern.
- Mapping folgt den PropertyDefinitions (`Searchable`, `Facet`); Definitionsänderungen lösen einen Reindex aus.

## 6. Saved Searches

`SavedSearch { Name, Request (jsonb), OwnerId }` als Ressourcentyp `SavedSearch` (Teilen über ResourcePermissionsPanel mit Rolle `SavedSearch.Read`).

## 7. Nicht in P2

Dokumentinhalt-Extraktion (P3), Suche über KB/Intranet (P7), Relations-Suche (SQL, vorhanden), Feld-Restriktionen (P5).
