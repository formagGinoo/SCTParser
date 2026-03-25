using Texture2DDecoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SCTParser;

/// <summary>
/// Parser for SCT and SCT2 texture file formats used in the game.
///
/// ========================================================================
/// 
/// SCT Format (Legacy):
/// -------------------
/// Offset | Size | Type   | Description
/// -------|------|--------|-------------
/// 0x00   | 3    | char[] | Signature: "SCT" (0x53 0x43 0x54)
/// 0x03   | 1    | byte   | Unknown/Padding
/// 0x04   | 1    | byte   | Pixel format code
/// 0x05   | 2    | uint16 | Image width (little endian)
/// 0x07   | 2    | uint16 | Image height (little endian)
/// 0x09   | N    | byte[] | LZ4 compressed image data
/// 
/// SCT2 Format:
/// ---------------------
/// Offset | Size | Type   | Description
/// -------|------|--------|-------------
/// 0x00   | 4    | int32  | Signature: "SCT2" (0x32544353)
/// 0x04   | 4    | int32  | Total file size
/// 0x08   | 4    | int32  | Unknown1 (padding/reserved)
/// 0x0C   | 4    | int32  | Image data offset from file start
/// 0x10   | 4    | int32  | Unknown2
/// 0x14   | 4    | int32  | Pixel format code
/// 0x18   | 2    | uint16 | Image width
/// 0x1A   | 2    | uint16 | Image height
/// 0x1C   | 2    | uint16 | Texture width (with padding)
/// 0x1E   | 2    | uint16 | Texture height (with padding)
/// 0x20   | 4    | int32  | Flags (see below)
/// 0x24+  | N    | byte[] | Image data (compressed or raw)
/// 
/// SCT2 Flags (Offset 0x20, 4 bytes):
/// ----------------------------------
/// Bit 0  (0x00000001): Has alpha channel
/// Bit 1  (0x00000002): Crop flag (adjust dimensions)
/// Bit 4  (0x00000010): Raw data hint (may not be compressed)
/// Bit 5  (0x00000020): Mipmap flag
/// Bit 31 (0x80000000): LZ4 compressed data
/// 
/// Pixel Format Codes (Common values):
/// -----------------------------------
/// 4   = RGB565 Little Endian
/// 6   = RGB
/// 16  = RGB565
/// 17-26 = RGBA variants
/// 19  = ETC2_RGBA8
/// 40  = ASTC 4x4
/// 44  = ASTC 6x6
/// 47  = ASTC 8x8
/// 102 = L8 (Luminance)
/// 
/// </summary>
#nullable disable
public class SCTParser
{
    public static int SCT2_SIGNATURE = 0x32544353; // 844383059 = "SCT2" in little endian
    private const int SCT_SIGNATURE_WORD = 0x4353;  // 17235 = "SC" in little endian
    private const byte SCT_SIGNATURE_BYTE = 0x54;   // 84 = "T"

    private abstract class File {
        public byte[] ImageData = Array.Empty<byte>();
        public string PixelFormatName = string.Empty;
        public int Channels;
        public string FormatType = string.Empty;

        public abstract int Width { get; }
        public abstract int Height { get; }
        public abstract int PixelFormatCode { get; }
        public virtual bool HasAlpha => Channels == 4;
    }

    private class SCTFile : File
    {
        public SCTHeader Header;

        public override int Width => Header.Width;
        public override int Height => Header.Height;
        public override int PixelFormatCode => Header.PixelFormat;
    }
    private struct SCTHeader
    {
        public byte[] Signature;
        public byte Unknown;
        public byte PixelFormat;
        public ushort Width;
        public ushort Height;
        public int DataOffset => 9;
        public bool Compressed => true;
        public ushort TextureWidth => Width;
        public ushort TextureHeight => Height;
    }

    private class SCT2File : File
    {
        public SCT2Header Header;
        public override int Width => Header.Width;
        public override int Height => Header.Height;
        public override int PixelFormatCode => Header.PixelFormat;
        public override bool HasAlpha => Header.HasAlpha || base.HasAlpha;
    }
    private struct SCT2Header
    {
        public int Signature;
        public int TotalSize;
        public int Unknown1;
        public int DataOffset;
        public int Unknown2;
        public int PixelFormat;
        public ushort Width;
        public ushort Height;
        public ushort TextureWidth;
        public ushort TextureHeight;
        public int Flags;
        public bool HasAlpha => (Flags & 0x01) != 0;
        public bool CropFlag => (Flags & 0x02) != 0;
        public bool RawData => (Flags & 0x10) != 0;
        public bool MipmapFlag2 => (Flags & 0x20) != 0;
        public bool Compressed => (Flags & 0x80000000) != 0;
    }

