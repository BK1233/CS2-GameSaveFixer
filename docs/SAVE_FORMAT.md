# Cities: Skylines 2 - Save File Format Specification

## Overview

Cities: Skylines 2 (CS2) save files are packaged as archive containers with the `.coc` extension or standard compressed ZIP archives. The internal structure uses Colossal Order's asset database specification (`Colossal.IO.AssetDatabase`) alongside Unity's Entities (DOTS/ECS) binary serialization format.

---

## Container Layout

```
[Save File: MyCity.coc / .zip]
├── metadata.json           <- Human-readable save summary & active mod list
├── metadata.bin            <- Binary serialized save metadata header
├── preview.png             <- Thumbnail screenshot
├── savegame.cid            <- Content Index Directory (Stream offsets & GUID map)
└── Data/
    ├── Entities.bin        <- Unity ECS Entity & Component binary data chunks
    ├── Simulation.bin      <- Simulation state (time, weather, budget, policy)
    ├── Prefabs.bin         <- Active prefab GUID mapping table
    └── Geometry.bin        <- Map terrain, network mesh, and node geometry index
```

---

## Component Binary Serialization

CS2 binary streams write components sequentially by Archetype chunk.

### Header Format
- **Magic Bytes**: `43 4F 53 56` (`COSV` - Colossal Order Save)
- **Version**: 32-bit integer (e.g. `1012`)
- **Archetype Count**: 32-bit integer

### Entity Component Chunk Structure
Each archetype chunk contains:
1. **Component Count**: Number of components per entity in this archetype.
2. **Component Type Hash List**: Array of 64-bit component type hashes.
3. **Entity Data Array**: Sequential binary bytes representing component structs.

```
+-------------------------------------------------------------------+
| Magic: 'COSV' | Version: int32 | ArchetypeCount: int32             |
+-------------------------------------------------------------------+
| Archetype #0 Component Hashes: [Hash1, Hash2, Hash3, ...]        |
| Entity 0 Data: [Struct1 Data][Struct2 Data][Struct3 Data]         |
| Entity 1 Data: [Struct1 Data][Struct2 Data][Struct3 Data]         |
+-------------------------------------------------------------------+
```

---

## Known Serialization Pitfalls & Fixes

1. **Unregistered Component Hash**:
   - *Issue*: Loading a save without the mod that introduced the component causes `UnknownComponentTypeException`.
   - *Fix*: The tool detects unmapped component hashes and bypasses the byte payload using the chunk component size index.

2. **Floating-Point Anomaly (`NaN`/`Infinity`)**:
   - *Issue*: Physics/Transform data corrupts to `NaN` (0x7FC00000) or `Infinity` (0x7F800000).
   - *Fix*: Scans single/double precision floats; replaces invalid coordinates with `(0, 0, 0)` vector values.

3. **Missing Prefab GUIDs**:
   - *Issue*: Prefab table references missing mod GUID `00000000-0000-0000-0000-000000000000` or uninstalled package.
   - *Fix*: Remaps missing GUIDs to default base game prefabs (e.g., default road, building, or tree prefab).
