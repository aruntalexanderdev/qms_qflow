# QMS Design System

The visual language for every QMS screen: the Angular web app (admin, counter console, dashboard, reports), the zone and counter displays, and the Android dispenser.

- **Values live in [`tokens.json`](tokens.json).** This document explains how to use them.
- **Order of change:** `tokens.json` + this document → `frontend/src/styles/_tokens.scss` → `android/.../ui/theme/`. Never the other way round.
- **Decision record:** [ADR-001](../adr/ADR-001-ui-design-system.md).

## 1. Look and feel

Modern, calm and professional, the way a bank should look. The style is flat: white cards on a light grey page, with a dark teal sidebar and a single teal accent for actions. There are few shadows, thin borders, generous spacing and rounded corners.

```
+-------------+-----------------------------------------------+
| ▣ QFlow     | [≡]                       (AA) Name   [Sign out]|  ← white top bar, bottom border
|   BANK QMS  +-----------------------------------------------+
|             |                                               |
| OVERVIEW    |  Page title                                   |  ← 28px bold
| • Dashboard |  One-line description in muted text           |
|             |  +-----------------------------------------+  |
| SETUP       |  | Label            Label                  |  |  ← white card, 12px radius
| ▌Branches ▐ |  | [ input       ]  [ input       ]        |  |
| • Zones     |  |                                         |  |
| • Devices   |  | [ Save ]  Cancel                        |  |  ← teal primary + text button
|             |  +-----------------------------------------+  |
+-------------+-----------------------------------------------+
  dark teal        light grey page (#F4F6F6)
```

## 2. Colour

Every pair below passes **WCAG 2.1 AA** (4.5:1 for normal text, 3:1 for large text and UI borders). Re-check contrast whenever a value changes.

### 2.1 Brand

| Token | Hex | Use |
| --- | --- | --- |
| `brand.primary` | `#0D7E8F` | Primary buttons, active nav item, links, focus, selected states |
| `brand.primary-hover` | `#0A6878` | Hover on primary |
| `brand.primary-pressed` | `#085663` | Pressed on primary |
| `brand.primary-subtle` | `#E8F1F3` | Avatar background, selected row, light highlight |
| `brand.on-primary` | `#FFFFFF` | Text and icons on primary (contrast 4.77) |
| `brand.on-primary-subtle` | `#11676E` | Text on `primary-subtle` (5.74) |

### 2.2 Sidebar (navigation shell)

| Token | Hex | Use |
| --- | --- | --- |
| `nav.bg` | `#0F262A` | Sidebar background |
| `nav.bg-hover` | `#163339` | Item hover |
| `nav.border` | `#20363A` | Divider under the brand block |
| `nav.text` | `#B8CFD0` | Nav items (9.67) |
| `nav.text-section` | `#8AA3A7` | Uppercase section labels (5.92) |
| `nav.bullet` | `#3D5A5C` | Small dot before items (decorative) |
| `nav.active-bg` / `nav.active-text` | `#0D7E8F` / `#FFFFFF` | Current page |

### 2.3 Neutrals

| Token | Hex | Use |
| --- | --- | --- |
| `neutral.bg` | `#F4F6F6` | Page background |
| `neutral.surface` | `#FFFFFF` | Cards, top bar, inputs, dialogs |
| `neutral.surface-muted` | `#F9FAFA` | Table header, read-only fields |
| `neutral.border` | `#E3E5E4` | Card, table and top-bar borders; dividers |
| `neutral.border-input` | `#8A9094` | Input, select and checkbox borders (3.23, meets the 3:1 rule for form controls) |
| `neutral.text` | `#111315` | Headings, body, input values |
| `neutral.text-label` | `#393F46` | Form labels, secondary buttons |
| `neutral.text-muted` | `#6B7174` | Page descriptions, help text, Cancel |
| `neutral.text-placeholder` | `#828689` | Placeholders only |
| `neutral.text-disabled` | `#9AA0A4` | Disabled text only |

### 2.4 Status and QMS-specific colours

Each state is a badge with a light background and dark text. Never rely on colour alone: always show the label, and an icon where it helps.

| Group | Value | Background / text | Note |
| --- | --- | --- | --- |
| Status | success / warning / error / info | see `tokens.json → color.status` | Toasts, alerts, field errors (`error.fg`) |
| Token state | Waiting | `#F2F4F7` / `#475467` | |
| | Called | `#DBEAFE` / `#1E40AF` | |
| | Serving | `#DCFCE7` / `#166534` | |
| | Held | `#EDE9FE` / `#5B21B6` | |
| | NoShow | `#FEE4E2` / `#B42318` | |
| | Completed | `#E8F1F3` / `#11676E` | |
| | Cancelled | `#F2F4F7` / `#475467` | Strike through the number |
| Priority | Normal | `#F2F4F7` / `#475467` | |
| | VIP | `#FEF9C3` / `#854D0E` | Add a ★ icon |
| Counter | Open / Paused / Closed | green / amber / grey | |

### 2.5 Display theme (zone and counter displays on TVs)

TVs use a dark theme built from the sidebar colour, so they are readable from across a hall and match the brand.

| Token | Hex | Use |
| --- | --- | --- |
| `display.bg` | `#0F262A` | Screen background |
| `display.panel` | `#163339` | "Last 5 calls" list and the counter tiles |
| `display.text` / `display.text-muted` | `#FFFFFF` / `#B8CFD0` | Token numbers / labels |
| `display.accent` | `#5FC9D6` | Counter numbers |
| `display.latest-call-bg` | `#0D7E8F` | Latest-call banner, with white text |
| `display.calling` | `#FBBF24` | "Now calling" highlight and pulse, **only on `display.bg` or `display.panel`** (it fails contrast on the teal banner) |

