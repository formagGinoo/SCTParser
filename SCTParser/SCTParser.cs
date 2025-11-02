using System;
using Texture2DDecoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SCTParser;

public class SCTParser
{
    public static int SCT2_SIGNATURE = 844383059; // "SCT2"
    private const int SCT_SIGNATURE_WORD = 17235;  // "SC"
    private const byte SCT_SIGNATURE_BYTE = 84;    // "T"

    /// <summary>
    /// Detect the format of the given byte array.
    /// </summary>
    /// <param name="data">Array of bytes to analyze</param>
    /// <param name="debug">Flag to enable debug messages</param>
    /// <returns>10002 for sct2, 10001 for sct, -1 for unknown format</returns>
    private static int detect_format(byte[] data, bool debug = false)
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
    /// Parsing dell'header SCT based on analysis of various SCT files.
    /// </summary>
    /// <param name="data">array of bytes containing the SCT header</param>
    /// <returns>A Dictionary object containing the header parameters</returns>
    /// <exception cref="ArgumentException">Thrown if the file is too small</exception>
    public static Dictionary<string, object> parse_sct_header(byte[] data)
    {
        if (data.Length < 9)
            throw new ArgumentException("File too small to contain a valid SCT header");

        var header = new Dictionary<string, object>();

        // Offset 0-2: Signature "SCT" (already verified)
        header["signature"] = data.Take(3).ToArray();

        // Offset 3: Padding/Unknown
        header["unknown"] = data[3];

        // Offset 4: format pixel
        header["pixel_format"] = data[4];

        // Offset 5-6: width
        header["width"] = BitConverter.ToUInt16(data, 5);

        // Offset 7-8: height
        header["height"] = BitConverter.ToUInt16(data, 7);

        // compressed data starts at offset 9
        header["data_offset"] = 9;
        header["compressed"] = true;  // SCT uses LZ4 compression

        // For SCT, texture dimensions = image dimensions
        header["texture_width"] = header["width"];
        header["texture_height"] = header["height"];

        // SCT has no explicit flags in the header
        header["flags"] = 0;
        header["has_alpha"] = false;
        header["crop_flag"] = false;
        header["mipmap_flag1"] = false;
        header["mipmap_flag2"] = false;

        return header;
    }

    /// <summary>
    /// Parse SCT2 header based on assembly code analysis
    /// </summary>
    /// <param name="data">Array of bytes containing the SCT2 header</param>
    /// <returns>A Dictionary object containing the header parameters</returns>
    /// <exception cref="ArgumentException">Thrown if the file is too small</exception>
    public static Dictionary<string, object> parse_sct2_header(byte[] data)
    {
        if (data.Length < 34)
            throw new ArgumentException("File too small to contain a valid SCT2 header");

        var header = new Dictionary<string, object>();

        // Offset 0: Signature (already verified)
        header["signature"] = BitConverter.ToInt32(data, 0);

        // Offset 4: Total data size
        header["total_size"] = BitConverter.ToInt32(data, 4);

        // Offset 8: Unknown (possibly padding)
        header["unknown1"] = BitConverter.ToInt32(data, 8);

        // Offset 12: Image data offset
        header["data_offset"] = BitConverter.ToInt32(data, 12);

        // Offset 16: Unknown
        header["unknown2"] = BitConverter.ToInt32(data, 16);

        // Offset 20: Pixel format
        header["pixel_format"] = BitConverter.ToInt32(data, 20);

        // Offset 24-25: Width
        header["width"] = BitConverter.ToUInt16(data, 24);

        // Offset 26-27: Height
        header["height"] = BitConverter.ToUInt16(data, 26);

        // Offset 28-29: Texture width
        header["texture_width"] = BitConverter.ToUInt16(data, 28);

        // Offset 30-31: Texture height
        header["texture_height"] = BitConverter.ToUInt16(data, 30);

        // Offset 32: Flags
        header["flags"] = data[32];

        // Flags analysis
        header["has_alpha"] = (((byte)header["flags"] & 0x01) != 0);
        header["crop_flag"] = (((byte)header["flags"] & 0x02) != 0);
        header["raw_data"] = (((byte)header["flags"] & 0x10) != 0);  // Flag 0x10: uncompressed/raw data
        header["mipmap_flag2"] = (((byte)header["flags"] & 0x20) != 0);
        header["compressed"] = (((byte)header["flags"] & 0x80) != 0);

        return header;
    }

