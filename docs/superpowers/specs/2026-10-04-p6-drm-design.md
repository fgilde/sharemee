# P6 DRM – Design

Ziel (Architektur-Spec §7): Lizenzen, Rechteinhaber, Abrechnungskontakte, Preislisten, Buyouts, Angebote, Käufe und DRM-Rechte; Lizenzkauf-Flow mit Mails. Vorbild: Sharedien `api/v{5|6}/drm` (siehe `docs/analysis/sharedien-backend.md` §4.8 und §8).

## 1. Modell (Modul `Shareme.Drm`, alles je Mandant)

| Objekt | Inhalt |
|---|---|
| `BillingContact` | Name, E-Mail, Adresse, Bankdaten (IBAN, BIC), aktiv |
| `RightsOwner` | Name, Art (Person/Agentur/…), Abrechnungskontakt, Adresse, aktiv |
| `ContextGroup` | Name, enthaltene Nutzerkontexte (Keys der Metadaten-Kontexte, z. B. Märkte/Regionen) |
| `PriceList` | Name, Beschreibung, aktiv, Prozentsätze je Kontext (Key) oder Kontextgruppe |
| `License` | Name, Beschreibung, Rechte (Liste, z. B. print, web, social), Zeitraum (von/bis), Kaufdauer in Tagen, Status (draft/active/inactive), zugeordnete Assets |
| `Buyout` | Lizenz × Rechteinhaber × Preisliste × Währung (ISO): Grundpreis, Zuschlag %, gültig von/bis |
| `Purchase` | Bestellnummer, Lizenz, Kontext oder Kontextgruppe, Käufer, Start/Ende, Preise je Währung, enthaltene Assets (Schnappschuss) |

Sharedien hat Lizenz, Rechteinhaber und Kontakt als Knoten mit Properties; Shareme nimmt feste Felder (die Sharedien-Systemproperties `license_*`, `rightsowner_*`, `billingcontact_*`, `contact_*`, `banking_*` decken sich mit ihnen). Lizenz ↔ Assets ist eine eigene Zuordnung (Sharedien: Edge `LicenseDrm`).

## 2. Angebot und Preis

Für eine aktive Lizenz, deren Zeitraum heute umfasst, und einen Kontext (oder eine Gruppe) gilt je gültigem Buyout:
`Preis = Grundpreis × (1 + Zuschlag/100) × Prozentsatz(Preisliste, Kontext)/100`.
Der Prozentsatz kommt aus dem Eintrag für den Kontext, sonst aus einer Gruppe, die den Kontext enthält (höchster Satz), sonst 100 %. Preise je Währung werden summiert (mehrere Rechteinhaber). Das Angebot nennt außerdem die Gruppen mit dem Kontext als Alternativen (mit ihren Preisen), die Zahl der lesbaren lizenzierten Assets und ein Vorschau-Asset. Kaufdauer: Start heute, Ende nach `Kaufdauer` Tagen, höchstens bis Lizenzende.

Angebotssuche: Text (Name, Beschreibung), Kontext/Gruppe, Rechte (alle müssen enthalten sein), gültig an einem Datum, Asset (Lizenzen, die das Asset enthalten), Paging.

## 3. Kauf

`POST /api/v1/drm/purchases` (Lizenz, Kontext oder Gruppe) berechnet das Angebot neu, legt den Kauf mit Bestellnummer `P-yyyyMMdd-xxxxxx` an und verschickt Mails: an den Käufer (`Drm.PurchaseCustomer`), an die Abrechnungskontakte der beteiligten Rechteinhaber (`Drm.PurchaseBillingContact`) und an die Adressen aus `Shareme:Drm:PurchaseEmails` (`Drm.PurchaseAdmin`), jeweils en/de. Käufe ohne gültigen Buyout (kein Preis) sind nicht möglich. Eigene Käufe sieht jeder Käufer, alle Käufe sieht `Drm.Purchases`.

## 4. Rechte

- `Drm.Manage`: Stammdaten (Kontakte, Rechteinhaber, Gruppen, Preislisten, Lizenzen, Buyouts, Asset-Zuordnung).
- `Drm.Offers`: Angebote sehen (Preise).
- `Drm.Purchase`: kaufen.
- `Drm.Purchases`: alle Käufe sehen.

Lizenzierte Assets erscheinen in Angeboten nur, soweit der Anfragende sie lesen darf (Zahl und Vorschau); die Zuordnung prüft Leserechte auf die Assets.

## 5. UI

- `/admin/drm`: Tabs Lizenzen (mit Assets, Buyouts), Rechteinhaber, Abrechnungskontakte, Preislisten, Kontextgruppen.
- `/licenses`: Angebote suchen, Kontext wählen, kaufen; `/purchases`: Käufe.
- Asset-Detail: Lizenzen des Assets mit Link zum Angebot.

## 6. Nicht in P6

Zahlung/Checkout bei einem Zahlungsanbieter, Rechnungen als PDF, Sperren von Downloads ohne Kauf (die Lizenzlage ist sichtbar, Downloads regeln weiter Rechte und Restriktionen).
