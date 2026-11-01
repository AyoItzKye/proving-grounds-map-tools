// bsp2obj: export the level geometry (every structure BSP, with its instanced geometry) of an MCC
// .map file to one OBJ, using Reclaimer's map library (reclaimer/, GPL-3.0). For the games whose
// editing kits cannot export level meshes (ODST, H2A, H4).
// Copyright 2026 the Project Proving Grounds contributors. GPL-3.0 (LICENSE).
// usage: bsp2obj <file.map> <out.obj> [bsp name filter] [--glass <glass.txt>] [--list]
//   --glass  also write every see-through triangle (a material named glass, window, fog or
//            breakable) as one line of nine
//            numbers, Halo axes (x y z x y z x y z): tools/glass_mask.py makes the map's glass mask
//   --list   print triangles per material (name, alpha mode, see-through or not)
// The OBJ is written in the editing kits' axes (Y up: v X Z -Y) so tools/heightmap.py reads it
// like the kits' own exports; units are world units.
using System.Globalization;
using System.Numerics;
using Reclaimer.Blam.Common;
using Reclaimer.Geometry;

var pos_args = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || args[i - 1] != "--glass")).ToList();
if (pos_args.Count < 2) { Console.WriteLine("usage: bsp2obj <file.map> <out.obj> [bsp name filter] [--glass <glass.txt>] [--list]"); return 1; }
int gi = Array.IndexOf(args, "--glass");
string glassPath = gi >= 0 && gi + 1 < args.Length ? args[gi + 1] : null;
bool list = args.Contains("--list");
var cache = CacheFactory.ReadCacheFile(pos_args[0]);
string filter = pos_args.Count > 2 ? pos_args[2] : null;
using var w = new StreamWriter(pos_args[1]);
using var gw = glassPath != null ? new StreamWriter(glassPath) : null;
var ci = CultureInfo.InvariantCulture;
var perMaterial = new Dictionary<string, (string alpha, bool see, int tris)>();
int vbase = 1, tris = 0, bsps = 0, glassTris = 0;

static bool SeeThrough(Material m)
{
    if (m == null) return false;
    // by name: alpha-blended materials are mostly decals laid over solid walls (dirt skirts, paint
    // chips, vines, graffiti: 61% of Coagulation's walls), so blend mode alone means nothing
    string n = (m.Name ?? "").ToLowerInvariant();
    return n.Contains("glass") || n.Contains("window") || n.Contains("fog") || n.Contains("breakable");
}

foreach (var item in cache.TagIndex)
{
    if (item.ClassCode != "sbsp") continue;
    if (filter != null && !item.TagName.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
    if (!ContentFactory.TryGetGeometryContent(item, out var provider)) { Console.WriteLine($"skip {item.TagName}: no geometry"); continue; }
    Scene scene;
    try { scene = provider.GetContent(); }
    catch (Exception e) { Console.WriteLine($"skip {item.TagName}: {e.Message}"); continue; }
    bsps++;
    int before = tris;
    foreach (var group in scene.EnumerateGroupHierarchy())
    {
        foreach (var placement in group.ChildObjects)
        {
            if (placement.Object is not Model model) continue;
            foreach (var region in model.Regions)
            foreach (var perm in region.Permutations)
            {
                var xf = perm.GetFinalTransform() * placement.Transform;
                foreach (int mi in perm.MeshIndices)
                {
                    if (mi < 0 || mi >= model.Meshes.Count) continue;
                    var mesh = model.Meshes[mi];
                    if (mesh?.VertexBuffer == null || mesh.IndexBuffer == null || !mesh.VertexBuffer.HasPositions) continue;
                    var pos = mesh.GetPositions().ToList();
                    var b = mesh.PositionBounds;
                    if (!b.IsEmpty)                        // compressed: 0..1 within the bounds
                        pos = pos.Select(p => b.Min + p * (b.Max - b.Min)).ToList();
                    var world = pos.Select(p0 => Vector3.Transform(p0, xf)).ToList();
                    foreach (var p in world)
                        w.WriteLine(string.Format(ci, "v {0:0.####} {1:0.####} {2:0.####}", p.X, p.Z, -p.Y));
                    foreach (var seg in mesh.Segments)
                    {
                        bool see = SeeThrough(seg.Material);
                        var idx = mesh.GetTriangleIndicies(seg).ToList();
                        int segTris = 0;
                        for (int t = 0; t + 2 < idx.Count; t += 3)
                        {
                            int a = idx[t], c = idx[t + 1], d = idx[t + 2];
                            if (a == c || c == d || a == d || a >= pos.Count || c >= pos.Count || d >= pos.Count) continue;
                            w.WriteLine($"f {vbase + a} {vbase + c} {vbase + d}");
                            tris++; segTris++;
                            if (see && gw != null)
                            {
                                var (A, C, D) = (world[a], world[c], world[d]);
                                gw.WriteLine(string.Format(ci, "{0:0.###} {1:0.###} {2:0.###} {3:0.###} {4:0.###} {5:0.###} {6:0.###} {7:0.###} {8:0.###}",
                                                           A.X, A.Y, A.Z, C.X, C.Y, C.Z, D.X, D.Y, D.Z));
                                glassTris++;
                            }
                        }
                        if (list)
                        {
                            string key = seg.Material?.Name ?? "(none)";
                            perMaterial.TryGetValue(key, out var e);
                            perMaterial[key] = (seg.Material?.AlphaMode ?? "", see, e.tris + segTris);
                        }
                    }
                    vbase += pos.Count;
                }
            }
        }
    }
    Console.WriteLine($"{item.TagName}: {tris - before} triangles");
}
if (list)
    foreach (var kv in perMaterial.OrderByDescending(kv => kv.Value.see).ThenByDescending(kv => kv.Value.tris).Take(60))
        Console.WriteLine($"  {(kv.Value.see ? "SEE-THROUGH" : "solid      ")} {kv.Value.tris,7}  {kv.Value.alpha,-8} {kv.Key}");
Console.WriteLine($"{bsps} bsps, {tris} triangles ({glassTris} see-through), {vbase - 1} vertices -> {pos_args[1]}");
return 0;
