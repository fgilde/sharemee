# UX-Konzept Sharemee

Stand 2026-10-15. Grundlage: Rundgang mit Playwright als Administrator (`admin@sharemee.local`) und als normaler Benutzer (`user@sharemee.local`, Rolle *Member*), hell/dunkel, Desktop (1600 px) und Telefon (390 px), leeres System plus Ordner, Uploads, Workspace mit Mitglied. Nur Konzept, noch nicht umgesetzt.

Legende: **Prio** P1 (zuerst) bis P3, **Aufwand** S (≤ 1 Tag), M (2–4 Tage), L (> 1 Woche). **[Lib]** gehört in Coworkee, nicht in Sharemee.

## Befund in einem Satz

Jede Seite erklärt sich gut (Beschreibungen, leere Zustände), aber Sharemee lässt Neue mit einer Sammlung gleichrangiger Formulare allein: Es gibt keinen geführten Weg vom leeren System zu „Dateien liegen strukturiert da und sind mit den richtigen Leuten geteilt“, und auf jeder Seite stehen alle Optionen gleichzeitig offen.

## Screenshots

| # | Datei | Was man sieht |
|---|---|---|
| 1 | [01-start-leer.png](assets/ux-review/01-start-leer.png) | Startseite eines neuen Systems: fünf leere Kacheln, kein „Was jetzt?“ |
| 2 | [02-browse-auswahl.png](assets/ux-review/02-browse-auswahl.png) | Browse mit Auswahl: zehn Aktionen in einer Zeile, Beschriftungen abgeschnitten („DOWNLO“, „EXPOR“), Upload-Liste bleibt stehen, Ordnerbaum-Spalte leer (Fehler, behoben in `270218a`) |
| 3 | [03-metadaten-formular.png](assets/ux-review/03-metadaten-formular.png) | Neue Eigenschaft: 16 Felder und 10 Schalter auf einmal, unter einer leeren Tabelle |
| 4 | [04-telefon-browse.png](assets/ux-review/04-telefon-browse.png) | Telefon: App-Leiste schiebt Benutzermenü hinaus, Kopf-Aktionen und Eingaben abgeschnitten |
| 5 | [05-workspaces-benutzer.png](assets/ux-review/05-workspaces-benutzer.png) | Benutzer sieht zwei gleichnamige Workspaces, nur einer davon ist seiner; Anlegen-Formular immer offen |

## 1. Was zu kompliziert ist

1. **Einrichtungsreihenfolge steht als Fließtext auf vier Seiten.** Lizenzverwaltung (6 Schritte), Regeln (4), KI-Tagging (4) und Metadaten („start here“) beschreiben eine Reihenfolge, die der Admin selbst über Seiten hinweg abarbeiten muss. Abhängigkeiten (Eigenschaft fehlt → Regel nicht sinnvoll) werden erst beim Scheitern sichtbar.
2. **Alles ist immer offen.** Neue Eigenschaft, neue Regel, neue Lizenz, neuer Workspace, neues Format, neue Einschränkung: jeweils ein großes Inline-Formular unter der Liste, auch wenn man nur lesen will. Auf Metadaten konkurrieren 10 Checkboxen gleichrangig (Bild 3).
3. **Browse ist Werkzeugkasten statt Ordner.** Kopf mit sieben unbeschrifteten Icons, darunter „New folder“-Feld + drei Buttons + Filter + Ansicht, darunter Upload-Fläche, darunter Liste, darunter drei Tabs (Metadaten/Rechte/Aktivität) des *aktuellen Ordners*. Die eigentliche Arbeit (Dateien) steht erst in der Mitte.
4. **Auswahlleiste mit zehn Aktionen** (Verschieben, Download, Lizenz, Sammlung, Kontaktbogen, Metadaten, KI, Export, Löschen, Leeren) ohne Gruppierung; bei 1600 px schon abgeschnitten (Bild 2).
5. **Teilen ist an drei Stellen verschieden gelöst:** Share-Link am Asset (zeitlich begrenzte URL), Share-Links im Workspace (Name, Ablauf, E-Mails, Formate, Originale), Rechte-Tab am Ordner. Wer „das hier mit Frau X teilen“ will, muss wissen, welches der drei Konzepte passt.
6. **Zwei Suchen** im Menü (Quick search, File search) plus Suchfeld auf der Startseite plus Filter in Browse: vier Einstiege, Unterschied erklärt nur der Untertitel.
7. **Rechte sind schwer vorherzusagen.** Rolle (global) vs. Recht am Ordner vs. Workspace-Rolle vs. Einschränkung (Restrictions). Folge im Rundgang: Ein Member mit globalem Lesen sieht unter *My workspaces* auch Workspaces, in denen er nicht Mitglied ist (Bild 5).
8. **Kleine Reibungen:** „Create“ im Workspace bleibt deaktiviert, bis das Namensfeld den Fokus verliert (fehlendes `Immediate`); Upload-Fortschritt bleibt nach Abschluss stehen („Clear finished“); Bildvorschau hatte Höhe 0 (behoben in `3929534`).

