// model2obj: export a character's render model from an MCC .map file, every region and armor permutation as its own
// group. Project Proving Grounds builds the 3D bots of its profile view from these files (its tools/make_models.py and
// tools/model_pack2.py). Reads the map with Reclaimer's map library (reclaimer/, GPL-3.0).
// Copyright 2026 the Project Proving Grounds contributors. GPL-3.0 (LICENSE).
// usage: model2obj <file.map> <tag name filter> <out.obj> [--uv] [--skin] [--tex <dir>]
//   writes "g <region>|<permutation index>|<permutation name>|<material>" groups with v / vn / f lines (Halo axes,
//   world units: x forward, y left, z up), and prints every region's permutations. Options:
//   --uv          texture coordinates too (vt; faces v/vt/vn)
//   --skin        <out>.skin: the skeleton and every vertex's four bones and weights (tools/model_pack2.py poses with it)
//                 header "SKN2", int bones, per bone: name (64 bytes), int parent, 16 floats world matrix, 16 floats local
//                 matrix (row-major, System.Numerics), int vertices, per vertex: 4 bytes bone indices, 4 floats weights
//   --tex <dir>   every material's textures as PNG in <dir> and <out>.mat.json {material: [{usage, file, tiling}]}
//   (always) <out>.markers.json: the model's markers [{name, bone, region, perm, pos, rot}] when it has any
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Reclaimer.Blam.Common;
using Reclaimer.Geometry;

