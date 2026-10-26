# Cities: Skylines 2 - Save Repair & Recovery Guide

## Quick Start Guide

This guide describes how to use both the standalone offline tool (`CS2SaveFixer.Tool`) and the in-game mod (`CS2SaveFixer.Mod`) to repair corrupted Cities: Skylines 2 save files.

---

## Method 1: Using the Standalone Offline Tool

Use the standalone tool if your save file crashes the game before reaching the loading screen or fails during initial deserialization.

### Usage Syntax
```bash
# Analyze a save file for errors
CS2SaveFixer.Tool analyze --input "C:/Users/.../Saves/MyCity.coc"

# Repair a corrupted save file
CS2SaveFixer.Tool fix --input "C:/Users/.../Saves/MyCity.coc" --output "C:/Users/.../Saves/MyCity_Fixed.coc" --clean-nan --remap-missing-prefabs
```

### Options & Features
- `--analyze`: Validates archive headers, CID index, and component streams without making changes.
- `--clean-nan`: Scans `Transform` and `Velocity` component buffers for `NaN` and `Infinity` values and sets them to valid vectors.
- `--remap-missing-prefabs`: Replaces missing mod asset GUIDs with base game default fallback GUIDs.
- `--strip-orphan-entities`: Removes entity chunks referencing non-existent prefab IDs.

---

## Method 2: Using the In-Game Mod

Use the in-game mod for dynamic protection during loading and runtime cleanup of simulation glitches.

### Features
1. **Safe Load Deserializer**: Intercepts `SerializerSystem` deserialization errors; catches missing prefab exceptions and assigns fallback prefabs automatically.
2. **Post-Load Cleanup System**: Runs an automated ECS cleanup pass after save loading complete:
   - Purges orphaned entities (e.g. broken path nodes, deleted vehicle links).
   - Resets invalid floating-point coordinates.
3. **In-Game Repair Panel**: Access via Options -> CS2 Save Fixer -> `Run Emergency City Sanitize`.

---

## Preventative Best Practices

1. Always keep backups of your saves before installing or removing major simulation mods.
2. Ensure mods are updated for your current game version.
3. If removing custom asset mods, use `CS2SaveFixer.Tool` or enable `Fallback Asset Mode` before loading the city.
