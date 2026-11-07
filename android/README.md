# QMS Android — Token Dispenser App

Kotlin app for Sunmi POS devices. It shows one screen (phone, service, priority, Print Token), issues tokens through the API, prints tickets on the built-in Sunmi printer and runs in kiosk (lock-task) mode. It holds no business rules: it shows what `GET /api/v1/device/config` returns. Spec: [`docs/PROJECT_SPEC.md`](../docs/PROJECT_SPEC.md) section 7.

> Status: not scaffolded yet. Built in milestone **M7 Android dispenser**.

## Versions

| Item | Version |
| --- | --- |
| Kotlin | 2.4.x |
| Android Gradle Plugin / Gradle | latest stable (use the Gradle wrapper) |
| JDK | 21 (the JetBrains Runtime bundled with Android Studio) |
| UI | Jetpack Compose (Compose BOM, latest stable) + Material 3 |
| Networking | Retrofit + OkHttp, kotlinx.serialization |
| Printing | Sunmi printer SDK, ESC/POS fallback |
| `minSdk` | Set from the oldest Android version on the bank's Sunmi models. Confirm this before M7. |

## Structure

```
android/
  app/src/main/java/.../qms/
    data/        api (Retrofit), repository, secure storage for the device key
    domain/      models mapped from API DTOs
    ui/          dispenser screen, confirmation, settings/registration, ViewModels
    printing/    TicketPrinter interface, SunmiTicketPrinter, FakeTicketPrinter
  app/src/main/res/values/      English strings
  app/src/main/res/values-ar/   Arabic strings
```

## Design system

Follow [`docs/design/DESIGN_SYSTEM.md`](../docs/design/DESIGN_SYSTEM.md). Mirror [`tokens.json`](../docs/design/tokens.json) in `ui/theme/` (`Color.kt`, `Type.kt`, `Shape.kt`, `Theme.kt`). Composables use only `MaterialTheme` and `QmsColors`, never `Color(0x…)` literals.

## Behaviour

1. **Register:** enter the server URL and a one-time code; the device key is kept in encrypted storage.
2. **Load config:** reload on SignalR `DeviceConfigChanged` and every 5 minutes.
3. **Issue a token:** `POST /api/v1/tokens`, then print (in print modes) and show the confirmation screen.
4. **Heartbeat:** `POST /api/v1/device/status` every 60 s, with the printer status.
5. **Offline:** show "Out of service". No offline tokens in v1.

## Commands

```bash
cd android && ./gradlew assembleDebug testDebugUnitTest
```

Open the `android/` folder in Android Studio (Apple Silicon build). Printing can only be checked on a real Sunmi device; use `FakeTicketPrinter` in the emulator.