## 2. Konkrete Vereinfachungen

| # | Vorschlag | Prio | Aufwand | Bausteine |
|---|---|---|---|---|
| V1 | **„Neu“-Knopf statt Inline-Formulare**: Listen zeigen nur die Liste + Primärknopf „Neue Eigenschaft/Regel/Lizenz/…“. Anlegen und Bearbeiten im seitlich einfahrenden Dialog. | P1 | M | `MudExObjectEdit` im Dialog (`ShowEx`, Slide-Animation, `MudExCss.Classes.Dialog._Initial`), wie im alten Template |
| V2 | **Browse entschlacken**: Kopf = Titel + Breadcrumb + *ein* Knopf „Neu ▾“ (Ordner, Seite, News, Dateien hochladen) + Suche. Upload-Fläche nur bei leerem Ordner groß, sonst Drag & Drop auf die ganze Liste. Ordner-Metadaten/Rechte/Aktivität in ein Info-Panel rechts (ausklappbar) statt Tabs unter der Liste. | P1 | M | `MudExSplitter`/`MudExSplitPanel` für Baum ↔ Liste ↔ Info, `MudExUploadEdit`, `MudExFileManager` als Vorbild |
| V3 | **Auswahlleiste gruppieren**: 3 Primäraktionen sichtbar (Herunterladen, Teilen, Verschieben), Rest im „Mehr“-Menü nach Gruppen (Bearbeiten: Metadaten, KI · Lizenz: Lizenz, Sammlung · Export: Kontaktbogen, Export · Löschen). Kontextmenü (Rechtsklick) mit denselben Einträgen. | P1 | S | `MudMenu`, `MudExIconButton` |
| V4 | **Ein Teilen-Dialog** für Datei, Auswahl und Ordner: „Mit Personen“ (Recht setzen, Personen/Gruppen-Picker), „Per Link“ (Ablauf, Formate, Originale, optional E-Mails). Die drei heutigen Wege werden zu Tabs eines Dialogs. | P1 | M | Personen-Picker aus Workspace-Mitgliedern, `MudExObjectEdit` |
| V5 | **Eine Suche**: Suchfeld in der App-Leiste (Quick search als Dropdown mit Treffern + „Alle Ergebnisse“), *File search* wird „Erweiterte Suche“ aus diesem Dropdown. Menüpunkt *Quick search* entfällt. | P2 | M | Lib-App-Bar-Slot, `MudExToggleableSearch` |
| V6 | **Metadaten-Formular stufen**: Pflicht = Key (aus Name vorgeschlagen), Name, Typ, gilt für. „Erweitert“ (aufklappbar) = Muster, Min/Max, Sprache, Audience, Vererbung, Versionen, Suche. Typ wählt sinnvolle Schalter vor (Liste → „Als Suchfilter“ an). | P1 | S | `MudExObjectEdit` Gruppen + `EnabledWhen`/`IgnoreIf` |
| V7 | **Kleinkram**: `Immediate` auf allen Namensfeldern vor Primärknopf; fertige Uploads nach 3 s ausblenden, Fehler bleiben; Icon-Knöpfe im Ordnerkopf mit Tooltip *und* `aria-label`; Workspace-Liste trennt „Meine“ und „Alle (Admin)“. | P1 | S | – |

