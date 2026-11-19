using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text;
using WzComparerR2.WzLib;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: WzFontExtractor <WZ folder> <output folder> [--inventory-only]");
    return 2;
}

string inputFolder = Path.GetFullPath(args[0]);
string outputFolder = Path.GetFullPath(args[1]);
bool inventoryOnly = args.Any(a => string.Equals(a, "--inventory-only", StringComparison.OrdinalIgnoreCase));

if (!Directory.Exists(inputFolder))
{
    Console.Error.WriteLine($"Input folder not found: {inputFolder}");
    return 3;
}

Directory.CreateDirectory(outputFolder);
string pngFolder = Path.Combine(outputFolder, "png");
string rawFolder = Path.Combine(outputFolder, "raw");
if (!inventoryOnly)
{
    Directory.CreateDirectory(pngFolder);
    Directory.CreateDirectory(rawFolder);
}

using StreamWriter inventory = new(Path.Combine(outputFolder, "inventory.tsv"), false, new UTF8Encoding(false));
inventory.WriteLine("path\ttype\tdetails\texported_file");

int imageCount = 0;
int pngCount = 0;
int pngExported = 0;
int scalarCount = 0;
int rawCount = 0;
int errors = 0;

Wz_Structure structure = new();
Wz_Node? root = null;

try
{
    structure.LoadWzFolder(inputFolder, ref root, false);
    if (root == null)
    {
        throw new InvalidDataException("The WZ folder produced no root node.");
    }

    Walk(root, root.Text);
}
finally
{
    structure.Clear();
}

Console.WriteLine($"images={imageCount} png_nodes={pngCount} png_exported={pngExported} raw_nodes={rawCount} scalars={scalarCount} errors={errors}");
Console.WriteLine($"inventory={Path.Combine(outputFolder, "inventory.tsv")}");
return errors == 0 ? 0 : 1;

void Walk(Wz_Node node, string logicalPath)
{
    object? value = node.Value;

    if (value is Wz_Image image)
    {
        imageCount++;
        inventory.WriteLine($"{Escape(logicalPath)}\tWz_Image\t\t");
        if (!image.TryExtract(out Exception? extractError))
        {
            errors++;
            inventory.WriteLine($"{Escape(logicalPath)}\tERROR\t{Escape(extractError?.Message ?? "TryExtract returned false")}\t");
            return;
        }

        foreach (Wz_Node child in image.Node.Nodes)
        {
            Walk(child, logicalPath + "\\" + child.Text);
        }
        return;
    }

    if (value is Wz_Png png)
    {
        pngCount++;
        string details = $"{png.Width}x{png.Height};format={(int)png.Format};scale={png.Scale};pages={png.ActualPages}";
        string exported = string.Empty;

        if (!inventoryOnly)
        {
            for (int page = 0; page < png.ActualPages; page++)
            {
                string relative = MakeSafeRelativePath(logicalPath, page, png.ActualPages);
                string destination = Path.Combine(pngFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                try
                {
                    using var bitmap = png.ExtractPng(page);
                    bitmap.Save(destination, ImageFormat.Png);
                    pngExported++;
                    exported = string.IsNullOrEmpty(exported) ? relative : exported + ";" + relative;
                }
                catch (Exception ex)
                {
                    errors++;
                    inventory.WriteLine($"{Escape(logicalPath)}\tPNG_ERROR\t{Escape(ex.Message)}\t{Escape(relative)}");
                }
            }
        }

        inventory.WriteLine($"{Escape(logicalPath)}\tWz_Png\t{Escape(details)}\t{Escape(exported)}");
    }
    else if (value is Wz_RawData rawData)
    {
        rawCount++;
        string exported = string.Empty;
        string details = $"length={rawData.Length}";
        if (!inventoryOnly)
        {
            try
            {
                byte[] bytes = new byte[rawData.Length];
                rawData.CopyTo(bytes, 0);
                string extension = DetectRawExtension(bytes);
                string relative = MakeSafeRelativePath(logicalPath, 0, 1, extension);
                string destination = Path.Combine(rawFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
                exported = relative;
                details += $";sha256={Convert.ToHexString(SHA256.HashData(bytes))};signature={GetSignature(bytes)}";
            }
            catch (Exception ex)
            {
                errors++;
                inventory.WriteLine($"{Escape(logicalPath)}\tRAW_ERROR\t{Escape(ex.Message)}\t");
            }
        }
        inventory.WriteLine($"{Escape(logicalPath)}\tWz_RawData\t{Escape(details)}\t{Escape(exported)}");
    }
    else if (value != null && value is not Wz_File)
    {
        scalarCount++;
        inventory.WriteLine($"{Escape(logicalPath)}\t{Escape(value.GetType().Name)}\t{Escape(value.ToString() ?? string.Empty)}\t");
    }

    foreach (Wz_Node child in node.Nodes)
    {
        Walk(child, logicalPath + "\\" + child.Text);
    }
}

static string Escape(string value) => value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

static string MakeSafeRelativePath(string logicalPath, int page, int pageCount, string extension = ".png")
{
    char[] invalid = Path.GetInvalidFileNameChars();
    string[] segments = logicalPath.Split('\\', StringSplitOptions.RemoveEmptyEntries)
        .Select(segment => new string(segment.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()))
        .ToArray();

    string fileName = segments.Length == 0 ? "unnamed" : segments[^1];
    if (pageCount > 1)
    {
        fileName += $"_page{page + 1}";
    }
    fileName += extension;

    string[] folders = segments.Take(Math.Max(0, segments.Length - 1)).ToArray();
    return folders.Length == 0 ? fileName : Path.Combine(Path.Combine(folders), fileName);
}

static string DetectRawExtension(byte[] bytes)
{
    if (bytes.Length >= 4)
    {
        if (bytes[0] == 0x00 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00) return ".ttf";
        string magic = Encoding.ASCII.GetString(bytes, 0, 4);
        if (magic == "OTTO") return ".otf";
        if (magic == "wOFF") return ".woff";
        if (magic == "wOF2") return ".woff2";
    }
    return ".bin";
}

static string GetSignature(byte[] bytes)
{
    int length = Math.Min(bytes.Length, 16);
    return Convert.ToHexString(bytes.AsSpan(0, length));
}