if (args.Length < 3) { Console.WriteLine("usage: model2obj <file.map> <tag name filter> <out.obj> [--uv] [--skin] [--tex <dir>]"); return 1; }
var cache = CacheFactory.ReadCacheFile(args[0]);
var ci = CultureInfo.InvariantCulture;
if (args[1] == "--list") {                                     // every render model whose name has the filter in it
    foreach (var t in cache.TagIndex.Where(t => (t.ClassCode == "mode" || t.ClassCode == "mod2") && t.TagName.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine(t.TagName);
    return 0;
}
if (args[1] == "--tag-at") {                                   // model2obj <map> --tag-at 66175e8: the tag whose data
    var tags = cache.TagIndex.Where(t => t.MetaPointer.Value != 0).Select(t => (t, a: t.MetaPointer.Address)).OrderBy(x => x.a).ToList();
    foreach (var h in args[2].Split(','))                       // starts last before that file offset (hex)
    {
        long off = Convert.ToInt64(h, 16);
        var hit = tags.LastOrDefault(x => x.a <= off);
        Console.WriteLine(hit.t == null ? $"{h} none" : $"{h} {hit.t.ClassCode} {hit.t.TagName} at {hit.a:x} (+{off - hit.a:x})");
    }
    return 0;
}
if (args[1] == "--addr") {                                     // model2obj <map> --addr 5f08f620: block pointers (hex)
    var any = cache.TagIndex.First(t => t.MetaPointer.Value != 0).MetaPointer;    // as file offsets
    foreach (var h in args[2].Split(','))
        Console.WriteLine($"{h} {new Reclaimer.Blam.Utilities.Pointer(Convert.ToInt32(h, 16), any).Address:x}");
    return 0;
}
if (args[1] == "--ptr") {                                      // model2obj <map> --ptr 66175e8: file offsets (hex) as
    foreach (var h in args[2].Split(','))                       // the pointer values tag data holds (Reach: no expander)
        Console.WriteLine($"{h} {cache.DefaultAddressTranslator.GetPointer(Convert.ToInt64(h, 16)):x}");
    return 0;
}
if (args[1] == "--tagnames") {                                 // model2obj <map> --tagnames <out.json>: every tag's
    var all = cache.TagIndex.Select(t => new Dictionary<string, object> // index (tag id low 16 bits), class and name
    { ["index"] = t.Id & 0xFFFF, ["class"] = t.ClassCode, ["name"] = t.TagName }).ToList();
    File.WriteAllText(args[2], JsonSerializer.Serialize(all));
    Console.WriteLine($"{all.Count} tags -> {args[2]}");
    return 0;
}
if (args[1] == "--names") {                                    // model2obj <map> --names 3962,3964: string ids (hex)
    foreach (var h in args[2].Split(','))                       // by name
        Console.WriteLine($"{h} {(cache.StringIndex.TryGetValue(Convert.ToInt32(h, 16), out var s) ? s : "?")}");
    return 0;
}
if (args[1] == "--sids") {                                     // model2obj <map> --sids a,b: each name's string id (hex)
    foreach (var s in args[2].Split(','))
        Console.WriteLine($"{s} {cache.StringIndex.GetStringId(s):x8}");
    return 0;
}
if (args[1] == "--bitmaps") {                                  // model2obj <map> --bitmaps <filter> <out dir>: every
    Directory.CreateDirectory(args[3]);                         // bitmap tag with the filter in its name, each submap a PNG
    foreach (var t in cache.TagIndex.Where(t => t.ClassCode == "bitm" && t.TagName.Contains(args[2], StringComparison.OrdinalIgnoreCase)))
    {
        if (!ContentFactory.TryGetBitmapContent(t, out var bp)) continue;
        try
        {
            var bmp = bp.GetContent();
            var stem = t.TagName.Replace('\\', '_');
            for (int k = 0; k < bmp.SubmapCount; k++)
                bmp.ToDds(k).WriteToDisk(Path.Combine(args[3], $"{stem}_{k}.png"), System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"{t.TagName}: {bmp.SubmapCount}");
        }
        catch (Exception e) { Console.WriteLine($"{t.TagName}: {e.Message}"); }
    }
    return 0;
}
bool wantUv = args.Contains("--uv"), wantSkin = args.Contains("--skin");
int ti = Array.IndexOf(args, "--tex");
string texDir = ti > 0 && ti + 1 < args.Length ? args[ti + 1] : null;
// the model whose name ends in the filter ("mp_masterchief" is the third-person one; "...pp" the arms), else contains it
var item = cache.TagIndex.FirstOrDefault(t => (t.ClassCode == "mode" || t.ClassCode == "mod2") && t.TagName.EndsWith(args[1], StringComparison.OrdinalIgnoreCase))
        ?? cache.TagIndex.FirstOrDefault(t => (t.ClassCode == "mode" || t.ClassCode == "mod2") && t.TagName.Contains(args[1], StringComparison.OrdinalIgnoreCase));
if (item == null) { Console.WriteLine($"no render model matching {args[1]}"); return 1; }
if (!ContentFactory.TryGetGeometryContent(item, out var provider)) { Console.WriteLine($"{item.TagName}: no geometry"); return 1; }
var scene = provider.GetContent();
var model = scene.EnumerateGroupHierarchy().SelectMany(g => g.ChildObjects).Select(p => p.Object).OfType<Model>().FirstOrDefault();
if (model == null) { Console.WriteLine($"{item.TagName}: no model in the scene"); return 1; }
Console.WriteLine($"{item.TagName}: {model.Regions.Count} regions, {model.Meshes.Count} meshes, {model.Bones.Count} bones");
using var w = new StreamWriter(args[2]);
int vbase = 1, tris = 0;
var skinIdx = new List<byte[]>();
var skinW = new List<float[]>();
var mats = new Dictionary<string, List<Dictionary<string, object>>>();
var texDone = new Dictionary<int, string>();
if (texDir != null) Directory.CreateDirectory(texDir);

string TexFile(Reclaimer.Geometry.Texture tex)
{
    if (tex?.ContentProvider == null) return null;
    if (texDone.TryGetValue(tex.Id, out var done)) return done;
    string name = null;
    try
    {
        var bmp = tex.ContentProvider.GetContent();
        if (bmp != null && bmp.SubmapCount > 0)
        {
            name = $"t{tex.Id}.png";
            bmp.ToDds(0).WriteToDisk(Path.Combine(texDir, name), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    catch (Exception e) { Console.WriteLine($"texture {tex.Id}: {e.Message}"); name = null; }
    texDone[tex.Id] = name;
    return name;
}

for (int ri = 0; ri < model.Regions.Count; ri++)
{
    var region = model.Regions[ri];
    Console.WriteLine($"region {ri} {region.Name}: " + string.Join(", ", region.Permutations.Select((p, k) => $"{k} {p.Name}")));
    for (int pi = 0; pi < region.Permutations.Count; pi++)
    {
        var perm = region.Permutations[pi];
        var xf = perm.GetFinalTransform();
        foreach (int mi in perm.MeshIndices)
        {
            if (mi < 0 || mi >= model.Meshes.Count) continue;
            var mesh = model.Meshes[mi];
            if (mesh?.VertexBuffer == null || mesh.IndexBuffer == null || !mesh.VertexBuffer.HasPositions) continue;
            var pos = mesh.GetPositions().ToList();
            var b = mesh.PositionBounds;
            if (!b.IsEmpty)                                    // compressed: 0..1 within the bounds
                pos = pos.Select(p => b.Min + p * (b.Max - b.Min)).ToList();
            // rigid meshes (one bone) are stored in model space like skinned ones: Reclaimer's own exporter writes them
            // as they are (an extra bone transform threw the helmets and shoulders off the body)
            var full = xf;
            var nrm = mesh.GetNormals()?.ToList();
            foreach (var p0 in pos)
            {
                var p = Vector3.Transform(p0, full);
                w.WriteLine(string.Format(ci, "v {0:0.#####} {1:0.#####} {2:0.#####}", p.X, p.Y, p.Z));
            }
            bool hasN = nrm != null && nrm.Count == pos.Count;
            if (hasN)
                foreach (var n0 in nrm)
                {
                    var n = Vector3.Normalize(Vector3.TransformNormal(n0, full));
                    w.WriteLine(string.Format(ci, "vn {0:0.####} {1:0.####} {2:0.####}", n.X, n.Y, n.Z));
                }
            bool hasT = false;
            if (wantUv)
            {
                var uv = mesh.GetTexCoords()?.ToList();
                if (uv != null && uv.Count == pos.Count)
                {
                    var tb = mesh.TextureBounds;
                    hasT = true;
                    foreach (var t0 in uv)
                    {
                        var t = tb.IsEmpty ? t0 : tb.Min + t0 * (tb.Max - tb.Min);
                        w.WriteLine(string.Format(ci, "vt {0:0.#####} {1:0.#####}", t.X, t.Y));
                    }
                }
                else
                    foreach (var _ in pos) w.WriteLine("vt 0 0");     // keeps vt numbering in step with v
                hasT = true;
            }
            if (wantSkin)
            {
                var bi = mesh.GetBlendIndices()?.ToList();
                var bw = mesh.GetBlendWeights()?.ToList();
                for (int k = 0; k < pos.Count; k++)
                {
                    if (mesh.BoneIndex.HasValue || bi == null)
                    {
                        skinIdx.Add(new byte[] { (byte)(mesh.BoneIndex ?? 0), 0, 0, 0 });
                        skinW.Add(new float[] { 1, 0, 0, 0 });
                        continue;
                    }
                    var ind = bi[k];
                    var wt = bw != null && k < bw.Count ? bw[k] : new Vector4(1, 0, 0, 0);
                    if (bw == null) wt = new Vector4(1, 0, 0, 0);
                    skinIdx.Add(new byte[] { (byte)ind.X, (byte)ind.Y, (byte)ind.Z, (byte)ind.W });
                    skinW.Add(new float[] { wt.X, wt.Y, wt.Z, wt.W });
                }
            }
            foreach (var seg in mesh.Segments)
            {
                var mname = seg.Material?.Name ?? "none";
                w.WriteLine($"g {region.Name}|{pi}|{perm.Name}|{mname}");
                if (texDir != null && seg.Material != null && !mats.ContainsKey(mname))
                    mats[mname] = seg.Material.TextureMappings.Select(m => new Dictionary<string, object> {
                        ["usage"] = m.Usage, ["file"] = TexFile(m.Texture), ["tiling"] = new[] { m.Tiling.X, m.Tiling.Y },
                        ["channel"] = m.ChannelMask.ToString(), ["bitmap"] = m.Texture?.Name ?? "" }).ToList();
                var idx = mesh.GetTriangleIndicies(seg).ToList();
                for (int t = 0; t + 2 < idx.Count; t += 3)
                {
                    int a = idx[t], c = idx[t + 1], d = idx[t + 2];
                    if (a == c || c == d || a == d || a >= pos.Count || c >= pos.Count || d >= pos.Count) continue;
                    string F(int q) => hasT ? (hasN ? $"{vbase + q}/{vbase + q}/{vbase + q}" : $"{vbase + q}/{vbase + q}")
                                            : (hasN ? $"{vbase + q}//{vbase + q}" : $"{vbase + q}");
                    w.WriteLine($"f {F(a)} {F(c)} {F(d)}");
                    tris++;
                }
            }
            vbase += pos.Count;
        }
    }
}
if (wantSkin)
{
    using var s = new BinaryWriter(File.Create(args[2] + ".skin"));
    s.Write(Encoding.ASCII.GetBytes("SKN2"));
    s.Write(model.Bones.Count);
    foreach (var bone in model.Bones)
    {
        var nb = new byte[64];
        var raw = Encoding.ASCII.GetBytes(bone.Name ?? "");
        Array.Copy(raw, nb, Math.Min(63, raw.Length));
        s.Write(nb);
        s.Write(bone.ParentIndex);
        foreach (var m in new[] { bone.WorldTransform, bone.LocalTransform })   // (SKN2: the local one too: Halo CE's
            foreach (var f in new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,  //  world ones are identity)
                                      m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 })
                s.Write(f);
    }
    s.Write(skinIdx.Count);
    for (int k = 0; k < skinIdx.Count; k++) { s.Write(skinIdx[k]); foreach (var f in skinW[k]) s.Write(f); }
    Console.WriteLine($"skin: {model.Bones.Count} bones, {skinIdx.Count} vertices -> {args[2]}.skin");
}
if (texDir != null)
{
    File.WriteAllText(args[2] + ".mat.json", JsonSerializer.Serialize(mats, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"materials: {mats.Count}, textures: {texDone.Values.Count(v => v != null)} -> {texDir}");
}
// the regions in the tag's order with their permutations by index: what a body's region bytes in game index into (the
// profile view's Reach / H2A / Halo 4 bots wear the pieces read off their bodies in a match; tools/look_record.py).
// Reach's instanced flair (the "<Instances>" group: one shoulder pad, pouch or helmet attachment each) also lists, per
// instance, the "region\permutation" pieces that wear it (the permutation's instance mask), so tools/model_pack2.py
// packs it under them and the profile view picks it with its piece
// The owners come from the tag's own masks here: Reclaimer's test ((mask & 1 << 31) > 0) loses every 32nd instance (Li's
// left shoulder pad, Kurt's helmet attachment had no owner)
var reachRm = cache.Metadata.Engine == BlamEngine.HaloReach ? item.ReadMetadata<Reclaimer.Blam.HaloReach.RenderModelTag>() : null;
List<string> OwnersOf(ModelPermutation p)
{
    if (reachRm == null || !p.CustomProperties.TryGetValue("instance_group", out var g)) return null;
    int i = Convert.ToInt32(g);
    return reachRm.Regions.SelectMany(rr => rr.Permutations, (rr, pp) => (rr, pp))
        .Where(x => (((uint)new[] { x.pp.InstanceMask1, x.pp.InstanceMask2, x.pp.InstanceMask3, x.pp.InstanceMask4 }[i / 32] >> (i % 32)) & 1) != 0)
        .Select(x => $"{x.rr.Name}\\{x.pp.Name}").ToList();
}
File.WriteAllText(args[2] + ".regions.json", JsonSerializer.Serialize(model.Regions.Select(r =>
{
    var d = new Dictionary<string, object> { ["name"] = r.Name, ["perms"] = r.Permutations.Select(p => p.Name).ToList() };
    if (r.Permutations.Any(p => OwnersOf(p) != null))
        d["owners"] = r.Permutations.Select(p => OwnersOf(p) ?? new List<string>()).ToList();
    return d;
}).ToList()));
if (model.Markers.Count > 0)
{
    // the markers (a gun's grip and fore-end, a hand's attachment point): position and rotation (x, y, z, w) in their bone's
    // space, the bone index into the skin's bones; a pose mounts the gun on the hand by them. Halo: CE stores them as the
    // conjugate of the later games' (with them as read, its left hand sat 129 degrees off the rifle's grip; conjugated,
    // 37), so they are conjugated here
    float ce = cache.Metadata.Generation == CacheGeneration.Gen1 ? -1 : 1;
    var marks = model.Markers.SelectMany(m => m.Instances.Select(i => new Dictionary<string, object>
    {
        ["name"] = m.Name, ["bone"] = i.BoneIndex, ["region"] = i.RegionIndex, ["perm"] = i.PermutationIndex,
        ["pos"] = new[] { i.Position.X, i.Position.Y, i.Position.Z },
        ["rot"] = new[] { ce * i.Rotation.X, ce * i.Rotation.Y, ce * i.Rotation.Z, i.Rotation.W },
    })).ToList();
    File.WriteAllText(args[2] + ".markers.json", JsonSerializer.Serialize(marks, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"markers: {marks.Count} -> {args[2]}.markers.json");
}
Console.WriteLine($"{tris} triangles, {vbase - 1} vertices -> {args[2]}");
return 0;
