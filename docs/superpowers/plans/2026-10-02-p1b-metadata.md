# P1b Metadaten – Plan

Spec: `docs/superpowers/specs/2026-10-02-p1-graph-metadata-assets-design.md` Abschnitt 4.

## Schnitt

- Neues Projekt `Shareme.Metadata` (hängt an `Shareme.Graph`), Tabellen im Schema `sm`, alle Katalog-Entitäten `IMultiTenant`.
- `Node.Metadata` (jsonb) kommt in `Shareme.Graph`: `{ "<key>": [ { "lang", "ctx", "v", "list" } ] }`.
- Katalog: `PropertyGroup`, `PropertyDefinition` (inkl. `ListValues` als eigene Tabelle `PropertyListValue`), `PropertySet` (Items und Conditions als jsonb), `DataLanguage`, `UserContext`.
- Ohne eigene Sprachen gilt die virtuelle Sprache `en` (Default), ohne eigene Kontexte der virtuelle Kontext `default`. GET schreibt nichts.
- Permission `Metadata.Manage` für den Katalog; Lesen des Katalogs für alle Angemeldeten; Werte lesen = `Nodes.Read`, schreiben = `Nodes.Update` am Knoten.

## API

- `GET/POST/PUT/DELETE /api/v1/metadata/{groups|definitions|languages|contexts|sets}`, Listenwerte über `PUT /api/v1/metadata/definitions/{id}/list-values` (ganzer Satz).
- `GET /api/v1/nodes/{id}/metadata` liefert die effektiven Werte, geerbte Werte mit `InheritedFrom`.
- `PUT /api/v1/nodes/{id}/metadata` ersetzt den ganzen Satz, `PATCH` setzt einzelne Keys (`null` entfernt).

## Validierung

Unbekannter oder für den Knotentyp nicht geltender Key, nicht editierbar oder System, Sprache/Kontext Pflicht oder verboten und bekannt, Einzelwert je Sprache/Kontext, Listenwert existiert und aktiv (außer `AllowCustomValues`), Werttyp (String, Text, RichText, Url, Date, DateTime, Time, Integer, Decimal, Boolean, Duration, Reference auf Knoten im Tenant), Regex, Min/Max (Zahl oder Textlänge), Required (PUT: alle geltenden Definitionen, PATCH: berührte Keys; geerbter Wert zählt).

## Vererbung

Für Definitionen mit `InheritToChildren` ohne eigenen Wert gilt der Wert des nächsten Vorfahren mit Wert.

## UI

- Tab „Metadata“ in Browse: Formular aus Definitionen (gruppiert, Set nach Default/Conditions), Speichern per PUT, geerbte Werte als Platzhalter.
- Admin-Seite „Metadata“: Gruppen, Definitionen mit Listenwerten, Sprachen, Kontexte.

## Nicht in P1b

System-Properties-Spiegelung (P1c mit AssetVersion), Feld-Restriktionen (P5), Mass-Edit (P5), Suche/Facetten (P2).
