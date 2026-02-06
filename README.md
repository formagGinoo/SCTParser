# SCTParser

> [!IMPORTANT]
> This project is now implemented in [Chaos Zero Nightmare ASSet Ripper](https://github.com/akioukun/Chaos-Zero-Nightmare-ASSet-Ripper). For a better and complete user experience i recommend using that tool. This will remain here as a research project.

### What is this

This is a small program that is able to **parse** the custom images format (**.sct**) used in games made with **Yuna Engine (SuperCreative engine)** like *Chaos Zero Nightmare (tested)* and *Epic Seven (not tested)*.

The program supports both **SCT** and **SCT2** versions of the format, with implementation based on reverse engineering analysis using IDA Pro.

## Notes
- it was a fun reverse/research project

<hr>

### Usage

```
Usage: SCTParser <input_path> <output_path> [--verbose]
  input_path: File or directory to process
  output_path: Output directory for PNG files
  --verbose / --v: Optional flag for detailed output
```
<hr>

### File Format Structure

#### SCT (Legacy Format)
- 3-byte signature: "SCT"
- 1-byte pixel format
- 2-byte width/height
- LZ4 compressed data

#### SCT2
- 4-byte signature: "SCT2" (0x32544353)
- 34-byte header with extended metadata
- Flags for compression, alpha, cropping, mipmaps
- Optional LZ4 compression

See [code](./SCTParser/SCTParser.cs#L8-L60) documentation for complete header layout.

<hr>

### Supported Formats

The parser correctly handles various pixel formats:
- **RGB**: RGB565, RGB565_LE
- **RGBA**: Standard RGBA, ETC2_RGBA8
- **ASTC**: 4x4, 6x6, 8x8 compressed textures
- **L8**: 8-bit luminance

### Credits
If you like this project, feel free to leave a star, and check out my other stuff. And if you use it, feel free to credit <3



