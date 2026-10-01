# P0.3c Realtime and Notifications – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (native). Tests per case are written first; implementation files are listed with their contract.

**Goal:** Generische Echtzeit-Aktualisierung über SignalR (Topics, autorisiertes Abonnieren, automatische `EntityChanged`-Events nach Commit, Redis-Backplane) und Benachrichtigungen für User mit Glocke in der AppBar.

**Architecture:** Neues Paket `Coworkee.Realtime` (IWebModule): `RealtimeHub` unter `/hubs/realtime` (authentifiziert), `Subscribe(topic)`/`Unsubscribe(topic)` prüfen `IRealtimeTopicAuthorizer` je Präfix; Server→Client `OnEvent(RealtimeEnvelope)`. `IRealtimePublisher.PublishAsync(topic, type, payload)` schreibt ein Domain-Event `RealtimeEventRaised` in die Outbox; der Outbox-Handler sendet nach Commit über `IHubContext` an die SignalR-Gruppe des Topics. Ein `RealtimeChangeInterceptor` erzeugt für Aggregate mit `[Realtime]` automatisch `EntityChanged { EntityType, EntityId, Action, ChangedProperties }` an `type:{EntityType}` und `entity:{EntityType}:{Id}`. Redis-Backplane wenn `ConnectionStrings:redis` gesetzt. Neues Paket `Coworkee.Notifications`: `Notification`-Entity, `INotifier`, Endpunkte, Realtime an `user:{id}`. Client: `RealtimeClient` (SignalR-Client, Reconnect, Re-Subscribe), `<RealtimeSubscription Topic OnEvent>`, `NotificationBell`; Admin-Listen (Users, Groups, Roles, Themes) laden bei `EntityChanged` neu. BFF leitet `/hubs/{**}` inkl. WebSockets mit Bearer weiter.

**Tech Stack:** Microsoft.AspNetCore.SignalR (Shared Framework), Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.12, Microsoft.AspNetCore.SignalR.Client 10.0.12, Testcontainers.Redis 4.15.0.

**Spec:** `docs/superpowers/specs/2026-10-01-p0-foundation-design.md` Abschnitt 4 (Realtime, Notifications), DoD Punkt 4.

## Global Constraints

- Topics: `type:{EntityType}`, `entity:{EntityType}:{Id}`, `user:{id}`, `job:{id}`; unbekannte Präfixe werden abgelehnt.
- Topic-Autorisierung: `type:`/`entity:` verlangen die beim `[Realtime(Permission = ...)]` hinterlegte Permission; `user:{id}` nur der User selbst; Tenant-Isolation: Gruppen-Namen enthalten den Tenant (`{tenant}|{topic}`).
- Events gehen nur nach erfolgreichem Commit raus (Outbox); Payload von `EntityChanged` enthält nur Property-Namen, keine Werte.
- Hub-Methoden geben bei verweigertem Abo einen Fehler zurück, ohne die Verbindung zu trennen.
- Notifications: `Notification { Id, UserId, TenantId, Type, Title, Body, Link?, CreatedAt, ReadAt? }`; `GET /api/v1/notifications?unreadOnly`, `POST /{id}/read`, `POST /read-all`, `GET /unread-count`.

## Review Focus

1. User abonniert `user:{fremde id}` oder `type:` ohne Permission ⇒ abgelehnt. Test in Task 1.
2. Tenant A erhält keine `EntityChanged` von Tenant B für denselben Typ. Test in Task 1.
3. Rollback ⇒ kein Event. Test in Task 1.
4. Reconnect stellt alle Abos wieder her. Test in Task 3.
5. Notification eines anderen Users lesen/als gelesen markieren ⇒ 404. Test in Task 2.

---

### Task 1: Coworkee.Realtime (Server)

**Files:** Create `coworkee/src/Coworkee.Realtime/**`, `coworkee/src/Coworkee.Contracts/Realtime/*.cs` (`RealtimeEnvelope(Topic, Type, JsonElement Payload)`, `EntityChangedPayload(EntityType, EntityId, Action, ChangedProperties)`), `[Realtime]` in `Coworkee.Domain`; Test `coworkee/tests/Coworkee.Realtime.Tests/**` (TestServer + `HubConnection` über `server.CreateHandler()` mit LongPolling).

- [ ] Tests: `Subscriber_receives_entity_changed_after_commit`, `Rollback_publishes_nothing`, `Subscription_without_permission_is_rejected`, `Foreign_user_topic_is_rejected`, `Other_tenant_receives_nothing`, `Unknown_prefix_is_rejected`, `Payload_has_property_names_only`.
- [ ] rot → grün → Commit `feat(realtime): signalr hub with authorized topics and entity change events`.

### Task 2: Coworkee.Notifications

**Files:** Create `coworkee/src/Coworkee.Notifications/**`, Contracts; Tests.

- [ ] Tests: `Notify_stores_and_pushes_to_user_topic`, `List_and_unread_count_are_per_user`, `Mark_read_of_foreign_notification_is_not_found`, `Read_all_marks_only_own`.
- [ ] rot → grün → Commit `feat(notifications): user notifications with realtime push`.

### Task 3: Client

**Files:** `Coworkee.Client.Blazor`: `RealtimeClient` (HubConnection gegen `/hubs/realtime`, `WithAutomaticReconnect`, Re-Subscribe in `Reconnected`), `RealtimeSubscription`-Komponente, `NotificationBell` in `CoworkeeLayout`, Admin-Seiten Users/Groups/Roles/Themes abonnieren `type:User` usw.; Tests mit Fake-`IRealtimeConnection`.

- [ ] Tests: `Resubscribes_after_reconnect`, `Subscription_component_unsubscribes_on_dispose`, `Bell_shows_unread_count_and_updates_on_event`.
- [ ] rot → grün → Commit `feat(client): realtime client, live admin lists and notification bell`.

### Task 4: BFF + Shareme

**Files:** BFF: `/hubs/{**catch-all}` forwarden (WebSockets), Shareme: Module, Redis-Backplane an Api (`WithReference(infrastructure.Redis)`), Migration `RealtimeNotifications`; E2E: zwei Browser-Sessions (beide über den BFF eingeloggt); A ändert einen User, B empfängt `EntityChanged` über SignalR-Client gegen den BFF.

- [ ] rot → grün → Commit `feat: shareme realtime and notifications`.