    /// <summary>
    /// LZ4 decompression implementation based on assembly code analysis
    /// </summary>
    /// <param name="compressed_data">Compressed data array</param>
    /// <returns>Decompressed data as byte array</returns>
    /// <exception cref="ArgumentException">Thrown if compressed data is too short</exception>
    public static byte[] lz4_decompress(byte[] compressed_data)
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
    /// based on intelligent criteria (for flags 0x01, 0x10, etc.)
    /// </summary>
    /// <param name="image_data">Raw image data</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="pixel_format">Pixel format code</param>
    /// <param name="verbose">Enable detailed output</param>
    /// <returns>True if data should be decompressed, false otherwise</returns>
    public static bool should_decompress_intelligently(byte[] image_data, int width, int height, int pixel_format, bool verbose = false)
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
            expected_astc_size = width * height * 2;  // Approximation
        }

        // Criterion 1: Size ratio
        double size_ratio = (double)image_data.Length / expected_astc_size;

        // Criterion 2: LZ4 pattern - first 4 bytes as decompressed size
        int potential_decomp_size = BitConverter.ToInt32(image_data, 0);
        double size_ratio_potential = (double)potential_decomp_size / expected_astc_size;

        // Criterion 3: Empirical decompression test
        byte[]? decompressed = null;
        double decomp_ratio = 0;
        bool lz4_works = false;

        try
        {
            decompressed = lz4_decompress(image_data);
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
            size_ratio < 0.95 &&     // Data too small
            lz4_works &&             // LZ4 works
            decomp_ratio > size_ratio // Decompressed result is better
        );

        if (verbose)
        {
            if (should_decompress)
            {
                Console.WriteLine("Intelligent detection: data appears to be LZ4 compressed");
                Console.WriteLine($"   Size ratio: {size_ratio:F2} (< 0.95)");
                Console.WriteLine($"   LZ4 decompression: works ({decomp_ratio:F2})");
            }
            else
            {
                Console.WriteLine("Intelligent detection: data appears to be already decompressed");
                Console.WriteLine($"   Size ratio: {size_ratio:F2}");
                if (!lz4_works)
                    Console.WriteLine("   LZ4 decompression: fails");
            }
        }

        return should_decompress;
    }

    /// <summary>
    /// Converts RGB565 Little Endian data to RGB
    /// </summary>
    /// <param name="data">Array of bytes in RGB565 LE format</param>
    /// <returns>Array of bytes in RGB format</returns>
    public static byte[] rgb565_le_to_rgb(byte[] data)
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
    public static byte[] decode_etc2_rgba8(byte[] compressed_data, int width, int height, bool verbose = false)
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
    /// Determines pixel format and channels based on format code
    /// </summary>
    /// <param name="format_code">Format code from header</param>
    /// <returns>Tuple containing format name, number of channels, and format type</returns>
    public static (string Format, int Channels, string Type) get_pixel_format_info(int format_code)
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
        };

        // Formats with alpha (17-26) - but exclude 19 and 40 which are special
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
    public static Dictionary<string, object> parse_sct_file(byte[] data, bool verbose = false)
    {
        // Verify format
        int format_type = detect_format(data, verbose);
        if (format_type != 10001)
            throw new ArgumentException($"File is not a valid SCT format (detected format: {format_type})");

        // Parse header
        var header = parse_sct_header(data);

        if (verbose)
        {
            Console.WriteLine("SCT Header:");
            Console.WriteLine($"  Dimensions: {header["width"]}x{header["height"]}");
            Console.WriteLine($"  Pixel format: {header["pixel_format"]}");
            Console.WriteLine($"  Compressed: {header["compressed"]}");
        }

        // Extract image data
        int image_data_start = Convert.ToInt32(header["data_offset"]);
        byte[] image_data = new byte[data.Length - image_data_start];
        Array.Copy(data, image_data_start, image_data, 0, data.Length - image_data_start);

        // Decompress (SCT always uses compression)
        if (verbose) Console.WriteLine("Decompressing data...");
        try
        {
            image_data = lz4_decompress(image_data);
            if (verbose) Console.WriteLine($"Decompressed: {image_data.Length} bytes");
        }
        catch (Exception e)
        {
            if (verbose) Console.WriteLine($"Error during decompression: {e.Message}");
            throw;
        }

        // Determine pixel format
        var (pixel_format_name, channels, format_type_str) = get_pixel_format_info(Convert.ToInt32(header["pixel_format"]));

        // Return all parsed data
        return new Dictionary<string, object>
        {
            { "header", header },
            { "image_data", image_data },
            { "pixel_format", pixel_format_name },
            { "channels", channels },
            { "format_type", format_type_str }
        };
    }

    /// <summary>
    /// Complete parsing of an SCT2 file data
    /// </summary>
    /// <param name="data">Raw byte array of the SCT2 file</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>Dictionary containing parsed header and image data</returns>
    /// <exception cref="ArgumentException">Thrown if file format is invalid</exception>
    private static Dictionary<string, object> parse_sct2_file(byte[] data, bool verbose = false)
    {
        // Verify format
        //int format_type = detect_format(data, verbose);
        //if (format_type != 10002)
        //throw new ArgumentException($"File is not a valid SCT2 format (detected format: {format_type})");

        // Parse header
        var header = parse_sct2_header(data);

        if (verbose)
        {
            Console.WriteLine("SCT2 Header:");
            Console.WriteLine($"  Dimensions: {header["width"]}x{header["height"]}");
            Console.WriteLine($"  Pixel format: {header["pixel_format"]}");
            Console.WriteLine($"  Flags: 0x{header["flags"]:X2}");
            Console.WriteLine($"  Compressed: {header["compressed"]}");
            Console.WriteLine($"  Has alpha: {header["has_alpha"]}");
            Console.WriteLine($"  Raw data: {header["raw_data"]}");
        }

        // Extract image data
        int image_data_start = Convert.ToInt32(header["data_offset"]);
        byte[] image_data = new byte[data.Length - image_data_start];
        Array.Copy(data, image_data_start, image_data, 0, data.Length - image_data_start);

        // Decompression logic based on flags
        byte[]? decompressed_image_data = null;

        // If raw_data flag is set (0x10) or has_alpha flag (0x01), use intelligent detection
        if ((bool)header["raw_data"] || (bool)header["has_alpha"])
        {
            string flag_name = (bool)header["raw_data"] ? "raw_data (0x10)" : "has_alpha (0x01)";
            if (verbose)
                Console.WriteLine($"Flag {flag_name} detected: {image_data.Length} bytes");

            // Use intelligent detection to decide if decompression is needed
            if (should_decompress_intelligently(
                image_data,
                Convert.ToInt32(header["width"]),
                Convert.ToInt32(header["height"]),
                Convert.ToInt32(header["pixel_format"]),
                verbose))
            {
                try
                {
                    decompressed_image_data = lz4_decompress(image_data);
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
        // Otherwise, attempt decompression if indicated by flags or for format 40
        else if (image_data.Length >= 8 && (Convert.ToInt32(header["pixel_format"]) == 40 || Convert.ToBoolean(header["compressed"])))
        {
            try
            {
                // Attempt LZ4 decompression
                decompressed_image_data = lz4_decompress(image_data);
                if (verbose)
                    Console.WriteLine($"Decompression successful: {decompressed_image_data.Length} bytes");
            }
            catch (Exception e)
            {
                if (verbose)
                    Console.WriteLine($"Decompression failed: {e.Message}");
                // If decompression fails, use raw data
                decompressed_image_data = image_data;
            }
        }
        // If we still don't have processed data, try anyway for other formats
        else if (decompressed_image_data == null && image_data.Length >= 8)
        {
            try
            {
                // Attempt decompression to see if it works
                decompressed_image_data = lz4_decompress(image_data);
                if (verbose)
                    Console.WriteLine($"Decompression successful: {decompressed_image_data.Length} bytes");
            }
            catch (Exception e)
            {
                if (verbose)
                    Console.WriteLine($"Decompression failed (using raw data): {e.Message}");
                // If decompression fails, use raw data
                decompressed_image_data = image_data;
            }
        }

        // Final fallback to ensure we always have data
        if (decompressed_image_data == null)
        {
            decompressed_image_data = image_data;
        }

        // Determine pixel format
        var (pixel_format_name, channels, format_type_str) = get_pixel_format_info(Convert.ToInt32(header["pixel_format"]));

        // Return all parsed data
        return new Dictionary<string, object>
        {
            { "header", header },
            { "image_data", decompressed_image_data },
            { "pixel_format", pixel_format_name },
            { "channels", channels },
            { "format_type", format_type_str }
        };
    }


    /// <summary>
    /// Converts SCT/SCT2 data to PNG format
    /// </summary>
    /// <param name="data">Raw byte array of the SCT/SCT2 file</param>
    /// <param name="verbose">Enable detailed output messages</param>
    /// <returns>PNG image data as byte array, or null if conversion fails</returns>
    public static byte[]? convert_to_png(byte[] data, bool verbose = false)
    {
        try
        {
            // Determine file type
            int format_type = detect_format(data);
            Dictionary<string, object>? result = null;

            if (format_type == 10002)  // SCT2
            {
                result = parse_sct2_file(data, verbose);
            }
            else if (format_type == 10001)  // SCT
            {
                result = parse_sct_file(data, verbose);
            }
            else
            {
                if (verbose) Console.WriteLine($"Unsupported format: {format_type}");
                return null;
            }

            if (result == null) return null;

            var header = (Dictionary<string, object>)result["header"];
            byte[] image_data = (byte[])result["image_data"];
            string format_type_str = (string)result["format_type"];
            int width, height;

            // Always use image dimensions for all formats
            width = Convert.ToInt32(header["width"]);
            height = Convert.ToInt32(header["height"]);

            // Process based on format type
            byte[] final_rgba_data;
            bool has_alpha = false;

            switch (format_type_str)
            {
                case "RGB565_LE":
                    if (verbose) Console.WriteLine("Decoding RGB565 Little Endian...");
                    var rgb_data = rgb565_le_to_rgb(image_data);
                    final_rgba_data = convert_rgb_to_rgba(rgb_data);
                    break;

                case "ETC2_RGBA8":
                    if (verbose) Console.WriteLine("Decoding ETC2 RGBA8...");
                    final_rgba_data = decode_etc2_rgba8(image_data, width, height, verbose);
                    has_alpha = true;
                    break;

                case "ASTC_4x4":
                    if (verbose) Console.WriteLine("Decoding ASTC 4x4...");
                    final_rgba_data = new byte[width * height * 4];
                    TextureDecoder.DecodeASTC(image_data, width, height, 4, 4, final_rgba_data);
                    BGRA_SwapRB(final_rgba_data); // ASTC output expected as BGRA in codebase
                    has_alpha = true;
                    break;

                case "ASTC_6x6":
                    if (verbose) Console.WriteLine("Decoding ASTC 6x6...");
                    final_rgba_data = new byte[width * height * 4];
                    TextureDecoder.DecodeASTC(image_data, width, height, 6, 6, final_rgba_data);
                    BGRA_SwapRB(final_rgba_data); // ASTC output expected as BGRA in codebase
                    has_alpha = true;
                    break;

                case "ASTC_8x8":
                    if (verbose) Console.WriteLine("Decoding ASTC 8x8...");
                    final_rgba_data = new byte[width * height * 4];
                    TextureDecoder.DecodeASTC(image_data, width, height, 8, 8, final_rgba_data);
                    BGRA_SwapRB(final_rgba_data); // ASTC output expected as BGRA in codebase
                    has_alpha = true;
                    break;

                default:
                    if (verbose) Console.WriteLine($"Using raw {format_type_str} data");
                    final_rgba_data = image_data;
                    has_alpha = format_type_str.Contains("RGBA") ||
                               (header.ContainsKey("has_alpha") && Convert.ToBoolean(header["has_alpha"]));
                    break;
            }

            if (final_rgba_data == null || final_rgba_data.Length == 0)
            {
                if (verbose) Console.WriteLine("Error: No valid image data produced");
                return null;
            }

            // Convert to ImageSharp format and save as PNG
            using (var image = new Image<Rgba32>(width, height))
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        int j = i * (has_alpha ? 4 : 3);

                        if (j < final_rgba_data.Length - (has_alpha ? 3 : 2))
                        {
                            image[x, y] = has_alpha
                                ? new Rgba32(
                                    final_rgba_data[j],     // R
                                    final_rgba_data[j + 1], // G
                                    final_rgba_data[j + 2], // B
                                    final_rgba_data[j + 3]  // A
                                )
                                : new Rgba32(
                                    final_rgba_data[j],     // R
                                    final_rgba_data[j + 1], // G
                                    final_rgba_data[j + 2], // B
                                    255                     // A (opaque)
                                );
                        }
                    }
                }

                // Save to memory stream
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

    private static byte[] convert_rgb_to_rgba(byte[] rgb_data)
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
