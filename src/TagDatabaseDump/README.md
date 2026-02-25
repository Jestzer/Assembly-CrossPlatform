# TagDatabaseDump

A CLI tool that exports all tags from Halo `.map` cache files into ForgeX-compatible XML, including sandbox (forge) palette data extracted from the scenario tag.

## Supported Games

Loads cache files for Halo CE, Halo 2, Halo 3, Halo Reach, Halo 4, and their MCC variants via the Blamite library. Palette extraction currently supports:

- **Halo 3 / Reach** &mdash; 7 fixed palette reflexives in the scenario tag (Vehicle, Weapon, Equipment, Scenery, Teleporter, Goal, Spawner)
- **Halo 4 MCC** &mdash; nested "Map Variant Palettes" reflexive with categories, entries, and per-entry variants

The tool auto-detects the format: it tries H4 first, then falls back to the H3/Reach layout.

## Usage

```
TagDatabaseDump <map file> [output xml file]
```

| Argument | Required | Description |
|---|---|---|
| `<map file>` | Yes | Path to the `.map` cache file to process. |
| `[output xml file]` | No | Output XML path. Defaults to `<internal_map_name>.xml` in the current directory. |

### Examples

```bash
# Writes guardian.xml (derived from the map's internal name)
TagDatabaseDump guardian.map

# Explicit output path
TagDatabaseDump guardian.map ~/output/guardian_tags.xml
```

## Output Format

The output is an XML file with no XML declaration, 2-space indentation:

```xml
<Map Map="guardian" TagCount="4231">
  <!-- One element per valid tag in the cache -->
  <Tag Class="weap" Path="objects/weapons/rifle/assault_rifle/assault_rifle" Ident="20185" />
  <Tag Class="bipd" Path="objects/characters/masterchief/masterchief" Ident="19872" />
  ...

  <Palettes>
    <!-- H3/Reach: one Palette element per category -->
    <Palette Category="Vehicle">
      <Entry Ident="20001" Name="mongoose" MaxAllowed="8" Cost="30" />
      ...
    </Palette>

    <!-- H4 MCC: entries may contain nested Variants -->
    <Palette Category="vehicles">
      <Entry Name="mongoose" MaxAllowed="8" Cost="30">
        <Variant Name="default" />
        <Variant Name="oynx" />
      </Entry>
      ...
    </Palette>
  </Palettes>
</Map>
```

### Tag Element Attributes

| Attribute | Description |
|---|---|
| `Class` | 4-character tag group code (e.g. `weap`, `bipd`, `scnr`). |
| `Path` | Full tag path/name, or `UNK` if the name couldn't be resolved. |
| `Ident` | Datum index (tag identity value) as a signed integer. |

### Entry Attributes

| Attribute | Present In | Description |
|---|---|---|
| `Ident` | H3/Reach only | Datum index of the referenced tag. |
| `Name` | Both | Display name resolved from the map's StringID table. |
| `MaxAllowed` | Both | Maximum instances allowed on the map. |
| `Cost` | Both | Forge budget cost per instance (float for H3, int for H4). |

## How It Works

1. Loads engine format definitions from `Formats/Engines.xml` (bundled by Blamite at build time).
2. Opens the `.map` file with a big-endian binary reader and loads it via `CacheFileLoader`.
3. Iterates every tag in the cache, writing `<Tag>` elements for each valid entry.
4. Locates the scenario (`scnr`) tag and reads its palette data:
   - **H4 path**: Reads a top-level tag block reflexive at offset `0x2C4` in the scenario. Each category (`0x14` bytes) contains a StringID name and a nested entries reflexive. Each entry (`0x1C` bytes) has a StringID name, MaxAllowed, Price, and an optional variants sub-block (`0x48` bytes per variant).
   - **H3/Reach path**: Reads 7 independent tag block reflexives at fixed offsets (`0x1E0`&ndash;`0x228`). Each entry (`0x1C` bytes) contains a tag reference (datum index at `+0xC`), a StringID display name, MaxAllowed, and a float Cost.
5. All pointer reads use Blamite's `PointerExpander` and `MetaArea` for correct address translation in ThirdGen (Halo 3+) maps.

## Building

```bash
# From the repository root
dotnet build src/TagDatabaseDump/TagDatabaseDump.csproj

# Or run directly
dotnet run --project src/TagDatabaseDump/TagDatabaseDump.csproj -- mymap.map
```

The project targets .NET 8 and depends only on the sibling `Blamite` library (referenced as a project dependency).
