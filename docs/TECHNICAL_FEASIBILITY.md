# Cities: Skylines 2 - Save File Fixer: Technical Feasibility Analysis

## Executive Summary

**Is it possible to create a mod or tool for Cities: Skylines 2 (CS2) that can fix and repair broken game saves and files?**

**Answer: YES.**
It is entirely feasible to build both an offline standalone repair tool and an in-game C# mod to inspect, diagnose, repair, and sanitize broken Cities: Skylines 2 save files.

Cities: Skylines 2 utilizes Unity's **Entities (DOTS / ECS)** architecture combined with Colossal Order's proprietary serialization framework (`Colossal.Serialization.Entities` and `Colossal.IO.AssetDatabase`). Save files are packaged as compressed archives (`.coc` or standard ZIP containers containing metadata, preview thumbnails, entity chunk binary streams, and index files).

Save corruption in CS2 primarily falls into four distinct categories:
1. **Missing Prefab / Asset References:** A save references custom assets or modded entity prefabs that were removed or uninstalled, causing null pointer exceptions during entity deserialization.
2. **Component Deserialization Stream Mismatches:** Mismatched component type IDs or modified assembly versions causing byte-offset misalignment during stream reading.
3. **Invalid / Corrupted Entity State Data:** Entities containing `NaN`, `Infinity`, invalid vector coordinates, or broken entity index handles (orphaned simulation components).
4. **Truncated / Corrupted Archive Containers:** File write failures during autosave/manual save leaving unclosed ZIP streams or corrupted CRC headers.

Both **offline tool-based repair** (manipulating binary streams, removing bad component blocks, or fixing archive structures) and **runtime mod-based repair** (intercepting `SaveGameSystem` deserialization, catching entity load exceptions, substituting fallback prefabs, and running post-load entity cleanup sweeps) are fully achievable.

---

## 1. Cities: Skylines 2 Save Architecture

Cities: Skylines 2 save files (typically stored in `%LOCALAPPDATA%Low\Colossal Order\Cities Skylines II\Saves\<SteamID>\`) consist of an archive container containing structured streams:

```
[Save Archive (.coc / .zip)]
 ├── SaveGameMetaData / metadata.json  (Save info: city name, population, date, version, mods list)
 ├── CustomData /                      (Embedded thumbnails, custom map settings)
 ├── world.cid / savegame.cid          (Content Index Directory / Stream Table of Contents)
 └── Data /                            (Compressed ECS Entity Component binary streams)
```

### Key Framework Components
- **`Colossal.IO.AssetDatabase`**: Manages save assets, metadata indices, and virtual stream handles.
- **`Colossal.Serialization.Entities`**:
  - `BinaryReader` & `BinaryWriter`: Handles low-level scalar, array, string, and entity reference reads/writes.
  - `SerializerSystem`: Manages entity component serialization across world chunks.
  - `Context`: Stores versioning, assembly definitions, component index maps, and prefab GUID lookups.
- **Unity Entities (DOTS / ECS)**:
  - World snapshot serialization writes component data grouped by Archetype (arrays of components for contiguous memory).

---

## 2. Root Causes of Save File Corruption

| Corruption Type | Root Cause | Failure Stage | Repair Feasibility |
|---|---|---|---|
| **Missing Mod / Asset GUID** | Save references prefab GUIDs from an uninstalled mod or custom asset. | Entity Deserialization (`PrefabSystem`) | **High** (Substitute dummy prefab or skip component) |
| **Stream Misalignment** | A mod changed its serialized struct size without incrementing version. | Component Reader (`BinaryReader`) | **High** (Re-align stream offset or dump corrupt chunk) |
| **NaN / Invalid Coordinates** | Pathfinding / physics glitch wrote `NaN` or `Inf` to `Transform` components. | Simulation Tick / Load Render | **High** (Reset vector coordinates or destroy bad entity) |
| **Broken Entity Handle** | Entity index points to a non-existent or recycled entity sequence. | System Update / System State | **High** (Clean up orphaned entity links) |
| **Truncated File** | Game crashed mid-save, creating corrupt ZIP structure or header. | Archive Extraction | **Medium** (Extract raw data stream & re-archive) |

---

## 3. Repair Strategies & Methodologies

### A. Offline Standalone Tool Repair (`CS2SaveFixer.Tool`)
1. **Container Inspection & Decompression:**
   - Unpack archive streams into memory.
   - Reconstruct corrupt ZIP headers if truncated using stream markers.
2. **Metadata & CID Sanitization:**
   - Parse `metadata.json` / `.cid` indices.
   - Audit missing mod GUID dependencies and clean or remap missing asset references.
3. **Binary Component Stream Processing:**
   - Scan binary chunk buffers for component type hashes.
   - Detect out-of-range floats (`NaN`, `Infinity`) in position/velocity vectors and sanitize them to `(0,0,0)`.
   - Strip unrecognized mod component blocks from archetypes while preserving core game simulation entities.
4. **Repacking:**
   - Re-encode updated ECS streams and update index checksums.

### B. In-Game Mod Repair (`CS2SaveFixer.Mod`)
1. **Deserialization Interception:**
   - Patch `Colossal.Serialization.Entities.SerializerSystem` or `SaveGameSystem` via Harmony.
   - Wrap entity component deserialization in `try-catch` blocks to prevent catastrophic save load crashes.
2. **Fallback Prefab Resolver:**
   - Register a fallback dummy prefab for missing modded asset GUIDs so the game loads instead of throwing null references.
3. **Post-Load Cleanup Sweeps:**
   - Run an automated ECS System (`SaveFixerCleanupSystem`) immediately after load:
     - Query all `Transform` components for `NaN` / infinite coordinates and fix or destroy affected entities.
     - Detect orphaned transit, building, or citizen links and clean references.
     - Reset broken pathfinding / traffic graph nodes.

---

## 4. Architectural Feasibility Matrix

| Approach | Advantages | Limitations | Target Use Case |
|---|---|---|---|
| **Standalone C# Tool** | Works offline; no game launch required; can fix unloadable saves; full stream control. | Requires complete binary structure parsing; cannot execute full game code routines. | Severely corrupted files, archive recovery, offline batch cleaning. |
| **In-Game C# Mod** | Access to live game engine, Unity ECS `EntityManager`, `PrefabSystem`, and simulation. | Requires the game to boot up to loading screen; cannot fix corrupted ZIP archives before extract. | Missing mod/asset fixes, runtime entity cleanup, automatic protection. |
| **Combined Solution** | Hybrid approach guarantees recovery from severe archive crash up to runtime entity cleanup. | Slightly larger project footprint. | **Recommended Architecture** |

---

## 5. Technical Implementation Roadmap

1. **`src/CS2SaveFixer.Tool/`**: Standalone C# .NET console/GUI tool for stream inspection, missing asset stripping, NaN cleanup, and repackaging.
2. **`src/CS2SaveFixer.Mod/`**: Unity ECS mod providing runtime exception trapping, missing prefab fallback handlers, and post-load entity sanitization systems.
3. **`tests/`**: Unit tests verifying archive unpacking, stream parsing, binary component fixing, NaN sanitization, and repacking integrity.