    /// <summary>
    /// Detect the format of the given byte array.
    /// </summary>
    /// <param name="data">Array of bytes to analyze</param>
    /// <param name="debug">Flag to enable debug messages</param>
    /// <returns>10002 for sct2, 10001 for sct, -1 for unknown format</returns>
    private static int DetectFormat(byte[] data, bool debug = false)
    {
        if (data.Length < 4)
        {
            if (debug) Console.WriteLine($"Data too short: {data.Length} bytes");
            return -1;
        }

        // Check SCT2
        int signature = BitConverter.ToInt32(data, 0);
        if (debug) Console.WriteLine($"4-byte signature: {signature} (0x{signature:X8})");

        if (signature == SCT2_SIGNATURE)
        {
            if (debug) Console.WriteLine("Matched SCT2!");
            return 10002;
        }

        // Check SCT
        if (data.Length >= 3)
        {
            ushort word = BitConverter.ToUInt16(data, 0);
            byte b = data[2];
            if (debug) Console.WriteLine($"SCT check: word={word}, byte={b}");

            if (word == SCT_SIGNATURE_WORD && b == SCT_SIGNATURE_BYTE)
            {
                if (debug) Console.WriteLine("Matched SCT!");
                return 10001;
            }
        }

        return -1;
    }


    /// <summary>
    /// Parse SCT header 
    /// </summary>
    /// <param name="data">array of bytes containing the SCT header</param>
    /// <returns>A Dictionary object containing the header parameters</returns>
    /// <exception cref="ArgumentException">Thrown if the file is too small</exception>
    private static SCTHeader ParseSCTHeader(byte[] data)
    {
        if (data.Length < 9)
            throw new ArgumentException("File too small to contain a valid SCT header");

        var header = new SCTHeader();

        // Offset 0-2: Signature "SCT" (already verified by DetectFormat)
        header.Signature = data.Take(3).ToArray();

        // Offset 3: Padding/Unknown (not used by game)
        header.Unknown= data[3];

        // Offset 4: Pixel format (1 byte)
        // Game reads this and uses it in format conversion logic
        header.PixelFormat = data[4];

        // Offset 5-6: Image width (2 bytes, little endian)
        header.Width = BitConverter.ToUInt16(data, 5);

        // Offset 7-8: Image height (2 bytes, little endian)
        header.Height = BitConverter.ToUInt16(data, 7);

        // compressed data starts at offset 9
        //header.DataOffset = 9;
        //header.Compressed = true;

        return header;
    }

    /// <summary>
    /// Parse SCT2 header
    /// </summary>
    /// <param name="data">Array of bytes containing the SCT2 header</param>
    /// <returns>A Dictionary object containing the header parameters</returns>
    /// <exception cref="ArgumentException">Thrown if the file is too small</exception>
    private static SCT2Header ParseSCT2Header(byte[] data)
    {
        if (data.Length < 34)
            throw new ArgumentException("File too small to contain a valid SCT2 header");

        var header = new SCT2Header();

        // Offset 0: Signature
        header.Signature = BitConverter.ToInt32(data, 0);

        // Offset 4: Total data size
        header.TotalSize = BitConverter.ToInt32(data, 4);

        // Offset 8: Unknown (possibly padding)
        header.Unknown1 = BitConverter.ToInt32(data, 8);

        // Offset 12: Image data offset
        header.DataOffset = BitConverter.ToInt32(data, 12);

        // Offset 16: Unknown
        header.Unknown2 = BitConverter.ToInt32(data, 16);

        // Offset 20: Pixel format (4 bytes)
        // The game reads this as 4-byte integer, then compares with format constants
        header.PixelFormat = BitConverter.ToInt32(data, 20);

        // Offset 24-25: Width (2 bytes)
        header.Width = BitConverter.ToUInt16(data, 24);

        // Offset 26-27: Height (2 bytes)
        header.Height = BitConverter.ToUInt16(data, 26);

        // Offset 28-29: Texture width (2 bytes)
        header.TextureWidth = BitConverter.ToUInt16(data, 28);

        // Offset 30-31: Texture height (2 bytes)
        header.TextureHeight = BitConverter.ToUInt16(data, 30);

        // Offset 32-35: Flags (4 bytes - int32)
        // flags are read as 4-byte integer
        header.Flags = BitConverter.ToInt32(data, 32);

        return header;
    }

