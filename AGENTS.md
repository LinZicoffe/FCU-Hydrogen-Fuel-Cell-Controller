# Repository Guidelines

Address the user as 东哥 in replies (standing preference recorded in CLAUDE.md).

## Project Structure & Module Organization

The solution contains one .NET 8 WPF application (`net8.0-windows`, version tracked in the csproj) in `FCU 氢燃料电池控/`. UI composition lives in `MainWindow.xaml` and `Views/` (including `FirmwareUpgradeView` for RS485 Bootloader firmware upgrades), with custom drawn controls in `Controls/`; view state and commands belong in `ViewModels/`. Keep device-frame construction and parsing in `Protocol/` (`ProtocolHandler` AA55 state machine, `ModbusRtu`, `BlProtocolHelper` Bootloader frames), serial and parameter coordination in `Managers/`, application services (BMS Modbus master, BL upgrade client, licensing, recipes) in `Services/`, and reusable data types in `Models/`. `Helpers/`, `Converters/`, and `Utils/` contain narrowly scoped support code. Standalone maintenance utilities are under `Tools/LicenseKeyGenerator/` and `Tools/ClearTrialData/`. Treat `bin/`, `obj/`, `Output/`, and `publish_v*/` as generated artifacts, not source.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore "FCU 氢燃料电池控.sln"
dotnet build "FCU 氢燃料电池控.sln" -c Debug
dotnet run --project "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj"
dotnet publish "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj" -c Release -o publish_v<version>
dotnet build "Tools/LicenseKeyGenerator/LicenseKeyGenerator.csproj"
dotnet run --project "Tools/ClearTrialData/ClearTrialData.csproj"
```

`restore` resolves NuGet packages, `build` performs the normal compile check, and `run` launches the desktop client. The last command clears trial/license IsolatedStorage for dev testing. When preparing a distributable: bump `Version`/`FileVersion`/`AssemblyVersion` in the csproj, publish into a new `publish_v<version>/` folder (win-x64 self-contained), and update `setup.iss` to point at it — see `打包教程.md`. Generated files must not be mixed into feature commits.

## Protocol & Platform Constraints

- Two protocols share one serial port: the FCU uses the custom AA55 frame (big-endian, CRC16-CCITT, `AA 55 | addr | fn | len | data | CRC16`), while the BMS uses standard Modbus RTU (0x03/0x10) with the host as master. `MainWindow` feeds every received byte into both parsers; keep them strictly isolated — response codes are never shared between the two.
- BMS bus arbitration goes through FCU function 0x0D. The firmware persists the enable flag to Flash, so an abnormal host exit leaves `en=0` and the FCU stops polling BMS after reboot; the BMS tab has a manual 「归还FCU轮询」 recovery button.
- Firmware upgrade (BL/IAP): `FirmwareUpgradeView` + `BlUpgradeClient` implement the Bootloader flow (9600 handshake → device power-cycle into 115200 BL → 1024-byte block writes → SHA256/CRC32 verify → META write → reset). While an upgrade is in progress, regular AA55 requests and parameter sends are blocked. The `feature/rs485-iap` branch pairs with the same-named branch of the firmware repo.
- The authoritative protocol reference is the companion firmware repository `D:\C_C++text\450V_FCU_git` (`App/Src/fcu_protocol.c`, `fcu_control.c`, `fcu_modbus.c`) — spec documents and firmware comments can both lag behind code. UI defaults, send-side validation, and response parsing must match firmware `.c` behavior (e.g. signed stack temperature, full-frame parameter validation, alarm value units varying by alarm type). A copy of the RS485 spec PDF sits at the repo root (untracked, gitignored); customer requirement specs live in `docs/`.
- UI updates from serial callbacks must go through the Dispatcher. Polling/chart timers use silent error handling, but frame dispatch and response handling must never swallow exceptions silently.
- The license/registration feature is currently disabled via the commented-out `DefineConstants` (`ENABLE_LICENSE_FEATURE`) line in the csproj; the related code remains in the tree.
- Follow `UI设计风格文档.md` for UI work (Material blue #1976D2, HandyControl, LiveCharts2/SkiaSharp for the power chart).
- Paths contain Chinese characters and spaces — always quote them in shell commands. Git Bash renders them as mojibake; that is cosmetic only.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on new lines, nullable reference types enabled, and explicit access modifiers. Use PascalCase for types, methods, properties, events, and XAML controls; use camelCase for parameters and locals and `_camelCase` for private fields. Keep code-behind focused on UI wiring and move reusable state or protocol logic into the appropriate layer. Preserve the protocol's big-endian encoding and CRC16-CCITT behavior.

## Testing Guidelines

No automated test project is currently configured. Every change must at least pass a Debug build. For protocol changes, verify representative request and response bytes against the RS485 specification and the firmware implementation, including CRC, lengths, offsets, and partial-frame reception. For UI or serial changes, manually test connection, disconnect, polling shutdown, relevant tabs, and error messages with an FCU/BMS device — or document in the commit/PR why hardware validation was unavailable. BL upgrade changes additionally require the matching firmware branch and a real `.upg` package.

## Commit & Pull Request Guidelines

History uses concise Conventional Commit subjects such as `fix: ...`, `feat: ...`, and `docs: ...`; continue that pattern and keep each commit single-purpose. **Before every `git push`, update `README.md`** — add entries under 「更新日志」 for the changes being pushed (mandatory repo rule from CLAUDE.md; applies no matter how small the change). Pull requests should describe behavior changed, affected function codes or screens, validation performed, and hardware limitations. Link the issue when available and include screenshots for visible XAML changes. Never commit private keys or license material (e.g. `密钥*.txt`, `*.key`, `*.license` — also gitignored), machine-specific state, or generated `bin/`, `obj/`, `publish*/`, and `Output/` contents.
