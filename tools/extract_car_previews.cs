#:property JsonSerializerIsReflectionEnabledByDefault=true
// Extracts the SMGP car PREVIEW images from the installed skinpack and saves them into the app so
// the briefing rival dossier + wizard cards can show each car. Each livery override XML carries a
// <PREVIEWIMAGE PATH="...preview <NAME> V1.dds"> — a pre-rendered car thumbnail (DXT5/BC3) — which
// this decodes to PNG at data/ams2/cars/<driverId>.png (keyed by the pack driver whose entry uses
// that livery). Reads the pack's entries.json + pack.json moddedField for the livery -> driver map.
//
// Output is a USER ASSET (the skins are rafaelcsanti's / Kobra Fleetworks'): written straight to
// dist/data/ams2/cars/ beside the exe, NEVER committed (like era-art). Re-run any time the skins
// change.
//
// Usage:  dotnet run tools/extract_car_previews.cs [<AMS2 install dir>] [<pack id>]
//         defaults: Y:\SteamLibrary\...\Automobilista 2  and  smgp-1

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string repo = AppContext.BaseDirectory.Contains("tools")
    ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
    : Directory.GetCurrentDirectory();
string install = args.Length > 0 ? args[0]
    : @"Y:\SteamLibrary\steamapps\common\Automobilista 2";
string packId = args.Length > 1 ? args[1] : "smgp-1";

string overridesDir = Path.Combine(install, "Vehicles", "Textures", "CustomLiveries", "Overrides");
string packDir = Path.Combine(repo, "packs", packId);
string outDir = Path.Combine(repo, "dist", "data", "ams2", "cars");
Directory.CreateDirectory(outDir);

if (!Directory.Exists(overridesDir))
{
    Console.Error.WriteLine($"Overrides folder not found: {overridesDir}");
    return 1;
}

// ---- livery name -> pack driver id (entries.json + pack.json moddedField) ----
var liveryToDriver = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var e in (JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "entries.json")))!["entries"]!).AsArray())
    liveryToDriver[(string)e!["ams2LiveryName"]!] = (string)e["driverId"]!;
if (JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "pack.json")))!["moddedField"] is JsonObject mf)
    foreach (var e in mf["entries"]!.AsArray())
        liveryToDriver[(string)e!["ams2LiveryName"]!] = (string)e["driverId"]!;

// ---- livery name -> preview DDS path (each model's TOP-LEVEL override XML only) ----
// Read <model>/<model>.xml directly — NOT a recursive sweep, which would pick up the
// _companion-backups copies (stale duplicates whose relative preview paths don't resolve).
int written = 0, missing = 0;
foreach (var modelDir in Directory.EnumerateDirectories(overridesDir))
{
    string model = Path.GetFileName(modelDir);
    string xml = Path.Combine(modelDir, model + ".xml");
    if (!File.Exists(xml))
        continue;
    XDocument doc;
    try { doc = XDocument.Parse(File.ReadAllText(xml)); }
    catch { continue; }

    foreach (var lo in doc.Descendants("LIVERY_OVERRIDE"))
    {
        string name = (string?)lo.Attribute("NAME") ?? "";
        if (!liveryToDriver.TryGetValue(name, out string? driverId))
            continue;
        string? previewRel = (string?)lo.Element("PREVIEWIMAGE")?.Attribute("PATH");
        if (previewRel is null)
        {
            Console.WriteLine($"  {name}: no PREVIEWIMAGE (AMS2 auto-renders it) — skipped");
            missing++;
            continue;
        }
        string ddsPath = Path.Combine(modelDir, previewRel.Replace('\\', Path.DirectorySeparatorChar));
        if (!File.Exists(ddsPath))
        {
            Console.WriteLine($"  {name}: preview file missing on disk ({previewRel}) — skipped");
            missing++;
            continue;
        }

        try
        {
            var (w, h, rgba) = DecodeDds(File.ReadAllBytes(ddsPath));
            (w, h, rgba) = Downscale(w, h, rgba, maxWidth: 900);
            File.WriteAllBytes(Path.Combine(outDir, driverId + ".png"), EncodePng(w, h, rgba));
            Console.WriteLine($"  {name} -> cars/{driverId}.png  ({w}x{h})");
            written++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {name}: decode failed ({ex.Message}) — skipped");
            missing++;
        }
    }
}

Console.WriteLine($"\nWrote {written} car previews to {outDir} ({missing} skipped).");
Console.WriteLine("These are USER ASSETS (skinpack renders) — not committed. Re-run when the skins change.");

// ---- a keys reference so the user knows exactly what to name their DRIVER PORTRAIT drops ----
// The car preview comes from the livery (this tool); the portrait is hand-supplied art. Both key
// off the pack driver id. This lists every pack driver + their exact filenames.
string portraitsDir = Path.Combine(repo, "dist", "data", "ams2", "portraits");
Directory.CreateDirectory(portraitsDir);

var byLivery = new List<(string Livery, string DriverId)>();
foreach (var kv in liveryToDriver) byLivery.Add((kv.Key, kv.Value));
byLivery.Sort((a, b) => string.CompareOrdinal(a.Livery, b.Livery));