    /// <summary>
    /// Validates header values for sanity checks.
    /// Helps detect corrupted or invalid SCT/SCT2 files.
    /// Could be usefull for future SCT2 parsing if we encounter files that don't follow expected patterns.
    /// </summary>
    /// <param name="header">Header dictionary to validate</param>
    /// <param name="verbose">Enable validation messages</param>
    /// <returns>True if header appears valid, false otherwise</returns>
    [Obsolete("This method is not currently used, but may be helpful for future debugging of SCT2 files with unexpected header values.")]
    public static bool ValidateHeader(Dictionary<string, object> header, bool verbose = false)
    {
        // Check width and height are reasonable
        int width = Convert.ToInt32(header["width"]);
        int height = Convert.ToInt32(header["height"]);

        if (width <= 0 || width > 16384)
        {
            if (verbose) Console.WriteLine($"Invalid width: {width} (expected 1-16384)");
            return false;
        }

        if (height <= 0 || height > 16384)
        {
            if (verbose) Console.WriteLine($"Invalid height: {height} (expected 1-16384)");
            return false;
        }

        // Check pixel format is within known range
        int pixelFormat = Convert.ToInt32(header["pixel_format"]);
        if (pixelFormat < 0 || pixelFormat > 255)
        {
            if (verbose) Console.WriteLine($"Suspicious pixel format: {pixelFormat}");
        }

        if (verbose) Console.WriteLine($"Header validation passed: {width}x{height}, format {pixelFormat}");
        return true;
    }

    /// <summary>
    /// LZ4 decompression implementation based on assembly code analysis
    /// </summary>
    /// <param name="compressed_data">Compressed data array</param>
    /// <returns>Decompressed data as byte array</returns>
    /// <exception cref="ArgumentException">Thrown if compressed data is too short</exception>
    private static byte[] LZ4Decompress(byte[] compressed_data)
    {
        if (compressed_data.Length < 8)
            throw new ArgumentException("Compressed data too short");

        // First 4 bytes are the decompressed size
        int decompressed_size = BitConverter.ToInt32(compressed_data, 0);

        // Next 4 bytes should be the compressed size
        int compressed_size = BitConverter.ToInt32(compressed_data, 4);

        // Start decompression from byte 8
        byte[] dst = new byte[decompressed_size];
        int src_pos = 8;
        int dst_pos = 0;

        while (src_pos < compressed_data.Length && dst_pos < decompressed_size)
        {
            if (src_pos >= compressed_data.Length)
                break;

            // Read token
            byte token = compressed_data[src_pos++];

            // Literal length (first 4 bits)
            int literal_length = (token >> 4) & 0x0F;

            // Match length (last 4 bits)
            int match_length = token & 0x0F;

            // If literal_length == 15, read additional bytes
            if (literal_length == 15)
            {
                while (src_pos < compressed_data.Length)
                {
                    byte extra = compressed_data[src_pos++];
                    literal_length += extra;
                    if (extra != 255)
                        break;
                }
            }

            // Copy literals
            if (literal_length > 0)
            {
                if (src_pos + literal_length > compressed_data.Length)
                    literal_length = compressed_data.Length - src_pos;
                if (dst_pos + literal_length > dst.Length)
                    literal_length = dst.Length - dst_pos;

                Array.Copy(compressed_data, src_pos, dst, dst_pos, literal_length);
                src_pos += literal_length;
                dst_pos += literal_length;
            }

            // If no more data or we're done, exit
            if (src_pos >= compressed_data.Length || dst_pos >= decompressed_size)
                break;

            // Read match offset (2 bytes little endian)
            if (src_pos + 1 >= compressed_data.Length)
                break;

            ushort offset = BitConverter.ToUInt16(compressed_data, src_pos);
            src_pos += 2;

            // If match_length == 15, read additional bytes
            if (match_length == 15)
            {
                while (src_pos < compressed_data.Length)
                {
                    byte extra = compressed_data[src_pos++];
                    match_length += extra;
                    if (extra != 255)
                        break;
                }
            }

            // Add 4 to match length (LZ4 minimum match)
            match_length += 4;

            // Copy from match
            int match_start = dst_pos - offset;
            if (match_start < 0)
                break;

            // Copy byte by byte to handle overlapping
            for (int i = 0; i < match_length && dst_pos < decompressed_size && match_start + i < dst_pos; i++)
            {
                dst[dst_pos++] = dst[match_start + i];
            }
        }

        // If we didn't fill the entire buffer, trim it
        if (dst_pos < decompressed_size)
        {
            byte[] trimmed = new byte[dst_pos];
            Array.Copy(dst, 0, trimmed, 0, dst_pos);
            return trimmed;
        }

        return dst;
    }

