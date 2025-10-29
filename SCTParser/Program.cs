namespace SCTParser;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: SCTParser <input_path> <output_path> [--verbose]");
            Console.WriteLine("  input_path: File or directory to process");
            Console.WriteLine("  output_path: Output directory for PNG files");
            Console.WriteLine("  --verbose / --v: Optional flag for detailed output");
            return;
        }

        string inputPath = args[0];
        string outputPath = args[1];
        bool verbose = args.Length > 2 && (args[2] == "--verbose" || args[2] == "--v");

        try
        {
            // Create output directory if it doesn't exist
            Directory.CreateDirectory(outputPath);

            if (File.Exists(inputPath))
            {
                // Process single file
                ProcessFile(inputPath, outputPath, verbose);
            }
            else if (Directory.Exists(inputPath))
            {
                // Process directory
                ProcessDirectory(inputPath, outputPath, verbose);
            }
            else
            {
                Console.WriteLine($"Error: Input path '{inputPath}' does not exist");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            if (args.Any(arg => arg == "--verbose"))
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }

    static void ProcessDirectory(string inputDir, string outputDir, bool verbose)
    {
        // Process all files in current directory
        foreach (string file in Directory.GetFiles(inputDir))
        {
            if (Path.GetExtension(file).ToLower() is ".sct" or ".sct2")
            {
                ProcessFile(file, outputDir, verbose);
            }
        }

        // Process all subdirectories
        foreach (string dir in Directory.GetDirectories(inputDir))
        {
            string dirName = Path.GetFileName(dir);
            string newOutputDir = Path.Combine(outputDir, dirName);
            Directory.CreateDirectory(newOutputDir);
            ProcessDirectory(dir, newOutputDir, verbose);
        }
    }

    static void ProcessFile(string inputFile, string outputDir, bool verbose)
    {
        try
        {
            if (verbose) Console.WriteLine($"Processing: {inputFile}");

            // Read file
            byte[] data = File.ReadAllBytes(inputFile);

            // Convert to PNG
            byte[]? pngData = SCTParser.convert_to_png(data, verbose);

            if (pngData != null)
            {
                // Create output filename
                string outputFile = Path.Combine(outputDir, 
                    Path.GetFileNameWithoutExtension(inputFile) + ".png");

                // Save PNG file
                File.WriteAllBytes(outputFile, pngData);

                Console.WriteLine($"Saved: {outputFile}");
            }
            else
            {
                Console.WriteLine($"Failed to convert: {inputFile}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing {inputFile}: {ex.Message}");
        }
    }
}
