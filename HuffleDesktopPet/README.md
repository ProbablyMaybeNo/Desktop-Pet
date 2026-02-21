# HuffleDesktopPet

A transparent, always-on-top Windows desktop overlay pet ("Huffle") that lives on your screen, wanders around, and has simulated needs (hunger, hygiene, fun, knowledge).

---

## What it is

- A **WPF overlay** window — fully transparent background, no border, always on top.
- A small creature that wanders your screen autonomously.
- Needs that decay over real time and are persisted between sessions (`%AppData%\HuffleDesktopPet\pet_state.json`).
- Windows-only by design (WPF + Win32 APIs for click-through).

---

## Requirements

| Dependency | Version |
|---|---|
| Windows | 10 / 11 |
| .NET SDK | 8.0+ |
| Visual Studio | 2022 (Desktop development with .NET workload) OR build tools |
| PowerShell | 5.1+ |

Run the bootstrap check before building:

```powershell
.\tools\scripts\bootstrap.ps1
```

---

## How to run

### From Visual Studio 2022
1. Open `HuffleDesktopPet.sln`.
2. Set `HuffleDesktopPet.App` as the startup project.
3. Press **F5** (Debug) or **Ctrl+F5** (Run without debug).

### From PowerShell
```powershell
.\tools\scripts\run.ps1
# or in Release mode:
.\tools\scripts\run.ps1 -Configuration Release
```

### From terminal
```powershell
dotnet run --project src\HuffleDesktopPet.App\HuffleDesktopPet.App.csproj
```

---

## How to test

```powershell
.\tools\scripts\test.ps1
# or:
dotnet test src\HuffleDesktopPet.Tests\HuffleDesktopPet.Tests.csproj
```

---

## Project structure

```
HuffleDesktopPet/
  src/
    HuffleDesktopPet.App/      WPF overlay application
    HuffleDesktopPet.Core/     Pure .NET logic — no WPF dependency
    HuffleDesktopPet.Tests/    xUnit unit tests for Core
  tools/
    scripts/
      bootstrap.ps1            Dependency check
      run.ps1                  Build + launch
      test.ps1                 Run unit tests
      doctor.ps1               Environment diagnostics
  docs/                        Extended documentation
  README.md
  TESTING.md
  DEVLOG.md
```

---

## v1 Demo scope

| In scope | Out of scope |
|---|---|
| Transparent always-on-top overlay | Packaging / installer |
| Placeholder pet shape (Milestone A) | Multiple pets |
| Draggable by click | Multi-monitor (initially, single monitor) |
| Click-through toggle (Milestone B) | Diagnostics HTTP server |
| System tray icon (Milestone B) | Screenshot automation |
| Autonomous wandering (Milestone C) | Cloud sync |
| Needs + persistence (Milestone D) | macOS / Linux |

---

## Known limitations

- **Windows only.** WPF is Windows-exclusive. No cross-platform plans.
- **Single monitor for v1.** Multi-monitor wandering is deferred to post-v1.
- **No installer yet.** Run from source via `run.ps1` or Visual Studio.
- **Placeholder sprite.** The pet is currently rendered as a purple ellipse. Real pixel art / sprite sheet comes in Milestone A.
- **No tray icon in scaffold.** Added in Milestone B.