## 3. Wizards

Ein Wizard lohnt sich, wo heute ein Fließtext eine Schrittfolge beschreibt oder ein Ergebnis mehrere Seiten braucht.

| # | Wizard | Schritte | Prio | Aufwand | Bausteine |
|---|---|---|---|---|---|
| W1 | **Erste Schritte (Admin)** nach Setup/erstem Login | 1. Ordnerstruktur aus Vorlage (Marketing, Produkte, Intern … oder leer) 2. Dateien hochladen 3. Personen einladen + Rolle (Member/Editor/Admin) 4. Optional: Metadaten-Vorlage (Schlagworte, Status, Jahr) 5. Fertig → Checkliste bleibt auf Start | P1 | M | **[Lib]** Wizard-Komponente, Setup-Wizard (`Pages/Setup.razor`) als Vorbild |
| W2 | **Hochladen & Einordnen** (Drag & Drop öffnet ihn optional) | 1. Dateien 2. Zielordner (Picker) 3. Gemeinsame Metadaten/Schlagworte (nur Pflichtfelder sichtbar) 4. Optional teilen | P2 | M | `MudExUploadEdit`, `FolderPicker`, `MetadataEditor`, Teilen-Dialog V4 |
| W3 | **Workspace anlegen** | 1. Name + Art (mit Erklärkarten statt Select) oder Vorlage 2. Mitglieder + Rolle 3. Optional: Zielordner für „Finalisieren“, Aufgaben aus Vorlage | P1 | S | `MudExCardList` für Art/Vorlage, bestehende Member-Suche |
| W4 | **Personen einladen** | 1. E-Mails (mehrere) 2. Rolle/Gruppe 3. Optional: Startordner/Workspace freigeben 4. Einladungs-Mail Vorschau | P2 | M | Lib-Identity (Benutzer anlegen), Mail-Templates der Lib |
| W5 | **Rechte setzen** am Ordner | 1. Wer (Person/Gruppe/Rolle) 2. Was (Lesen · Bearbeiten · Verwalten als drei Stufen statt Einzelrechte; „Erweitert“ zeigt `Nodes.*`) 3. Vererbung ja/nein mit Vorschau „betrifft 128 Dateien“ | P1 | M | Coworkee Rechte-UI, Stufen als Preset auf `NodePermissions` |
| W6 | **Lizenzierung einrichten** (ersetzt die 6-Schritte-Liste) | 1. Abrechnungskontakt 2. Rechteinhaber 3. Preisliste 4. Lizenz 5. Dateien zuordnen (Picker) | P2 | L | **[Lib]** Wizard; vorhandene DRM-Formulare als Schritte |
| W7 | **Automatisch verschlagworten** (Regeln + KI) | 1. Ziel-Eigenschaft (oder neu anlegen) 2. KI-Dienst + Schlüssel (Lib-AI-Settings) 3. Testlauf an 3 Dateien 4. Regel aktivieren | P3 | L | Lib-AI-Settings, Dry run der Regeln |
| W8 | **Share-Link** | 1. Was (Auswahl) 2. Für wen (E-Mails, optional) 3. Was darf man (Formate/Originale/Download) + Ablauf 4. Link kopieren / Mail senden | P2 | S | wird Tab „Per Link“ in V4 |

## 4. Navigation / Informationsarchitektur

Heute (Admin): Home · API · Chat · Content (5) · Collaboration (2) · Licensing (2) · Administration (7 Sharemee + 5 Lib-Gruppen). Gruppen mit zwei Einträgen kosten einen Klick ohne Gewinn; „API“ steht für jeden Admin ganz oben.

Vorschlag:

```
Start
Dateien            (= Browse, Baum direkt im Menü aufklappbar)
Suche              (erweiterte Suche; Schnellsuche in der App-Leiste)
Workspaces
Personen
Lizenzen ▸ Shop · Meine Käufe          (nur mit Drm.Offers)
Mehr ▸ Hintergrundaufgaben · Papierkorb · Chat
Verwaltung ▸
   Inhalte: Metadaten · Item types · Formate & Vorschauen
   Automatisierung: Regeln · KI-Tagging
   Rechte: Einschränkungen · Benutzer & Rollen (Lib)
   Lizenzverwaltung
   System (Lib: Einstellungen, Themes, Sprachen, Mail, Monitoring, API)
```

| # | Vorschlag | Prio | Aufwand | Bausteine |
|---|---|---|---|---|
| N1 | Gruppen mit ≤ 2 Einträgen auflösen, „API“ unter Verwaltung › System | P1 | S | `NavigationMenuOptions` (Order/Group), `SharemeNavigation` |
| N2 | Ordnerbaum als Unterknoten von „Dateien“ im Menü (statt eigener Spalte in Browse) | P2 | M | `MudExTreeView` im Nav (Lib-Nav ist schon `MudExTreeView`) **[Lib]**: Nav-Eintrag mit Lazy-Kindern |
| N3 | Verwaltung nach Aufgaben gruppieren (s. o.); Sharemee-Seiten und Lib-Gruppen gemischt nach Thema statt nach Herkunft | P2 | S | `NavigationMenuOptions.Place` |
| N4 | Telefon: App-Leiste auf Logo + Suche + Avatar; Sprache, Theme, Textrichtung, Dunkelmodus ins Avatar-Menü | P1 | S | **[Lib]** `CoworkeeLayout` App-Bar (Breakpoint) |

## 5. Leere Zustände

Die Texte sind gut, aber passiv. Jeder leere Zustand bekommt **einen Knopf für den nächsten Schritt**:

| Ort | Heute | Vorschlag |
|---|---|---|
| Start (Bild 1) | 5 leere Kacheln | Admin: Onboarding-Checkliste (W1) statt leerer Kacheln; Benutzer: „Zu den Dateien“ + „Workspace anlegen“ |
| Leerer Ordner | Text verweist auf Felder darüber | Große Drop-Fläche mit „Dateien hochladen“ + „Ordner anlegen“ |
| Workspaces | Text + offenes Formular | Illustration + „Workspace anlegen“ (W3) |
| Metadaten/Regeln/Formate/Lizenzen | „Create the first one below“ | Knopf „Erste … anlegen“ + optional „Vorlage übernehmen“ (z. B. Standardformate Web/Druck/Thumbnail, Standardeigenschaften Schlagworte/Status/Copyright) |
| Lizenz-Shop | „ask your license managers“ | Für Admins Link „Lizenzierung einrichten“ (W6) |

Prio P1, Aufwand S–M. **[Lib]**: generische `EmptyState`-Komponente (Icon, Titel, Text, Primär-/Sekundäraktion), die auch die Lib-Seiten nutzen.

## 6. Sinnvolle Vorgaben

| # | Vorgabe | Prio | Aufwand |
|---|---|---|---|
| D1 | Neues System bringt Standard-Formate (Thumbnail 400 px, Web 1920 px, Druck Original) und Standard-Eigenschaften (Schlagworte als Liste, Status, Copyright) mit – abschaltbar im Onboarding | P1 | S |
| D2 | Rollen-Presets statt Einzelrechte: *Leser* (Read, Download), *Mitarbeiter* (+ Create, Update, Move, Workspaces), *Redakteur* (+ Delete, News/Seiten), *Verwalter* (+ ManagePermissions); die Demo-Rolle *Member* entspricht *Mitarbeiter* | P1 | S |
| D3 | Share-Links: Ablauf 14 Tage, nur Web-Format, keine Originale vorgewählt | P2 | S |
| D4 | Workspace: Ersteller ist Owner, Art „Collaboration“ vorgewählt, Finalisieren-Ziel = aktueller Ordner, wenn aus Browse angelegt | P2 | S |
| D5 | Key von Eigenschaften/Formaten/Typen wird aus dem Namen erzeugt (editierbar) | P1 | S |

## 7. Progressive Disclosure

