# Repository Guidelines

## Project Structure & Module Organization

The solution contains one .NET 8 WPF application in `FCU 氢燃料电池控/`. UI composition lives in `MainWindow.xaml` and `Views/`; view state and commands belong in `ViewModels/`. Keep device-frame construction and parsing in `Protocol/`, serial and parameter coordination in `Managers/`, application services in `Services/`, and reusable data types in `Models/`. `Helpers/`, `Converters/`, and `Utils/` contain narrowly scoped support code. Standalone maintenance utilities are under `Tools/LicenseKeyGenerator/` and `Tools/ClearTrialData/`. Treat `publish/` and `Output/` as generated artifacts, not source.

## Build, Test, and Development Commands

Run commands from the repository root:

```powershell
dotnet restore "FCU 氢燃料电池控.sln"
dotnet build "FCU 氢燃料电池控.sln" -c Debug
dotnet run --project "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj"
dotnet publish "FCU 氢燃料电池控/FCU 氢燃料电池控.csproj" -c Release -o publish
dotnet build "Tools/LicenseKeyGenerator/LicenseKeyGenerator.csproj"
```

`restore` resolves NuGet packages, `build` performs the normal compile check, and `run` launches the desktop client. Publish only when preparing a distributable; generated files should not be mixed into feature commits.

## Coding Style & Naming Conventions

Follow the existing C# style: four-space indentation, braces on new lines, nullable reference types enabled, and explicit access modifiers. Use PascalCase for types, methods, properties, events, and XAML controls; use camelCase for parameters and locals and `_camelCase` for private fields. Keep code-behind focused on UI wiring and move reusable state or protocol logic into the appropriate layer. Preserve the protocol's big-endian encoding and CRC16-CCITT behavior.

## Testing Guidelines

No automated test project is currently configured. Every change must at least pass a Debug build. For protocol changes, verify representative request and response bytes against the RS485 specification, including CRC, lengths, offsets, and partial-frame reception. For UI or serial changes, manually test connection, disconnect, polling shutdown, relevant tabs, and error messages with an FCU/BMS device or document why hardware validation was unavailable.

## Commit & Pull Request Guidelines

History uses concise Conventional Commit subjects such as `fix: ...`, `feat: ...`, and `docs: ...`; continue that pattern and keep each commit single-purpose. Pull requests should describe behavior changed, affected function codes or screens, validation performed, and hardware limitations. Link the issue when available and include screenshots for visible XAML changes. Never commit private keys, license files, machine-specific state, or generated `bin/`, `obj/`, `publish/`, and `Output/` contents.
