/*
 * HuffleDesktopPet — Sprite Sheet Processor
 *
 * Slices a grid-based sprite sheet into individual 64×64 frame PNGs,
 * named according to the huffle_{state}_{frame:D2}.png convention.
 *
 * Usage (via process-sprites.ps1 wrapper or directly):
 *
 *   process-sprites <sheet.png> --cols <n> [OPTIONS]
 *
 * Options:
 *   --cols    N          Frames per row (required)
 *   --rows    N          Number of animation state rows (default: 1)
 *   --states  a,b,c,...  Name for each row, comma-separated (default: state0, state1, ...)
 *   --out     PATH       Output directory (default: sprites/ next to the sheet)
 *   --size    N          Output frame size in pixels (default: 64)
 *   --pet     NAME       Filename prefix (default: huffle)
 *   --padding N          Transparent gap between frames in pixels (default: 0)
 *   --margin  N          Border around the entire sheet in pixels (default: 0)
 *
 * Example:
 *   process-sprites sheet.png --cols 4 --rows 2 --states walk,idle --padding 2
 */

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// ── Parse arguments ───────────────────────────────────────────────────────────

string? sheetPath  = null;
int     cols       = 0;
int     rows       = 1;
int     outputSize = 64;
int     padding    = 0;
int     margin     = 0;
string  petName    = "huffle";
string? outputDir  = null;
string[] stateNames = [];

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--cols":    cols       = int.Parse(args[++i]); break;
        case "--rows":    rows       = int.Parse(args[++i]); break;
        case "--size":    outputSize = int.Parse(args[++i]); break;
        case "--padding": padding    = int.Parse(args[++i]); break;
        case "--margin":  margin     = int.Parse(args[++i]); break;
        case "--pet":     petName    = args[++i];             break;
        case "--out":     outputDir  = args[++i];             break;
        case "--states":  stateNames = args[++i].Split(',');  break;
        default:
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
                sheetPath = args[i];
            break;
    }
}

// ── Validate ──────────────────────────────────────────────────────────────────

if (string.IsNullOrWhiteSpace(sheetPath) || cols == 0)
{
    Console.Error.WriteLine("""
        Huffle Sprite Processor
        -----------------------
        Usage:  process-sprites <sheet.png> --cols <n> [OPTIONS]

        Required:
          <sheet.png>       Path to the sprite sheet image
          --cols N          Number of frame columns

        Optional:
          --rows    N       Number of animation rows      (default: 1)
          --states  a,b,c   Name each row, comma-separated (default: state0, state1, ...)
          --out     PATH    Output directory               (default: sprites/ next to sheet)
          --size    N       Output frame size in px        (default: 64)
          --pet     NAME    Filename prefix                (default: huffle)
          --padding N       Gap between frames in px       (default: 0)
          --margin  N       Border around the sheet in px  (default: 0)

        Examples:
          process-sprites walk_idle.png --cols 4 --rows 2 --states walk,idle
          process-sprites sheet.png --cols 6 --rows 3 --states walk,idle,eat --padding 2 --margin 1
        """);
    return 1;
}

if (!File.Exists(sheetPath))
{
    Console.Error.WriteLine($"ERROR: File not found: {sheetPath}");
    return 1;
}

if (rows < 1 || cols < 1)
{
    Console.Error.WriteLine("ERROR: --rows and --cols must both be >= 1");
    return 1;
}

// ── Fill missing state names ──────────────────────────────────────────────────

string[] resolvedStates = new string[rows];
for (int r = 0; r < rows; r++)
    resolvedStates[r] = r < stateNames.Length
        ? stateNames[r].Trim().ToLowerInvariant()
        : $"state{r}";

// ── Resolve output directory ──────────────────────────────────────────────────

string sheetDir = Path.GetDirectoryName(Path.GetFullPath(sheetPath))!;
outputDir ??= Path.Combine(sheetDir, "sprites");
Directory.CreateDirectory(outputDir);

// ── Load sheet and compute frame geometry ─────────────────────────────────────

using var sheet = new Bitmap(sheetPath);

// usable area after removing margin on each side
int usableW = sheet.Width  - margin * 2;
int usableH = sheet.Height - margin * 2;

// frame dimensions derived from usable area minus inter-frame gaps
int frameW = (usableW - padding * (cols - 1)) / cols;
int frameH = (usableH - padding * (rows - 1)) / rows;

if (frameW <= 0 || frameH <= 0)
{
    Console.Error.WriteLine(
        $"ERROR: Calculated frame size is {frameW}x{frameH}. " +
        "Check --cols, --rows, --padding, --margin match the sheet dimensions.");
    return 1;
}

// ── Print summary ─────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("=== Huffle Sprite Processor ===");
Console.WriteLine($"  Sheet     : {Path.GetFileName(sheetPath)}  ({sheet.Width}x{sheet.Height})");
Console.WriteLine($"  Grid      : {cols} cols x {rows} rows");
Console.WriteLine($"  Frame src : {frameW}x{frameH} px");
Console.WriteLine($"  Frame out : {outputSize}x{outputSize} px");
Console.WriteLine($"  Padding   : {padding} px  |  Margin: {margin} px");
Console.WriteLine($"  States    : {string.Join(", ", resolvedStates)}");
Console.WriteLine($"  Output    : {outputDir}");
Console.WriteLine();

// ── Slice and save frames ─────────────────────────────────────────────────────

int saved = 0;

for (int row = 0; row < rows; row++)
{
    string state = resolvedStates[row];

    for (int col = 0; col < cols; col++)
    {
        // Source rectangle in the sprite sheet
        int srcX = margin + col * (frameW + padding);
        int srcY = margin + row * (frameH + padding);
        var srcRect = new Rectangle(srcX, srcY, frameW, frameH);

        // Output bitmap — 32-bit RGBA so transparency is preserved
        using var frame = new Bitmap(outputSize, outputSize, PixelFormat.Format32bppArgb);
        using var g     = Graphics.FromImage(frame);

        // Clear to fully transparent
        g.Clear(Color.Transparent);

        // Pixel-art–correct scaling: NearestNeighbor + PixelOffsetMode.Half
        // (Half corrects the known 0.5px shift bug in GDI+ NearestNeighbor)
        g.InterpolationMode  = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode    = PixelOffsetMode.Half;
        g.SmoothingMode      = SmoothingMode.None;
        g.CompositingMode    = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.AssumeLinear;

        g.DrawImage(
            sheet,
            new Rectangle(0, 0, outputSize, outputSize),
            srcRect,
            GraphicsUnit.Pixel);

        string filename = $"{petName}_{state}_{col + 1:D2}.png";
        string outPath  = Path.Combine(outputDir, filename);

        // Save as PNG (lossless, preserves alpha)
        frame.Save(outPath, ImageFormat.Png);

        Console.WriteLine($"  [{row + 1}/{rows}, {col + 1}/{cols}]  {filename}");
        saved++;
    }

    Console.WriteLine();
}

// ── Done ──────────────────────────────────────────────────────────────────────

Console.WriteLine($"Done — {saved} frames saved to:");
Console.WriteLine($"  {outputDir}");
Console.WriteLine();
return 0;