var driverNames = new Dictionary<string, string>(StringComparer.Ordinal);
foreach (var dn in (JsonNode.Parse(File.ReadAllText(Path.Combine(packDir, "drivers.json")))!["drivers"]!).AsArray())
    driverNames[(string)dn!["id"]!] = (string)dn["name"]!;

var keys = new StringBuilder();
keys.AppendLine($"SMGP image keys — {packId}");
keys.AppendLine("Drop a DRIVER PORTRAIT (your art) as portraits\\<driverId>.jpg (or .png).");
keys.AppendLine("The CAR preview (cars\\<driverId>.png) is extracted from the livery by this tool.");
keys.AppendLine();
keys.AppendLine($"{"Livery",-30} {"Driver",-20} portrait file");
foreach (var (livery, driverId) in byLivery)
    keys.AppendLine($"{livery,-30} {driverNames.GetValueOrDefault(driverId, ""),-20} portraits\\{driverId}.jpg");
File.WriteAllText(Path.Combine(portraitsDir, "_SMGP-portrait-keys.txt"), keys.ToString());
Console.WriteLine($"\nPortrait-key reference: {Path.Combine(portraitsDir, "_SMGP-portrait-keys.txt")}");
Console.WriteLine("  -> e.g. Zeroforce's Paul Klinger portrait goes at portraits\\driver.paul_klinger.jpg");
return 0;

// ================= DDS (BC1/DXT1 + BC3/DXT5 + uncompressed 32-bit) decode =================
static (int W, int H, byte[] Rgba) DecodeDds(byte[] d)
{
    if (d.Length < 128 || d[0] != 'D' || d[1] != 'D' || d[2] != 'S' || d[3] != ' ')
        throw new InvalidDataException("not a DDS");
    int height = BitConverter.ToInt32(d, 12);
    int width = BitConverter.ToInt32(d, 16);
    uint pfFlags = BitConverter.ToUInt32(d, 80);
    string fourCc = Encoding.ASCII.GetString(d, 84, 4);
    int rgbBits = BitConverter.ToInt32(d, 88);
    int offset = 128;
    var rgba = new byte[width * height * 4];

    if ((pfFlags & 0x4) != 0 && fourCc is "DXT1" or "DXT5") // FOURCC compressed
    {
        bool dxt5 = fourCc == "DXT5";
        for (int by = 0; by < height; by += 4)
            for (int bx = 0; bx < width; bx += 4)
            {
                byte[] alpha = new byte[16];
                if (dxt5) { DecodeBc3Alpha(d, offset, alpha); offset += 8; }
                else Array.Fill(alpha, (byte)255);
                DecodeBc1Color(d, offset, rgba, bx, by, width, height, alpha);
                offset += 8;
            }
        return (width, height, rgba);
    }
    if ((pfFlags & 0x40) != 0 && rgbBits == 32) // uncompressed 32-bit (B8G8R8A8 / R8G8B8A8)
    {
        uint rMask = BitConverter.ToUInt32(d, 92), gMask = BitConverter.ToUInt32(d, 96);
        uint bMask = BitConverter.ToUInt32(d, 100), aMask = BitConverter.ToUInt32(d, 104);
        for (int i = 0; i < width * height; i++)
        {
            uint px = BitConverter.ToUInt32(d, offset + i * 4);
            rgba[i * 4 + 0] = Channel(px, rMask);
            rgba[i * 4 + 1] = Channel(px, gMask);
            rgba[i * 4 + 2] = Channel(px, bMask);
            rgba[i * 4 + 3] = aMask == 0 ? (byte)255 : Channel(px, aMask);
        }
        return (width, height, rgba);
    }
    throw new NotSupportedException($"unsupported DDS format (fourCC '{fourCc}', flags 0x{pfFlags:X})");
}

static byte Channel(uint px, uint mask)
{
    if (mask == 0) return 0;
    int shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
    uint val = (px & mask) >> shift;
    uint max = mask >> shift;
    return (byte)(val * 255 / max);
}

static void DecodeBc1Color(byte[] d, int o, byte[] rgba, int bx, int by, int w, int h, byte[] alpha)
{
    ushort c0 = BitConverter.ToUInt16(d, o), c1 = BitConverter.ToUInt16(d, o + 2);
    Span<(byte R, byte G, byte B)> col = stackalloc (byte, byte, byte)[4];
    col[0] = Rgb565(c0); col[1] = Rgb565(c1);
    if (c0 > c1)
    {
        col[2] = (Lerp(col[0].R, col[1].R, 1, 3), Lerp(col[0].G, col[1].G, 1, 3), Lerp(col[0].B, col[1].B, 1, 3));
        col[3] = (Lerp(col[0].R, col[1].R, 2, 3), Lerp(col[0].G, col[1].G, 2, 3), Lerp(col[0].B, col[1].B, 2, 3));
    }
    else
    {
        col[2] = ((byte)((col[0].R + col[1].R) / 2), (byte)((col[0].G + col[1].G) / 2), (byte)((col[0].B + col[1].B) / 2));
        col[3] = (0, 0, 0);
    }
    uint bits = BitConverter.ToUInt32(d, o + 4);
    for (int py = 0; py < 4; py++)
        for (int px = 0; px < 4; px++)
        {
            int x = bx + px, y = by + py;
            if (x >= w || y >= h) continue;
            var c = col[(int)((bits >> (2 * (py * 4 + px))) & 3)];
            int i = (y * w + x) * 4;
            rgba[i] = c.R; rgba[i + 1] = c.G; rgba[i + 2] = c.B; rgba[i + 3] = alpha[py * 4 + px];
        }
}

