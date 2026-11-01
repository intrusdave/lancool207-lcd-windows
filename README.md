# lancool207-lcd-windows

Displays a static image on the **Lian Li LANCOOL 207 Digital** case's USB LCD from Windows, without L-Connect.

A small C# / .NET 10 command-line tool. It talks to the panel directly through Windows' built-in WinUSB driver, so you don't need libusb, Zadig, or any extra DLLs.

## Requirements

- Windows 10/11 x64
- [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or the SDK, if you're building from source)
- The panel in LCD mode, which shows up as USB `1CBE:A065` ("lianli-207LCD-1.0"). Windows binds it to WinUSB automatically.
- **Lian Li L-Connect closed**, because it holds the device open.

## Usage

```
lcd207 <image> [--rotate 0|90|180|270] [--fit cover|contain|stretch] [--brightness 0-100] [--no-clear] [-v]
lcd207 --info [-v]
```

- **Image formats:** PNG, JPEG, BMP, GIF (first frame), WebP, and anything else SkiaSharp can decode.
- **Size and orientation:** the panel is **720×1472 portrait**. Without `--rotate`, landscape images are rotated 90° to fit.
- **`--fit`:**
  - `cover` (the default) fills the screen and crops the overflow.
  - `contain` letterboxes the image on black.
  - `stretch` ignores the aspect ratio.
- **`--info`:** prints the panel firmware version. This is a quick connectivity check.
- **`-v`:** logs each command and the panel's reply.

The image stays on the panel after the program exits. No background process is needed.

### GUI

`lcd207-gui.exe` is a small window you can drop an image onto (or click to browse). It sends the image to the panel straight away and shows a preview of exactly what the panel displays.

The **Rotation** (Auto, 0°, 90°, 180°, 270°) and **Fit** (Cover, Contain, Stretch) dropdowns work like the CLI options. Changing either one re-sends the current image.

## Build

```
dotnet test
dotnet publish src/Lcd207 -c Release -o publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

This produces `publish/lcd207.exe`. For the GUI, publish `src/Lcd207.Gui` the same way to get `lcd207-gui.exe`.

### Installer

```
dotnet build installer -c Release [-p:ProductVersion=1.2.3]
```

This produces `installer/bin/Release/LanCoolLCD-Setup.msi`. It's built with [WiX Toolset](https://wixtoolset.org/) 6, which is pulled from NuGet, so there's nothing to install.

The MSI:
- installs the GUI and CLI to `C:\Program Files\A3V01D\LanCoolLCD\` (you can change the location),
- adds a **LanCool LCD** Start Menu shortcut,
- puts `lcd207` on the system PATH,
- requires the .NET 10 Desktop Runtime (x64).

Installing a newer version replaces the old one.

## How it works

Every command is a 512-byte header. The header is a 500-byte plaintext block containing:
- the opcode,
- the magic bytes `1A 6D`,
- a millisecond timestamp,
- the parameters.

That block is DES-CBC encrypted (PKCS7), padded to 512 bytes, and given a trailer of `A1 1A`.

To display an image, the tool sends:
- the header for opcode `0x65` with the JPEG length,
- immediately followed by a baseline 4:2:0 JPEG of at most 512,000 bytes,
- all as one bulk transfer on endpoint `0x01`.

The panel acknowledges on endpoint `0x81`.

| File | Purpose |
|---|---|
| `src/Lcd207/WinUsbDevice.cs` | Device discovery (SetupAPI) and WinUSB bulk I/O |
| `src/Lcd207/PacketBuilder.cs` | Encrypted command headers |
| `src/Lcd207/Lcd207Panel.cs` | Init sequence, image push, reply handling |
| `src/Lcd207/ImageEncoder.cs` | Rotate, scale, and JPEG-encode to panel size |
| `src/Lcd207.Gui/` | WPF drag-and-drop front end |
| `installer/` | WiX MSI installer |

Tested on firmware `lianli207_0003_0009`.

## Credits

The protocol comes from [sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). This project is a C# port of its LANCOOL 207 LCD driver; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Not affiliated with or endorsed by Lian Li.

## License

[MIT](LICENSE)