    /// <summary> 
    /// Determines if a file should be LZ4 decompressed
    /// </summary>
    /// <param name="image_data">Raw image data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="pixel_format">Pixel format code</param>
    /// <param name="verbose">Enable detailed output</param>
    /// <returns>True if data should be decompressed, false otherwise</returns>
    private static bool ShouldDecompressIntelligently(byte[] image_data, int width, int height, int pixel_format, bool verbose = false)
    {
        if (image_data.Length < 8)
            return false;

        // Calculate expected ASTC size for format 40 (ASTC 4x4)
        int expected_astc_size;
        if (pixel_format == 40)
        {
            int blocks_w = (width + 3) / 4;
            int blocks_h = (height + 3) / 4;
            expected_astc_size = blocks_w * blocks_h * 16;
        }
        else
        {
            // For other formats, use approximate sizes
            expected_astc_size = width * height * 2;
        }

        // 1: Size ratio
        double size_ratio = (double)image_data.Length / expected_astc_size;

        // 2: LZ4 pattern - first 4 bytes as decompressed size
        int potential_decomp_size = BitConverter.ToInt32(image_data, 0);
        _ = (double)potential_decomp_size / expected_astc_size;
        bool lz4_works;
        double decomp_ratio;
        try
        {
            // 3: Empirical decompression test
            byte[]? decompressed = LZ4Decompress(image_data);
            decomp_ratio = (double)decompressed.Length / expected_astc_size;
            lz4_works = decompressed.Length > 0;
        }
        catch
        {
            decomp_ratio = 0;
            lz4_works = false;
        }

        // Decision logic:
        // 1. If data is significantly smaller than expected (< 0.95) AND
        // 2. LZ4 decompression works AND
        // 3. Decompressed result is closer to expected size
        bool should_decompress = (
            size_ratio < 0.95 &&
            lz4_works &&
            decomp_ratio > size_ratio
        );

        if (verbose)
        {
            if (should_decompress)
            {
                Console.WriteLine("Intelligent detection: data appears to be LZ4 compressed");
            }
            else
            {
                Console.WriteLine("Intelligent detection: data appears to be already decompressed");
            }
        }

        return should_decompress;
    }

    /// <summary>
    /// Converts RGB565 Little Endian data to RGB
    /// </summary>
    /// <param name="data">Array of bytes in RGB565 LE format</param>
    /// <returns>Array of bytes in RGB format</returns>
    private static byte[] RGB565LEToRGB(byte[] data)
    {
        List<byte> rgb_data = new List<byte>();

        for (int i = 0; i < data.Length; i += 2)
        {
            if (i + 1 < data.Length)
            {
                // Little endian: low byte first
                ushort pixel = BitConverter.ToUInt16(data, i);

                // Convert 5/6/5 bits to 8 bits per channel
                byte r = (byte)(((pixel >> 11) & 0x1F) << 3);  // 5 bits red -> 8 bits
                byte g = (byte)(((pixel >> 5) & 0x3F) << 2);   // 6 bits green -> 8 bits
                byte b = (byte)((pixel & 0x1F) << 3);          // 5 bits blue -> 8 bits

                rgb_data.Add(r);
                rgb_data.Add(g);
                rgb_data.Add(b);
            }
        }

        return rgb_data.ToArray();
    }

