# Bundled font licenses

All fonts embedded in the app are open-license and safe to redistribute inside the single exe.

## Type-system upgrade (added 2026-07-12) — all SIL Open Font License 1.1

| Font | Files | Role | Source |
|---|---|---|---|
| **Orbitron** | `Orbitron-Bold.ttf`, `Orbitron-Black.ttf` | display / headings | Google Fonts (OFL); static instances via Fontsource |
| **Inter** | `Inter-Regular.ttf`, `Inter-SemiBold.ttf`, `Inter-Bold.ttf` | body / UI | Google Fonts / rsms (OFL); static instances via Fontsource |
| **JetBrains Mono** | `JetBrainsMono-Regular.ttf`, `JetBrainsMono-Bold.ttf` | tabular numbers (lap times, gaps) | JetBrains (OFL) |
| **Press Start 2P** | `PressStart2P-Regular.ttf` | pixel badges / position numbers | Google Fonts (OFL) |
| **Chakra Petch** | `ChakraPetch-Bold.ttf` | alt display (clean-modern combo) | Google Fonts (OFL) |
| **Saira** | `Saira-Regular.ttf`, `Saira-SemiBold.ttf` | alt body (clean-modern combo) | Google Fonts (OFL); static via Fontsource |
| **Silkscreen** | `Silkscreen-Regular.ttf` | alt pixel (hardcore-retro combo) | Google Fonts (OFL) |

The SIL OFL-1.1 permits bundling, embedding and redistribution with the software; the fonts may not be
sold on their own. Full license text: <https://openfontlicense.org/>. Each family's `OFL.txt` is
available in its Google Fonts / upstream repository directory.

> **WPF note:** Orbitron, Inter, JetBrains Mono and Saira ship upstream as *variable* fonts, but classic
> WPF/DirectWrite only renders a variable font's default weight. The files above are **static weight
> instances**, so `FontWeight` works normally.

## Pre-existing fonts

- **Race Sport**, **Retro Floral**, **Microsport** — bundled display faces (see the original `<Resource>`
  entries in `Companion.App.csproj`).
- **Open Sans**, **Roboto** — Apache License 2.0.