## 3. Typography

| Script | Font | Licence | How it is loaded |
| --- | --- | --- | --- |
| Latin | **Instrument Sans** | OFL | `@fontsource-variable/instrument-sans` (bundled) |
| Arabic | **IBM Plex Sans Arabic** | OFL | `@fontsource/ibm-plex-sans-arabic` (bundled) |

Load fonts **from the app bundle only**, never from Google Fonts or another CDN (spec section 11: no external calls). On Android, put the `.ttf` files in `res/font/`.

When Arabic is active (`dir="rtl"`), IBM Plex Sans Arabic comes first in the font stack. Token numbers (`VB0001`) always use Instrument Sans with `font-variant-numeric: tabular-nums`.

| Role | Size / weight | Notes |
| --- | --- | --- |
| Page title | 28px / 700 | letter-spacing `-0.01em` |
| Card or section title | 20px / 600 | |
| Body | 15px / 400 | line-height 1.5 |
| Form label | 14px / 600 | `text-label` |
| Help text, description | 15px / 400 | `text-muted` |
| Nav item | 15px / 500 | |
| Nav section label | 12px / 600 | UPPERCASE, letter-spacing `0.08em` |
| Table cell | 14px / 400 | |
| Badge | 12px / 600 | |
| Counter console, current token | 56px / 700 | |
| Display, latest token | `clamp(96px, 14vw, 200px)` / 700 | |
| Dispenser button | 24–28px / 600 | |

## 4. Spacing, radius and elevation

- **Spacing:** a 4px scale (`space.1` = 4px up to `space.16` = 64px). Page padding is 32px, card padding 28px, form field gap 20px (vertical) and 24px (column), label to input 8px.
- **Radius:** inputs and buttons `md` (8px), nav items `md`, cards `lg` (12px), dialogs `xl` (16px), badges and avatars `full`.
- **Elevation:** cards are flat (1px `neutral.border`, no shadow). Menus and dropdowns use `shadow.md`; dialogs use `shadow.lg` over `neutral.overlay`.

## 5. Layout

- **Shell:** a 280px dark sidebar (collapses to 72px icons, or to a drawer below `lg`) and a 64px white top bar with a bottom border. The top bar holds the menu toggle (start side), then the user avatar, name, role and a Sign out outline button (end side). The page sits on `neutral.bg`.
- **Content:** max width 1200px. Forms sit in a white card, max width 848px, with two columns that become one below `md`.
- **Page header:** title, a one-line muted description, then content.
- **RTL:** the whole layout mirrors (the sidebar moves to the right). Use CSS logical properties only.

## 6. Components

| Component | Specification |
| --- | --- |
| Primary button | `primary` background, white text, 44px high, 8px radius, 600 weight, 20px horizontal padding. Hover and pressed tokens; disabled at 40% opacity |
| Secondary (outline) button | White background, 1px `neutral.border`, `text-label` text (e.g. Sign out) |
| Text button | No background, `text-muted` (e.g. Cancel); `primary` on hover |
| Danger button | `status.error.fg` background, white text; confirm in a dialog |
| Input / select | 44px high, white, 1px `border-input`, 8px radius, 12px padding, label above. Focus: 1px `primary` border plus a 3px `focus-ring` |
| Field error | `error.fg` border plus a 13px message below; the problem-details message from the API |
| Card | White, 1px `neutral.border`, 12px radius, 28px padding |
| Table | Header in `surface-muted` with 12px uppercase `text-muted`; rows separated by 1px `neutral.border`; hover in `surface-muted`; selected row in `primary-subtle` |
| Mapping grid | Checkbox cells; a disabled cell (not allowed by the level above) gets a hatched `surface-muted` fill and a tooltip with the reason |
| Badge | Pill, 12px / 600, the colours from section 2.4 |
| Avatar | 36px circle, `primary-subtle` background, `on-primary-subtle` initials |
| Nav item | 44px high, 8px radius, 12px inner margin, dot bullet; active item fills with `primary` |
| Toast | White card with a 4px status-colour bar on the start edge; top end corner; 5 s |
| Dispenser button | At least 72px high, 12px radius. Service buttons: white with a `primary` border, filled when selected. Print Token: full-width `primary` |

## 7. Accessibility

- Text contrast is at least 4.5:1, large text and UI borders at least 3:1. Check every new colour pair.
- A visible focus ring on every interactive element; never `outline: none` without a replacement.
- Colour is never the only signal: badges carry text, errors carry a message, VIP carries a ★.
- Touch targets are at least 44×44px on the web and 72px on the dispenser.
- Respect `prefers-reduced-motion`: turn off the display pulse and the slide transitions.

## 8. Where the tokens live in code

| App | File | How |
| --- | --- | --- |
| Web | `frontend/src/styles/_tokens.scss` | CSS custom properties `--qms-*`, generated from or mirroring `tokens.json` |
| Web | `frontend/src/styles/_material-theme.scss` | Angular Material 22 (M3) theme mapped to the `--qms-*` variables |
| Web, displays | `[data-theme="display"]` on `<body>` | Swaps to the display tokens |
| Android | `ui/theme/Color.kt`, `Type.kt`, `Theme.kt` | Material 3 `ColorScheme`, plus `QmsColors` for state and priority colours |
| Ticket print | — | Monochrome; logo converted to 1-bit |

Naming: `--qms-<group>-<name>`, for example `--qms-brand-primary`, `--qms-token-state-called-bg`. In Kotlin: `QmsColors.tokenCalledBg`.
