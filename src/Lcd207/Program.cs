using Lcd207;

const string Usage = """
    Usage:
      lcd207 <image> [--rotate 0|90|180|270] [--fit cover|contain|stretch] [--brightness 0-100] [--no-clear] [-v]
      lcd207 --info [-v]

    Pushes a static image to the LANCOOL 207 Digital LCD (USB 1CBE:A065).
    Without --rotate, landscape images are rotated 90° to fit the 720x1472 portrait panel.
    Close Lian Li L-Connect first; it holds the device open.
    """;

string? imagePath = null;
int? rotation = null;
var fit = FitMode.Cover;
byte? brightness = null;
bool info = false, verbose = false, clear = true;

try
{
    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--rotate": rotation = int.Parse(args[++i]); break;
            case "--fit": fit = Enum.Parse<FitMode>(args[++i], ignoreCase: true); break;
            case "--brightness": brightness = byte.Parse(args[++i]); break;
            case "--no-clear": clear = false; break;
            case "--info": info = true; break;
            case "-v" or "--verbose": verbose = true; break;
            case "-h" or "--help": Console.WriteLine(Usage); return 0;
            default:
                if (args[i].StartsWith('-') || imagePath != null)
                    throw new ArgumentException($"Unexpected argument: {args[i]}");
                imagePath = args[i];
                break;
        }
    }
    if (imagePath == null && !info)
        throw new ArgumentException("No image given");
}
catch (Exception e) when (e is ArgumentException or FormatException or IndexOutOfRangeException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    byte[]? jpeg = null;
    if (imagePath != null)
    {
        jpeg = ImageEncoder.EncodeForPanel(imagePath, Lcd207Panel.Width, Lcd207Panel.Height,
            Lcd207Panel.MaxPayload, rotation, fit);
        Console.WriteLine($"Encoded {imagePath} -> {jpeg.Length:N0} byte JPEG");
    }

    using var panel = Lcd207Panel.Open();
    if (verbose)
    {
        panel.Log = Console.WriteLine;
        Console.WriteLine($"Opened {panel.DevicePath}");
    }

    if (info)
    {
        panel.StopPlayback();
        Console.WriteLine($"Firmware: {panel.ReadFirmware() ?? "(no reply)"}");
        if (jpeg == null)
            return 0;
    }

    panel.Init(clearLayers: clear);
    if (!info)
        Console.WriteLine($"Firmware: {panel.Firmware ?? "(no reply)"}");
    if (brightness is { } b)
        panel.SetBrightness(b);

    panel.PushJpeg(jpeg!);
    Console.WriteLine("Image sent.");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"Error: {e.Message}");
    if (verbose)
        Console.Error.WriteLine(e);
    return 1;
}
