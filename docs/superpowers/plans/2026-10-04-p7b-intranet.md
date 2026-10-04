# P7b News, Favoriten, Startseite – Plan

Spec: `docs/superpowers/specs/2026-10-04-p7-knowledge-intranet-design.md` (P7b).

- News (Knotentyp aus P7a, Inhalt in `NodeContent`): anlegen (Create auf dem Ordner, nur offene Container), lesen, ändern (Update); Feed `/api/v1/news` der veröffentlichten (ab ≤ jetzt < bis), lesbaren News ohne verborgene Listenwerte, angeheftete zuerst, mit Anriss.
- Favoriten je User (`Favorite`, Leserecht zum Setzen), Liste nur noch erreichbarer Knoten.
- Startseite: `HomeLayout` je User oder Mandant (Standard setzen nur Admins), Widgets search, news, favorites, tasks, due, recent; Daten `/api/v1/home/recent` (zuletzt geänderte lesbare Knoten) und `/api/v1/home/tasks` (offene eigene Aufgaben).
- UI: Startseite mit Widgets und Anpassen, `/news/{id}`, Favoriten-Stern in Browse, Seite und News, Browse „New news“, gemeinsame Links/Icons je Semantik.
