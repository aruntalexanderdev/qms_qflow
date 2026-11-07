---
name: android-kotlin
description: Senior Android method for the QMS token dispenser — Kotlin, Jetpack Compose, MVVM, device registration and config, one-screen token UI, Sunmi printing, heartbeat, kiosk mode, English/Arabic. Use in stage 5.
---

# Android (Kotlin) skill

## Structure
```
android/app/src/main/java/.../qms/
  data/      api/ (Retrofit), dto/, repository/, storage/ (EncryptedSharedPreferences)
  domain/    models (DispenserConfig, ServiceButton, PriorityOption, IssuedToken)
  print/     TicketPrinter interface, SunmiTicketPrinter, FakeTicketPrinter
  ui/        setup/, home/, confirm/, outofservice/, theme/
  work/      HeartbeatWorker, ConfigRefresh
```
- MVVM: Compose screens observe `StateFlow<UiState>` from ViewModels; Hilt for DI.
- Retrofit + OkHttp interceptor adds `X-Device-Key`; timeouts 10 s; TLS only.

## Design system (rules section 11)
- Read `docs/design/DESIGN_SYSTEM.md`; mirror `docs/design/tokens.json` in `ui/theme/Color.kt`, `Type.kt`, `Shape.kt` and `Theme.kt` (Material 3 `lightColorScheme` with `primary = 0xFF0D7E8F` and so on, plus `QmsColors` for state and priority).
- Fonts: Instrument Sans and IBM Plex Sans Arabic `.ttf` in `res/font/`. Token numbers use tabular figures.
- Composables use `MaterialTheme.colorScheme` / `QmsColors` / `MaterialTheme.typography` only. No `Color(0x…)` or `sp` literals outside `ui/theme/`.
- Kiosk sizing: touch targets of at least 72dp; service buttons are outlined in `primary` and filled when selected; Print Token is full-width `primary`.

## Screens
1. **Setup** (first run): server URL + registration code → `POST /device/register` → device key stored encrypted.
2. **Home** (one screen, spec 7): phone row by token mode (country code dropdown + mobile no, Optional/Required label); service buttons from config, grouped by zone; priority buttons only for the selected service's Available priority types (hidden if one); Print Token / Get Token button enabled only when valid.
3. **Confirm:** large token number, service, zone, people ahead; auto-return after `confirm_seconds`.
4. **Out of service:** no network, printer error, branch closed.

## Rules
- No business logic: availability, limits and numbering come from the API.
- Print modes: check printer status before calling `POST /tokens`; if printing fails after issue, show the token number on screen and report `printer_status` — the token already exists.
- Config refresh: on `DeviceConfigChanged` (SignalR Java client or polling fallback) and every 5 minutes.
- Heartbeat every 60 s via WorkManager / coroutine loop with printer status.
- Kiosk: lock task mode, hide system bars, hidden admin exit with PIN.
- Locale: English / Arabic toggle on Home; Compose layout handles RTL; token numbers Latin digits.
- Never log phone numbers or the device key.

## Tests
ViewModel unit tests with fake repository and `FakeTicketPrinter`: button visibility per mode, priority row logic, disabled states, error paths. `./gradlew assembleDebug testDebugUnitTest` green. List manual checks for a real Sunmi device in the handoff.