    /// <summary>
    /// Decodes ETC2 RGBA8 data to RGBA
    /// </summary>
    /// <param name="compressed_data">Compressed ETC2 data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>Decoded RGBA data</returns>
    /// <exception cref="Exception">Thrown when decoding fails</exception>
    private static byte[] DecodeETC2ToRGBA(byte[] compressed_data, int width, int height, bool verbose = false)
    {
        // Try texture2ddecoder first
        try
        {
            if (verbose) Console.WriteLine("Attempting ETC2 decode with texture2ddecoder...");

            // texture2ddecoder supports ETC2 RGBA8
            var decoded = new byte[width * height * 4];
            TextureDecoder.DecodeETC2A8(compressed_data, width, height, decoded);
            if (verbose) Console.WriteLine($"Texture2ddecoder decode successful: {decoded.Length} bytes");
            return decoded;
        }
        catch (Exception e)
        {
            if (verbose) Console.WriteLine($"Texture2ddecoder ETC2A8 error: {e.Message}");

            // Try ETC2 RGB
            try
            {
                var decoded_rgb = new byte[width * height * 3];
                TextureDecoder.DecodeETC2(compressed_data, width, height, decoded_rgb);

                // Convert RGB to RGBA
                var decoded_rgba = new byte[decoded_rgb.Length / 3 * 4];
                for (int i = 0, j = 0; i < decoded_rgb.Length; i += 3, j += 4)
                {
                    decoded_rgba[j] = decoded_rgb[i];     // R
                    decoded_rgba[j + 1] = decoded_rgb[i + 1]; // G
                    decoded_rgba[j + 2] = decoded_rgb[i + 2]; // B
                    decoded_rgba[j + 3] = 255;            // Alpha opaque
                }

                if (verbose) Console.WriteLine($"Texture2ddecoder ETC2 RGB decode successful: {decoded_rgba.Length} bytes");
                return decoded_rgba;
            }
            catch (Exception e2)
            {
                if (verbose) Console.WriteLine($"Texture2ddecoder ETC2 error: {e2.Message}");
            }
        }

        // Calculate block dimensions
        const int block_size = 16; // ETC2 RGBA8 uses 16 bytes per 4x4 block
        int blocks_x = (width + 3) / 4;
        int blocks_y = (height + 3) / 4;
        int expected_size = blocks_x * blocks_y * block_size;

        if (verbose)
        {
            Console.WriteLine($"ETC2 decode: {width}x{height}, blocks {blocks_x}x{blocks_y}");
            Console.WriteLine($"Required data: {expected_size} bytes, available: {compressed_data.Length} bytes");
        }

        // Pad data if necessary
        byte[] padded_data;
        if (compressed_data.Length < expected_size)
        {
            if (verbose) Console.WriteLine($"Warning: insufficient data, using {compressed_data.Length} bytes");
            padded_data = new byte[expected_size];
            Array.Copy(compressed_data, padded_data, compressed_data.Length);
        }
        else
        {
            padded_data = new byte[expected_size];
            Array.Copy(compressed_data, padded_data, expected_size);
        }

        try
        {
            // Decode ETC2 RGBA8 using blocks
            var decoded = new byte[blocks_x * blocks_y * 16];
            TextureDecoder.DecodeETC2A8(padded_data, blocks_x * 4, blocks_y * 4, decoded);
            if (verbose) Console.WriteLine($"ETC2 RGBA decode successful: {decoded.Length} bytes");

            // Decoded data is for (blocks_x*4) x (blocks_y*4)
            // Crop to original size if necessary
            int decoded_width = blocks_x * 4;
            int decoded_height = blocks_y * 4;

            if (decoded_width != width || decoded_height != height)
            {
                if (verbose) Console.WriteLine($"Cropping from {decoded_width}x{decoded_height} to {width}x{height}");

                var cropped = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    Array.Copy(decoded, y * decoded_width * 4, cropped, y * width * 4, width * 4);
                }

                if (verbose) Console.WriteLine($"Cropping complete: {cropped.Length} bytes");
                return cropped;
            }

            return decoded;
        }
        catch (Exception e)
        {
            if (verbose) Console.WriteLine($"ETC2 RGBA decode error: {e.Message}");

            // Fallback: try ETC2 RGB with 8 bytes per block
            try
            {
                const int rgb_block_size = 8;
                int rgb_expected_size = blocks_x * blocks_y * rgb_block_size;

                var rgb_data = new byte[rgb_expected_size];
                Array.Copy(compressed_data, rgb_data, Math.Min(compressed_data.Length, rgb_expected_size));

                var decoded_rgb = new byte[blocks_x * blocks_y * 48];  // 48 bytes per 4x4 block (3 bytes per pixel)
                TextureDecoder.DecodeETC2(rgb_data, blocks_x * 4, blocks_y * 4, decoded_rgb);
                if (verbose) Console.WriteLine($"ETC2 RGB decode successful: {decoded_rgb.Length} bytes");

                // Convert RGB to RGBA and crop if necessary
                var rgba = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int src_idx = (y * blocks_x * 4 + x) * 3;
                        int dst_idx = (y * width + x) * 4;

                        if (src_idx + 2 < decoded_rgb.Length)
                        {
                            rgba[dst_idx] = decoded_rgb[src_idx];     // R
                            rgba[dst_idx + 1] = decoded_rgb[src_idx + 1]; // G
                            rgba[dst_idx + 2] = decoded_rgb[src_idx + 2]; // B
                            rgba[dst_idx + 3] = 255;                  // A
                        }
                    }
                }

