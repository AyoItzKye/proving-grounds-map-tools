# Proving Grounds Map Tools

Two small command-line exporters that read Halo: The Master Chief Collection's `.map` files, used by
Project Proving Grounds (PvP bots for MCC) to build data from each player's own copy of the game:

| Program | What it writes | Used by Project Proving Grounds for |
|---|---|---|
| `bsp2obj.exe` | a map's level geometry (every structure BSP and its instanced geometry) as one OBJ, and optionally its see-through triangles | rebuilding map data (height maps, glass masks) for Halo 2 Anniversary, Halo 4 and ODST, whose official mod kits cannot export these meshes |
| `model2obj.exe` | a character's render model with every armor permutation, skeleton, skin weights, markers and textures | the 3D bots in the profile view (built once on each install) |

They are a separate project because they are built on [Reclaimer](https://github.com/Gravemind2401/Reclaimer), a Halo
map library under the GNU General Public License v3.0, so these two programs are GPL-3.0 as well (LICENSE). Project
Proving Grounds itself is under the PolyForm Noncommercial License and only runs these programs as separate
processes; it contains none of their code.

## Using them

Project Proving Grounds' installer puts the built programs in its `tools\map_tools` folder. To use a build of your
own, put `bsp2obj.exe` and `model2obj.exe` (with the rest of `bin\`) there, or set the environment variable
`HB_MAP_TOOLS` to the folder that holds them.

```
bsp2obj <file.map> <out.obj> [bsp name filter] [--glass <glass.txt>] [--list]
model2obj <file.map> <tag name filter> <out.obj> [--uv] [--skin] [--tex <dir>]
model2obj <file.map> --list <filter>          render models whose tag name contains the filter
model2obj <file.map> --bitmaps <filter> <dir> every bitmap whose tag name contains the filter, as PNG
```

Each program's full option list is at the top of its `Program.cs`.

## Building

Needs Windows, Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone --recurse-submodules <this repository>
cd proving-grounds-map-tools
build.bat
```

`build.bat` publishes both programs into `bin\` as self-contained x64 programs, so they run without a .NET install.
The `reclaimer` submodule is pinned to the Reclaimer commit these programs were tested with.

## License and credits

GPL-3.0 (LICENSE). Reclaimer (c) Gravemind2401 and contributors, GPL-3.0. Halo: The Master Chief Collection
(c) Microsoft Corporation; these programs read files from a copy of the game you own and ship no game content, and
they are not endorsed by or affiliated with Microsoft.