static void DecodeBc3Alpha(byte[] d, int o, byte[] alpha)
{
    int a0 = d[o], a1 = d[o + 1];
    Span<int> a = stackalloc int[8];
    a[0] = a0; a[1] = a1;
    if (a0 > a1) for (int i = 1; i < 7; i++) a[i + 1] = ((7 - i) * a0 + i * a1) / 7;
    else { for (int i = 1; i < 5; i++) a[i + 1] = ((5 - i) * a0 + i * a1) / 5; a[6] = 0; a[7] = 255; }
    ulong bits = 0;
    for (int i = 0; i < 6; i++) bits |= (ulong)d[o + 2 + i] << (8 * i);
    for (int i = 0; i < 16; i++) alpha[i] = (byte)a[(int)((bits >> (3 * i)) & 7)];
}

static (byte R, byte G, byte B) Rgb565(ushort c) =>
    ((byte)(((c >> 11) & 0x1F) * 255 / 31), (byte)(((c >> 5) & 0x3F) * 255 / 63), (byte)((c & 0x1F) * 255 / 31));

static byte Lerp(byte a, byte b, int num, int den) => (byte)((a * (den - num) + b * num) / den);

// ================= box downscale (keep aspect, cap width) =================
static (int W, int H, byte[] Rgba) Downscale(int w, int h, byte[] src, int maxWidth)
{
    if (w <= maxWidth) return (w, h, src);
    int nw = maxWidth, nh = Math.Max(1, h * maxWidth / w);
    var dst = new byte[nw * nh * 4];
    for (int y = 0; y < nh; y++)
        for (int x = 0; x < nw; x++)
        {
            int sx0 = x * w / nw, sx1 = Math.Max(sx0 + 1, (x + 1) * w / nw);
            int sy0 = y * h / nh, sy1 = Math.Max(sy0 + 1, (y + 1) * h / nh);
            long r = 0, g = 0, b = 0, a = 0, n = 0;
            for (int sy = sy0; sy < sy1; sy++)
                for (int sx = sx0; sx < sx1; sx++)
                {
                    int i = (sy * w + sx) * 4;
                    r += src[i]; g += src[i + 1]; b += src[i + 2]; a += src[i + 3]; n++;
                }
            int o = (y * nw + x) * 4;
            dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n); dst[o + 3] = (byte)(a / n);
        }
    return (nw, nh, dst);
}

// ================= minimal PNG encoder (zlib via DeflateStream + adler32/crc32) =================
static byte[] EncodePng(int w, int h, byte[] rgba)
{
    using var ms = new MemoryStream();
    ms.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

    var ihdr = new byte[13];
    WriteBe(ihdr, 0, w); WriteBe(ihdr, 4, h);
    ihdr[8] = 8; ihdr[9] = 6; // 8-bit, RGBA
    Chunk(ms, "IHDR", ihdr);

    // filter byte 0 per scanline
    var raw = new byte[h * (1 + w * 4)];
    for (int y = 0; y < h; y++)
    {
        raw[y * (1 + w * 4)] = 0;
        Array.Copy(rgba, y * w * 4, raw, y * (1 + w * 4) + 1, w * 4);
    }
    Chunk(ms, "IDAT", Zlib(raw));
    Chunk(ms, "IEND", []);
    return ms.ToArray();
}

static void Chunk(Stream s, string type, byte[] data)
{
    var len = new byte[4]; WriteBe(len, 0, data.Length); s.Write(len);
    var t = Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
    uint crc = Crc32(t, Crc32(data, 0xFFFFFFFF)) ^ 0xFFFFFFFF;
    var c = new byte[4]; WriteBe(c, 0, (int)crc); s.Write(c);
}

static byte[] Zlib(byte[] data)
{
    using var ms = new MemoryStream();
    ms.WriteByte(0x78); ms.WriteByte(0x01);
    using (var df = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        df.Write(data, 0, data.Length);
    uint adler = Adler32(data);
    ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16));
    ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
    return ms.ToArray();
}

static uint Adler32(byte[] d)
{
    uint a = 1, b = 0;
    foreach (byte x in d) { a = (a + x) % 65521; b = (b + a) % 65521; }
    return (b << 16) | a;
}

static uint Crc32(byte[] d, uint crc)
{
    foreach (byte x in d)
    {
        crc ^= x;
        for (int k = 0; k < 8; k++)
            crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
    }
    return crc;
}
static void WriteBe(byte[] b, int o, int v)
{
    b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
}
