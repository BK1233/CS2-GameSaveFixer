# Cities: Skylines II - Save Game Fixer & Analyzer (`CS2-GameSaveFixer`)

[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A dual-tier repair solution and technical toolkit for diagnosing, repairing, and sanitizing corrupted **Cities: Skylines II (CS2)** save files (`.coc` archives and `.zip` save containers).

---

## 🌟 Overview

Cities: Skylines II save files utilize Unity's **Entities (DOTS / ECS)** binary stream serialization inside compressed archive containers. Save files frequently become unreadable or crash on loading due to missing mod asset GUIDs, byte-offset misalignments, float vector anomalies (`NaN` / `Infinity`), or corrupted entity handles.

This repository provides:
1. **`CS2SaveFixer.Tool`**: A standalone C# CLI utility that inspects, validates, cleans floating-point corruptions, remaps uninstalled mod asset GUIDs, and repacks save archives offline without launching the game.
2. **`CS2SaveFixer.Mod`**: An in-game C# runtime mod framework that intercepts deserialization exceptions (`SerializerSystem`), provides dynamic fallback prefabs for missing assets, and executes post-load entity sanitization sweeps.
3. **Comprehensive Documentation**: Technical feasibility research, internal binary save format specifications, and step-by-step repair guides.

---

## 📁 Repository Structure

```
├── docs/
│   ├── TECHNICAL_FEASIBILITY.md  <- Technical analysis of CS2 save architecture & corruption feasibility
│   ├── SAVE_FORMAT.md            <- Detailed specification of CS2 binary streams & COSV archive headers
│   └── REPAIR_GUIDE.md           <- User & developer guide for offline tool and mod repair routines
├── src/
│   ├── CS2SaveFixer.Tool/        <- Standalone C# .NET 8 CLI Save Analyzer & Repair Tool
│   └── CS2SaveFixer.Mod/         <- In-game C# Runtime Mod & Unity ECS Sanitization Systems
├── tests/
│   └── CS2SaveFixer.Tests/       <- Automated xUnit unit & integration tests
└── README.md                     <- Main project documentation
```

---

## 🛠️ Usage Guidelines

### 1. Standalone Offline Tool (`CS2SaveFixer.Tool`)

Build and run the standalone CLI tool using .NET 8:

```bash
# Build the tool
dotnet build src/CS2SaveFixer.Tool/CS2SaveFixer.Tool.csproj

# Analyze a Cities Skylines II save file for corruption
dotnet run --project src/CS2SaveFixer.Tool/CS2SaveFixer.Tool.csproj -- analyze --input "path/to/MyCity.coc"

# Repair corrupted float vectors (NaN/Inf) and remap missing mod prefab GUIDs
dotnet run --project src/CS2SaveFixer.Tool/CS2SaveFixer.Tool.csproj -- fix --input "path/to/MyCity.coc" --output "path/to/MyCity_Fixed.coc" --clean-nan --remap-missing-prefabs
```

### 2. In-Game Runtime Mod (`CS2SaveFixer.Mod`)

The runtime mod hooks into the game's load workflow to provide continuous protection:
- **Fallback Prefab Resolver**: Prevents missing custom asset GUIDs from throwing `NullReferenceException` during `PrefabSystem` loading.
- **Entity Cleanup System**: Scans active simulation entities post-load to reset `NaN` coordinates and purge broken/orphaned node links.

---

## 🧪 Running Automated Tests

To run the automated test suite across save parsing, corruption detection, NaN sanitization, and fallback prefab resolution:

```bash
dotnet test tests/CS2SaveFixer.Tests/CS2SaveFixer.Tests.csproj
```

---

## 📖 Technical Documentation

For in-depth specifications and architectural analysis, refer to:
- [Technical Feasibility Report](docs/TECHNICAL_FEASIBILITY.md)
- [Save File Format Specification](docs/SAVE_FORMAT.md)
- [Save Repair Guide](docs/REPAIR_GUIDE.md)

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