- **Erweitert-Bereiche** in allen Anlegen-Dialogen (V6 als Muster): sichtbar ist, was 80 % brauchen.
- **Asset-Detail**: 9 Tabs (Metadaten, Technisch, Formate, Versionen, Beziehungen, Lizenzen, Kommentare, Rechte, Aktivität) → 3 sichtbar (Metadaten, Kommentare, Versionen) + „Mehr ▾“; Technisch/SHA-256 in ein Info-Popover.
- **Ordner-Info-Panel** (V2) statt Tabs unter jeder Liste.
- **Admin-Funktionen ausblenden**, solange sie nicht eingerichtet sind: Auswahlaktionen „Zur Lizenz“/„KI-Tagging“ erscheinen erst, wenn Lizenzen bzw. ein Tagging-Setup existieren (mit Hinweis im „Mehr“-Menü für Admins).
- **Telefon**: Tabellen als Karten (`MudExCardList`), Kopf-Aktionen im Overflow-Menü (Bild 4).

## 8. Bausteine aus der Lib wiederverwenden

| Bedarf | Vorhanden |
|---|---|
| Anlegen/Bearbeiten | `MudExObjectEdit`/`MudExObjectEditForm` + Dialog (`ShowEx`, Slide-In), `SettingsFormRules`-Muster mit `EnabledWhen` |
| Dateien | `MudExFileDisplay` (Vorschau, braucht feste Höhe), `MudExUploadEdit`, `MudExFileManager`, `CoworkeeFilePicker` |
| Navigation | Lib-Nav auf `MudExTreeView`, `NavigationMenuOptions` (Order, Group, Place) |
| Auswahl/Karten | `MudExCardList`, `MudExChipSelect`, `MudExTagField` (Schlagworte mit Vorschlägen) |
| Layout | `MudExSplitter`/`MudExSplitPanel`, `StickyMergeTabs` der Settings-Seiten |
| Schrittfolgen | `Pages/Setup.razor` und `CampaignWizard` (Chips + versteckte Schritte) – heute zwei Eigenbauten |

## 9. Was in die Lib gehört

| # | Baustein | Warum Lib |
|---|---|---|
| L1 | **`CoworkeeWizard`**: Schritte als `RenderFragment` oder `MudExObjectEdit`-Modelle, Validierung pro Schritt, Zurück/Weiter/Fertig, Fortschritt, als Seite oder Slide-Dialog, Zustand bei Abbruch optional als Entwurf | Setup-Wizard und `CampaignWizard` bauen es schon zweimal; Sharemee braucht W1–W8 |
| L2 | **`OnboardingChecklist`**: Liste aus Contributors (`IOnboardingStep` mit `IsDoneAsync`, Link/Wizard, Berechtigung), Fortschritt, ausblendbar pro Benutzer, auf der Startseite oder im Benachrichtigungsmenü | Jede App auf Coworkee hat „nach dem Setup: was jetzt?“; Lib-Schritte (Mail einrichten, Theme, Sprachen, KI) + App-Schritte |
| L3 | **`EmptyState`** (Icon, Titel, Text, Aktionen) | Einheitliche leere Zustände auf Lib- und App-Seiten |
| L4 | **App-Leiste responsiv** (Overflow ins Avatar-Menü unter `sm`) | Betrifft jede App (Bild 4) |
| L5 | **Rechte-Presets** (Stufen über Einzelrechten) in der Rollen-/Rechte-UI | Allgemeines Muster, Sharemee liefert nur die Stufen |
| L6 | **Nav-Eintrag mit Lazy-Kindern** (Baum im Menü) | Für Dateien, Wissensseiten, Reports gleichermaßen |

## 10. Reihenfolge

1. P1/S zuerst: V3, V6, V7, N1, N4 (Lib), D2, D5, leere Zustände mit Aktion.
2. P1/M: L1 Wizard + L2 Checkliste in der Lib, dann W1 (Erste Schritte), W3 (Workspace), W5 (Rechte), V1 (Dialoge statt Inline-Formulare), V2 (Browse), V4 (Teilen-Dialog).
3. P2: W2, W4, W8, V5, N2, N3, D1, D3, D4.
4. P3: W6, W7.