                if (verbose) Console.WriteLine($"RGB->RGBA conversion complete: {rgba.Length} bytes");
                return rgba;
            }
            catch (Exception e2)
            {
                if (verbose) Console.WriteLine($"Final ETC2 error: {e2.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Determines pixel format and channels based on format code.
    /// Format mapping reverse-engineered from game's image loader (sub_2F253E0).
    /// The game uses these format codes to determine texture decompression method.
    /// </summary>
    /// <param name="format_code">Format code from header</param>
    /// <returns>Tuple containing format name, number of channels, and format type</returns>
    private static (string Format, int Channels, string Type) GetPixelFormatInfo(int format_code)
    {
        // Define format mapping dictionary
        var format_map = new Dictionary<int, (string Format, int Channels, string Type)>
        {
            { 4, ("RGB", 3, "RGB565_LE") },     // Format 4 is RGB565 Little Endian (confirmed)
            { 6, ("RGB", 3, "RGB") },
            { 16, ("RGB", 3, "RGB565") },
            { 19, ("RGBA", 4, "ETC2_RGBA8") },  // Pixel format 19 is ETC2 RGBA8
            { 40, ("RGBA", 4, "ASTC_4x4") },    // Format 40 is compressed ASTC 4x4
            { 44, ("RGBA", 4, "ASTC_6x6") },    // Format 44 is compressed ASTC 6x6
            { 47, ("RGBA", 4, "ASTC_8x8") },    // Format 47 is compressed ASTC 8x8
            { 102, ("L", 1, "L8") },
        };

        // Formats with alpha (17-26) - but exclude 19 and 40 which have specific decoders
        if (format_code >= 17 && format_code <= 26 && format_code != 19 && format_code != 40)
        {
            return ("RGBA", 4, "RGBA");
        }

        // Compressed formats (41-53) - exclude those with specific decoders
        var excluded_formats = new[] { 44, 47 };
        if (format_code >= 41 && format_code <= 53 && !excluded_formats.Contains(format_code))
        {
            return ("RGBA", 4, "COMPRESSED");
        }

        // Return from map or default to RGBA/UNKNOWN
        return format_map.TryGetValue(format_code, out var result)
            ? result
            : ("RGBA", 4, "UNKNOWN");
    }

    /// <summary>
    /// Complete parsing of an SCT file data
    /// </summary>
    /// <param name="data">Raw byte array of the SCT file</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>Dictionary containing parsed header and image data</returns>
    /// <exception cref="ArgumentException">Thrown if file format is invalid</exception>
    private static SCTFile ParseSCTFile(byte[] data, bool verbose = false)
    {
        // Verify format
        int format_type = DetectFormat(data, verbose);
        if (format_type != 10001)
            throw new ArgumentException($"File is not a valid SCT format (detected format: {format_type})");

        // Parse header
        SCTFile file = new SCTFile();
        file.Header = ParseSCTHeader(data);

        if (verbose)
        {
            Console.WriteLine("SCT Header:");
            Console.WriteLine($"  Dimensions: {file.Header.Width}x{file.Header.Height}");
            Console.WriteLine($"  Pixel format: {file.Header.PixelFormat}");
            Console.WriteLine($"  Compressed: {file.Header.Compressed}");
        }

        // Extract image data
        int image_data_start = Convert.ToInt32(file.Header.DataOffset);
        byte[] image_data = new byte[data.Length - image_data_start];
        Array.Copy(data, image_data_start, image_data, 0, data.Length - image_data_start);

        // Decompress (SCT always uses compression)
        if (verbose) Console.WriteLine("Decompressing data...");
        try
        {
            image_data = LZ4Decompress(image_data);
            if (verbose) Console.WriteLine($"Decompressed: {image_data.Length} bytes");
        }
        catch (Exception e)
        {
            if (verbose) Console.WriteLine($"Error during decompression: {e.Message}");
            throw;
        }

        file.ImageData = image_data;
        (file.PixelFormatName, file.Channels, file.FormatType) = GetPixelFormatInfo(Convert.ToInt32(file.Header.PixelFormat));
        return file;
    }

    /// <summary>
    /// Complete parsing of an SCT2 file data
    /// </summary>
    /// <param name="data">Raw byte array of the SCT2 file</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>Dictionary containing parsed header and image data</returns>
    /// <exception cref="ArgumentException">Thrown if file format is invalid</exception>
    private static SCT2File ParseSCT2File(byte[] data, bool verbose = false)
    {
        // Verify format
        //int format_type = detect_format(data, verbose);
        //if (format_type != 10002)
        //throw new ArgumentException($"File is not a valid SCT2 format (detected format: {format_type})");

        // Parse header
        SCT2File file = new SCT2File();
        file.Header = ParseSCT2Header(data);

        if (verbose)
        {
            Console.WriteLine("SCT2 Header:");
            Console.WriteLine($"  Dimensions: {file.Header.Width}x{file.Header.Height}");
            Console.WriteLine($"  Pixel format: {file.Header.PixelFormat}");
            Console.WriteLine($"  Flags: 0x{file.Header.Flags:X2}");
            Console.WriteLine($"  Compressed: {file.Header.Compressed}");
            Console.WriteLine($"  Has alpha: {file.Header.HasAlpha}");
            Console.WriteLine($"  Raw data: {file.Header.RawData}");
        }

        // Extract image data
        int image_data_start = Convert.ToInt32(file.Header.DataOffset);
        byte[] image_data = new byte[data.Length - image_data_start];
        Array.Copy(data, image_data_start, image_data, 0, data.Length - image_data_start);

        // Decompression logic based on flags
        byte[]? decompressed_image_data = null;

        // If raw_data flag (0x10) or has_alpha flag (0x01) is set, use intelligent detection
        // The game tests bit 4 to decide decompression strategy
        if (file.Header.RawData || file.Header.HasAlpha)
        {
            string flag_name = file.Header.RawData ? "raw_data (0x10)" : "has_alpha (0x01)";
            if (verbose)
                Console.WriteLine($"Flag {flag_name} detected: {image_data.Length} bytes");

            if (ShouldDecompressIntelligently(
                image_data,
                Convert.ToInt32(file.Header.Width),
                Convert.ToInt32(file.Header.Height),
                Convert.ToInt32(file.Header.PixelFormat),
                verbose))
            {
                try
                {
                    decompressed_image_data = LZ4Decompress(image_data);
                    if (verbose)
                        Console.WriteLine($"LZ4 decompression applied: {decompressed_image_data.Length} bytes");
                }
                catch (Exception e)
                {
                    if (verbose)
                        Console.WriteLine($"Decompression failed, using raw data: {e.Message}");
                    decompressed_image_data = image_data;
                }
            }
            else
            {
                if (verbose)
                    Console.WriteLine("Using raw data without decompression");
                decompressed_image_data = image_data;
            }
        }
        // Otherwise, attempt decompression if compressed flag (bit 31) is set
        else if (image_data.Length >= 8 && file.Header.Compressed)
        {
            try
            {
                // Attempt LZ4 decompression
                decompressed_image_data = LZ4Decompress(image_data);
                if (verbose)
                    Console.WriteLine($"Decompression successful: {decompressed_image_data.Length} bytes");
            }
            catch (Exception e)
            {
                if (verbose)
                    Console.WriteLine($"Decompression failed: {e.Message}");
                // raw data fallback
                decompressed_image_data = image_data;
            }
        }
        else if (decompressed_image_data == null && image_data.Length >= 8)
        {
            try
            {
                decompressed_image_data = LZ4Decompress(image_data);
                if (verbose)
                    Console.WriteLine($"Decompression successful: {decompressed_image_data.Length} bytes");
            }
            catch (Exception e)
            {
                if (verbose)
                    Console.WriteLine($"Decompression failed (using raw data): {e.Message}");
                // raw data fallback
                decompressed_image_data = image_data;
            }
        }

        // final fallback
        if (decompressed_image_data == null)
        {
            decompressed_image_data = image_data;
        }

        file.ImageData = decompressed_image_data;
        (file.PixelFormatName, file.Channels, file.FormatType) = GetPixelFormatInfo(Convert.ToInt32(file.Header.PixelFormat));
        return file;
    }


    /// <summary>
    /// Converts SCT/SCT2 data to PNG format
    /// </summary>
    /// <param name="data">Raw byte array of the SCT/SCT2 file</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>PNG image data as byte array, or null if conversion fails</returns>
    public static byte[] ConvertToPNG(byte[] data, bool verbose = false)
    {
        try
        {
            int format_type = DetectFormat(data);
            File result;

            if (format_type == 10002)
            {
                result = ParseSCT2File(data, verbose);
            }
            else if (format_type == 10001)
            {
                result = ParseSCTFile(data, verbose);
            }
            else
            {
                if (verbose) Console.WriteLine($"Unsupported format: {format_type}");
                return null;
            }

            if (result == null) return null;

            int width = result.Width;
            int height = result.Height;
            byte[] image_data = result.ImageData;

            byte[] final_rgba_data;

            switch (result.FormatType)
            {
                case "RGB565_LE":
                    if (verbose) Console.WriteLine("Decoding RGB565 Little Endian...");
                    final_rgba_data = ConvertRGBToRGBA(RGB565LEToRGB(image_data));
                    break;

                case "ETC2_RGBA8":
                    if (verbose) Console.WriteLine("Decoding ETC2 RGBA8...");
                    final_rgba_data = DecodeETC2ToRGBA(image_data, width, height, verbose);
                    break;

                case "ASTC_4x4":
                    if (verbose) Console.WriteLine("Decoding ASTC 4x4...");
                    final_rgba_data = DecodeAstcToRgba(image_data, width, height, 4, 4);
                    break;

                case "ASTC_6x6":
                    if (verbose) Console.WriteLine("Decoding ASTC 6x6...");
                    final_rgba_data = DecodeAstcToRgba(image_data, width, height, 6, 6);
                    break;

                case "ASTC_8x8":
                    if (verbose) Console.WriteLine("Decoding ASTC 8x8...");
                    final_rgba_data = DecodeAstcToRgba(image_data, width, height, 8, 8);
                    break;

                case "L8":
                    if (verbose) Console.WriteLine("Converting L8 to RGBA...");
                    final_rgba_data = ConvertL8ToRGBA(image_data);
                    break;

                case "RGB":
                    if (verbose) Console.WriteLine("Converting RGB to RGBA...");
                    final_rgba_data = ConvertRGBToRGBA(image_data);
                    break;

                case "RGBA":
                    final_rgba_data = image_data;
                    break;

                default:
                    if (verbose) Console.WriteLine($"Using fallback conversion for type {result.FormatType}");
                    final_rgba_data = result.Channels switch
                    {
                        4 => image_data,
                        3 => ConvertRGBToRGBA(image_data),
                        1 => ConvertL8ToRGBA(image_data),
                        _ => image_data
                    };
                    break;
            }

            if (final_rgba_data == null || final_rgba_data.Length == 0)
            {
                if (verbose) Console.WriteLine("Error: No valid image data produced");
                return null;
            }

            using (var image = new Image<Rgba32>(width, height))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int pixelIndex = (y * width + x) * 4;
                        if (pixelIndex + 3 < final_rgba_data.Length)
                        {
                            image[x, y] = new Rgba32(
                                final_rgba_data[pixelIndex],
                                final_rgba_data[pixelIndex + 1],
                                final_rgba_data[pixelIndex + 2],
                                final_rgba_data[pixelIndex + 3]
                            );
                        }
                    }
                }

                using (var ms = new MemoryStream())
                {
                    image.SaveAsPng(ms);
                    return ms.ToArray();
                }
            }
        }
        catch (Exception e)
        {
            if (verbose) Console.WriteLine($"Error during conversion: {e.Message}");
            return null;
        }
    }

    private static byte[] DecodeAstcToRgba(byte[] imageData, int width, int height, int blockWidth, int blockHeight)
    {
        var rgba = new byte[width * height * 4];
        TextureDecoder.DecodeASTC(imageData, width, height, blockWidth, blockHeight, rgba);
        BGRA_SwapRB(rgba);
        return rgba;
    }

    private static byte[] ConvertRGBToRGBA(byte[] rgb_data)
    {
        var rgba = new byte[rgb_data.Length / 3 * 4];
        for (int i = 0, j = 0; i < rgb_data.Length - 2; i += 3, j += 4)
        {
            rgba[j] = rgb_data[i];     // R
            rgba[j + 1] = rgb_data[i + 1]; // G
            rgba[j + 2] = rgb_data[i + 2]; // B
            rgba[j + 3] = 255;         // A (opaque)
        }
        return rgba;
    }

    private static byte[] ConvertL8ToRGBA(byte[] l8_data)
    {
        var rgba = new byte[l8_data.Length * 4];
        for (int i = 0; i < l8_data.Length; i++)
        {
            byte gray = l8_data[i];
            rgba[i * 4 + 0] = gray;
            rgba[i * 4 + 1] = gray;
            rgba[i * 4 + 2] = gray;
            rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    // Swap R and B channels for BGRA->RGBA conversion in-place
    private static void BGRA_SwapRB(byte[] buffer)
    {
        for (int i = 0; i + 3 < buffer.Length; i += 4)
        {
            byte b = buffer[i];      // B
            buffer[i] = buffer[i + 2];   // R
            buffer[i + 2] = b;           // B <- R
        }
    }
}
