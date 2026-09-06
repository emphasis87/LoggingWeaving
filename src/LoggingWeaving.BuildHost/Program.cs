using System.Security.Cryptography;
using System.Text;
using LoggingWeaving.Rewriter;

return Run(args);

static int Run(string[] args)
{
    try
    {
        if (args is ["--check-runtime"])
        {
            Console.WriteLine($"LoggingWeaving build host runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            return 0;
        }

        IReadOnlyDictionary<string, string> arguments = ParseArguments(args);
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(arguments["--project"]))!;
        string outputRoot = Path.GetFullPath(arguments["--output-root"]);
        string[] mappedSources = File.ReadAllLines(arguments["--map"])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();

        SourceDocument[] sources = File.ReadAllLines(arguments["--sources"])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new SourceDocument(Path.GetFullPath(path), File.ReadAllText(path)))
            .ToArray();

        string[] references = File.ReadAllLines(arguments["--references"])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string[] defines = File.ReadAllText(arguments["--defines"])
            .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var request = new RewriteProjectRequest(
            sources,
            references,
            Path.GetFileNameWithoutExtension(arguments["--project"]),
            defines,
            new RewriteOptions(
                ProjectGuardEnabled: ParseProjectGuardMode(arguments["--project-guard-mode"]),
                NormalizeLanguageVersion(arguments["--language-version"])));

        RewriteProjectResult result = new LoggingSourceRewriter().Rewrite(request);

        foreach (RewriteDiagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(
                $"{diagnostic.Path}({diagnostic.Line},{diagnostic.Column}): warning {diagnostic.Id}: {diagnostic.Message}");
        }

        var documents = result.Documents.ToDictionary(
            document => Path.GetFullPath(document.Path),
            StringComparer.OrdinalIgnoreCase);
        List<string> generatedFiles = [];

        foreach (string sourcePath in mappedSources)
        {
            if (!documents.TryGetValue(sourcePath, out RewrittenDocument? document))
            {
                throw new InvalidOperationException($"Mapped source was not analyzed: {sourcePath}");
            }

            string outputPath = GetOutputPath(projectDirectory, outputRoot, sourcePath);
            WriteIfChanged(outputPath, document.Text);
            generatedFiles.Add(outputPath);
        }

        WriteIfChanged(arguments["--generated-files"], string.Join(Environment.NewLine, generatedFiles));

        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"LoggingWeaving failed: {exception}");
        return 1;
    }
}

static IReadOnlyDictionary<string, string> ParseArguments(string[] args)
{
    if (args.Length % 2 != 0)
    {
        throw new ArgumentException("Arguments must be supplied as --name value pairs.");
    }

    var result = new Dictionary<string, string>(StringComparer.Ordinal);

    for (int index = 0; index < args.Length; index += 2)
    {
        result.Add(args[index], args[index + 1]);
    }

    return result;
}

static string GetOutputPath(string projectDirectory, string outputRoot, string sourcePath)
{
    string relativePath = Path.GetRelativePath(projectDirectory, sourcePath);

    if (relativePath == ".." ||
        relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        Path.IsPathRooted(relativePath))
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..16];
        relativePath = Path.Combine("external", hash, Path.GetFileName(sourcePath));
    }

    string outputPath = Path.GetFullPath(Path.Combine(outputRoot, relativePath));
    string rootWithSeparator = outputRoot.TrimEnd(
        Path.DirectorySeparatorChar,
        Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    if (!outputPath.StartsWith(rootWithSeparator, comparison))
    {
        throw new InvalidOperationException(
            $"Generated path '{outputPath}' escapes output root '{outputRoot}'.");
    }

    return outputPath;
}

static string NormalizeLanguageVersion(string value) =>
    string.IsNullOrWhiteSpace(value) || value == "default" ? "14.0" : value;

static bool ParseProjectGuardMode(string value) => value switch
{
    "Enabled" => true,
    "Disabled" => false,
    _ => throw new ArgumentException(
        $"Unsupported project guard mode '{value}'. Expected 'Enabled' or 'Disabled'.")
};

static void WriteIfChanged(string path, string text)
{
    if (File.Exists(path) && File.ReadAllText(path) == text)
    {
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text, new UTF8Encoding(false));
}
