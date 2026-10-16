# Repository Guidelines

Address the user as 东哥 in replies (standing preference recorded in CLAUDE.md).

## Project Structure & Module Organization

The solution contains one .NET 8 WPF application (`net8.0-windows`, version tracked in the csproj) in `FCU 氢燃料电池控/`. UI composition lives in `MainWindow.xaml` and `Views/` (including `FirmwareUpgradeView` for RS485 Bootloader firmware upgrades), with custom drawn controls in `Controls/`; view state and commands belong in `ViewModels/`. Keep device-frame construction and parsing in `Protocol/` (`ProtocolHandler` AA55 state machine, `ModbusRtu`, `OpenBltXcpClient` OpenBLT/XCP upgrade frames, legacy `BlProtocolHelper`), serial and parameter coordination in `Managers/`, application services (BMS Modbus master, legacy BL upgrade client, licensing, recipes) in `Services/`, and reusable data types in `Models/` (including `SrecFile` for OpenBLT S-record images and legacy `BlUpgFile`). `Helpers/`, `Converters/`, and `Utils/` contain narrowly scoped support code. Standalone maintenance utilities are under `Tools/LicenseKeyGenerator/` and `Tools/ClearTrialData/`. Treat `bin/`, `obj/`, `Output/`, and `publish_v*/` as generated artifacts, not source.

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

`restore` resolves NuGet packages, `build` performs the normal compile check, and `run` launches the desktop client. The last command clears trial/license IsolatedStorage for dev testing. When preparing a distributable: bump `Version`/`FileVersion`/`AssemblyVersion` in the csproj, publish into a new `publish_v<version>/` folder (win-x64 self-contained), and update the matching Inno Setup script — `setup.iss` for the standard package, `setup_众赢普惠.iss` for the 众赢普惠 variant (published into `publish_v<version>_zyph/`) — see `打包教程.md`. Generated files must not be mixed into feature commits.

## Protocol & Platform Constraints

- Two protocols share one serial port: the FCU uses the custom AA55 frame (big-endian, CRC16-CCITT, `AA 55 | addr | fn | len | data | CRC16`), while the BMS uses standard Modbus RTU (0x03/0x10) with the host as master. `MainWindow` feeds every received byte into both parsers; keep them strictly isolated — response codes are never shared between the two.
- The firmware pushes unsolicited AA55 state frames: `0x4842` is a ~5s RS485 heartbeat that must be silently ignored (never surfaced as status), and `0x0105` carries the low-pressure relief protection bit (「未触发/泄压中」 display branch in `MainWindow`). Alarm display values are formatted per alarm code — `0x000A`/`0x000C` are raw KPA, not scaled (see `ProtocolModels.cs`).
- BMS monitoring defaults to FCU cache query: empty AA55 `0x11`, 18-byte `0x91` response. Current/voltage/status/SOC validity and cache age come from the firmware; data expires after 6000ms including elapsed host time. Do not disable FCU polling on normal connect/close. Direct Modbus diagnostics and MOS control remain behind an explicit checkbox and successful `0x0D en=0` takeover; return to the saved owner on exit. Mode/target/connection changes permanently cancel pending takeover waits.
- Standby sensor period uses `0x0B+0x0902` (1..65535 integer seconds, OFF only), read through `0x0A/0x8A`; keep `0x0901` in 0.1-second units. A `StandbyPeriodSession` reserves the ordinary serial channel until write-result/readback confirmation or timeout. Check actual send results, pause auto queries, and reset sessions on disconnect, target changes and upgrades. Safety stop and returning BMS polling remain allowed.
- Firmware upgrade remains for devices already equipped with compatible OpenBLT XCP over RS485 (57600bps) and matching S-record images based at `0x08003000`; keep the S-record address/checksum guards. Current firmware main@2c8be1d is a standalone APP linked at `0x08000000`, has no boot-entry command, and requires SWD programming of that standalone image. Never emit guessed raw boot bytes or interpret BMS `0x91` as an IAP acknowledgement. The old `BlProtocolHelper`/`BlUpgFile`/`BlUpgradeClient` files remain offline. Before prechecks, finish pending parameter/response transactions, pause polling and lock ordinary sends; release the lock on every exit. Existing historical upgrade docs are superseded where noted by `docs/实施步骤-固件主线20261016适配.md`.
- The authoritative protocol reference is the companion firmware repository `D:\C_C++text\450V_FCU_git` (`App/Src/fcu_protocol.c`, `fcu_control.c`, `fcu_modbus.c`); this batch is pinned to main@2c8be1d in `D:\C_C++text\worktrees\fcu-main-20261016` — spec documents and firmware comments can both lag behind code. UI defaults, send-side validation, and response parsing must match firmware `.c` behavior (e.g. signed stack temperature, full-frame parameter validation, alarm value units varying by alarm type). A copy of the RS485 spec PDF sits at the repo root (untracked, gitignored); customer requirement specs live in `docs/`.
- Every feature batch leaves a paper trail: a root-level 「xxx功能更新日志.md」 plus `docs/实施步骤-*.md` and `docs/评审记录-*.md` (e.g. `OpenBLT升级与固件适配功能更新日志.md`, `低压分级泄压保护适配功能更新日志.md`). Read the matching changelog before modifying that area, and produce the same doc set when landing a new batch.
- UI updates from serial callbacks must go through the Dispatcher. Polling/chart timers use silent error handling, but frame dispatch and response handling must never swallow exceptions silently.
- The license/registration feature is currently disabled via the commented-out `DefineConstants` (`ENABLE_LICENSE_FEATURE`) line in the csproj; the related code remains in the tree.
- Follow `UI设计风格文档.md` for UI work (Material blue #1976D2, HandyControl, LiveCharts2/SkiaSharp for the power chart).
- Paths contain Chinese characters and spaces — always quote them in shell commands. Git Bash renders them as mojibake; that is cosmetic only.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on new lines, nullable reference types enabled, and explicit access modifiers. Use PascalCase for types, methods, properties, events, and XAML controls; use camelCase for parameters and locals and `_camelCase` for private fields. Keep code-behind focused on UI wiring and move reusable state or protocol logic into the appropriate layer. Preserve the protocol's big-endian encoding and CRC16-CCITT behavior.

## Testing Guidelines

Run `dotnet run --project Tools/FirmwareAdaptationChecks/FirmwareAdaptationChecks.csproj -c Debug -- --ui` for protocol, transaction and real WPF event checks with a fake transport (no device writes). Every change must at least pass a Debug build. For protocol changes, verify representative request and response bytes against the RS485 specification and the firmware implementation, including CRC, lengths, offsets, and partial-frame reception. For UI or serial changes, manually test connection, disconnect, polling shutdown, relevant tabs, and error messages with an FCU/BMS device — or document in the commit/PR why hardware validation was unavailable. Upgrade-path changes additionally require the matching firmware branch and a real firmware image (`.srec` for the OpenBLT XCP path, `.upg` for legacy BL).

## Commit & Pull Request Guidelines

History uses concise Conventional Commit subjects such as `fix: ...`, `feat: ...`, and `docs: ...`; continue that pattern and keep each commit single-purpose. **Before every `git push`, update `README.md`** — add entries under 「更新日志」 for the changes being pushed (mandatory repo rule from CLAUDE.md; applies no matter how small the change). Pull requests should describe behavior changed, affected function codes or screens, validation performed, and hardware limitations. Link the issue when available and include screenshots for visible XAML changes. Never commit private keys or license material (e.g. `密钥*.txt`, `*.key`, `*.license` — also gitignored), machine-specific state, or generated `bin/`, `obj/`, `publish*/`, and `Output/` contents.
