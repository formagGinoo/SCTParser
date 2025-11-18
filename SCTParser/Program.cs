using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SCTParser;

class Program
{
    static void Main(string[] args)
    {
        string? inputPath = null;
        string? outputPath = null;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++) 
        {
            string a = args[i];

            if (a == "--verbose" || a == "--v" || a == "-v") 
            {
                verbose = true;
                continue;
            }

            if (a == "-o" || a == "--out") 
            {
                if (i + 1 >= args.Length) 
                {
                    Console.WriteLine("Error: -o/--out requires a path.");
                    return;
                }
                outputPath = args[++i];
                continue;
            }

            if (a.StartsWith("--out=")) 
            {
                outputPath = a.Substring("--out=".Length);
                continue;
            }

            // positional
            if (inputPath == null) inputPath = a;
            else if (outputPath == null) outputPath = a;
            else 
            {
                Console.WriteLine($"Error: Unexpected argument '{a}'.");
                return;
            }
        }

        if (inputPath == null) 
        {
            Console.WriteLine("Usage: SCTParser <input_path> <output_path> [--verbose]");
            Console.WriteLine("  <input_path>     File or directory to process (.sct / .sct2)");
            Console.WriteLine("  [output_path]    Optional: output directory for PNG files (defaults to input directory)");
            Console.WriteLine("  -o, --out        Optional: explicit output directory");
            Console.WriteLine("  --verbose | --v  Optional: show detailed log output");
            return;
        }

        if (outputPath == null) 
        {
            if (File.Exists(inputPath)) 
            {
                outputPath = Path.GetDirectoryName(Path.GetFullPath(inputPath))
                             ?? Directory.GetCurrentDirectory();
            }
            else if (Directory.Exists(inputPath)) 
            {
                outputPath = inputPath;
            }
            else 
            {
                Console.WriteLine($"Error: Input path '{inputPath}' does not exist");
                return;
            }
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            Directory.CreateDirectory(outputPath);

            if (File.Exists(inputPath))
            {
                ProcessFile(inputPath, outputPath, verbose);
            }
            else if (Directory.Exists(inputPath))
            {
                ProcessDirectory(inputPath, outputPath, verbose);
            }
            else
            {
                Console.WriteLine($"Error: Input path '{inputPath}' does not exist");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal error: {ex.Message}");
            if (verbose)
                Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            stopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine($"Done. Elapsed time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        }
    }
	
    static readonly ConcurrentDictionary<string, bool> createdDirs = new();

    static void ProcessDirectory(string inputDir, string outputDir, bool verbose)
    {
        var files = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories)
                             .Where(f => Path.GetExtension(f).ToLower() is ".sct" or ".sct2");

        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
        {
            try
            {
                string relativePath = Path.GetRelativePath(inputDir, Path.GetDirectoryName(file)!);
                string outputSubdir = Path.Combine(outputDir, relativePath);

                if (createdDirs.TryAdd(outputSubdir, true))
                    Directory.CreateDirectory(outputSubdir);

                ProcessFile(file, outputSubdir, verbose);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing {file}: {ex.Message}");
                if (verbose)
                    Console.WriteLine(ex.StackTrace);
            }
        });
    }

    static void ProcessFile(string inputFile, string outputDir, bool verbose)
    {
        try
        {
            if (verbose)
                Console.WriteLine($"Processing: {inputFile}");

            byte[] data = File.ReadAllBytes(inputFile);
            byte[]? pngData = SCTParser.ConvertToPNG(data, verbose);

            if (pngData != null)
            {
                string outputFile = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputFile) + ".png");
                File.WriteAllBytes(outputFile, pngData);

                if (verbose)
                    Console.WriteLine($"Saved: {outputFile}");
            }
            else
            {
                Console.WriteLine($"\nFailed to convert: {inputFile}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError processing {inputFile}: {ex.Message}");
            if (verbose)
                Console.WriteLine(ex.StackTrace);
        }
    }
}
