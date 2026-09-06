using System.Reflection.Metadata;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LoggingWeaving.Rewriter.Tests;

public sealed class LoggingSourceRewriterTests
{
    [Fact]
    public void Rewrite_GuardsLoggingArguments()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Value: {Value}", BuildValue());
                }

                private static string BuildValue() => "value";
            }
            """;

        RewriteProjectResult result = Rewrite(source);
        string rewritten = Assert.Single(result.Documents).Text;

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("is not null", rewritten);
        Assert.Contains("IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information)", rewritten);
        Assert.True(
            rewritten.IndexOf("IsEnabled", StringComparison.Ordinal) <
            rewritten.IndexOf("BuildValue()", StringComparison.Ordinal));
        AssertCompiles(rewritten);
    }

    [Fact]
    public void Rewrite_SupportsCSharp14Syntax()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            internal static class WorkerExtensions
            {
                extension(ILogger logger)
                {
                    public void Run()
                    {
                        logger.LogInformation("C# 14 extension block");
                    }
                }
            }
            """;

        RewriteProjectResult result = Rewrite(source);
        string rewritten = Assert.Single(result.Documents).Text;

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information)", rewritten);
        AssertCompiles(rewritten, LanguageVersion.CSharp14);
    }

    [Fact]
    public void Rewrite_LeavesUnrelatedCallsUnchanged()
    {
        const string source = """
            internal static class Worker
            {
                public static void Run()
                {
                    WriteValue(CreateValue());
                }

                private static string CreateValue() => "value";
                private static void WriteValue(string value) { }
            }
            """;

        RewriteProjectResult result = Rewrite(source);

        Assert.Equal(0, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(WithLineMapping(source), Assert.Single(result.Documents).Text);
    }

    [Fact]
    public void Rewrite_GuardsStaticExtensionInvocation()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                public void Run(ILogger logger)
                {
                    LoggerExtensions.LogWarning(logger, "Warning");
                }
            }
            """;

        RewriteProjectResult result = Rewrite(source);

        string rewritten = Assert.Single(result.Documents).Text;

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("LogLevel.Warning", rewritten);
        AssertCompiles(rewritten);
    }

    [Fact]
    public void Rewrite_GuardsLoggerMessageInvocation()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            internal sealed partial class Worker
            {
                [LoggerMessage(Level = LogLevel.Error, Message = "Value: {Value}")]
                private static partial void LogValue(ILogger logger, string value);

                public void Run(ILogger logger)
                {
                    LogValue(logger, BuildValue());
                }

                private static string BuildValue() => "value";
            }
            """;

        RewriteProjectResult result = Rewrite(source);
        string rewritten = Assert.Single(result.Documents).Text;

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("LogLevel.Error", rewritten);
        Assert.True(
            rewritten.IndexOf("IsEnabled", StringComparison.Ordinal) <
            rewritten.IndexOf("BuildValue()", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ILogger?", false)]
    [InlineData("ILogger?", true)]
    [InlineData("ILogger<Worker>?", false)]
    [InlineData("ILogger<Worker>?", true)]
    public void Rewrite_RecognizesNullableLoggerAndPreservesSkipEnabledCheck(string loggerType, bool skipEnabledCheck)
    {
        string source = $$"""
            #nullable enable
            using Microsoft.Extensions.Logging;
            internal sealed class Worker
            {
                [LoggerMessage(Level = LogLevel.Information, Message = "{Value}", SkipEnabledCheck = {{skipEnabledCheck.ToString().ToLowerInvariant()}})]
                private static void LogValue({{loggerType}} logger, string value) { }

                public void Run({{loggerType}} logger)
                {
                    LogValue(logger, "value");
                }
            }
            """;
        RewriteProjectResult result = Rewrite(source);
        string rewritten = Assert.Single(result.Documents).Text;
        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Contains($"SkipEnabledCheck = {skipEnabledCheck.ToString().ToLowerInvariant()}", rewritten);
        Assert.Contains("is not null", rewritten);
        Assert.Contains("IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Information)", rewritten);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "NullableLoggerVerification",
            [CSharpSyntaxTree.ParseText(rewritten)],
            GetReferencePaths().Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
    }

    [Fact]
    public void Rewrite_MethodAttributeCanDisableWeaving()
    {
        const string source = """
            using LoggingWeaving;
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                [LoggingGuard(LoggingGuardMode.Disabled)]
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Value: {Value}", BuildValue());
                }

                private static string BuildValue() => "value";
            }
            """;

        RewriteProjectResult result = Rewrite(source);

        Assert.Equal(0, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(WithLineMapping(source), Assert.Single(result.Documents).Text);
    }

    [Fact]
    public void Rewrite_MethodAttributeOverridesDisabledDefault()
    {
        const string source = """
            using LoggingWeaving;
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                [LoggingGuard(LoggingGuardMode.Enabled)]
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Value: {Value}", BuildValue());
                }

                private static string BuildValue() => "value";
            }
            """;

        RewriteProjectResult result = Rewrite(source, defaultEnabled: false);

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Rewrite_LeavesNamedStaticCallWithLateLoggerUnchanged()
    {
        const string source = """
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                public void Run(ILogger logger)
                {
                    LoggerExtensions.LogInformation(
                        message: CreateMessage(),
                        logger: logger,
                        args: []);
                }

                private static string CreateMessage() => "message";
            }
            """;

        RewriteProjectResult result = Rewrite(source);

        Assert.Equal(0, result.RewrittenCallCount);
        Assert.Equal("LW0001", Assert.Single(result.Diagnostics).Id);
        Assert.Equal(WithLineMapping(source), Assert.Single(result.Documents).Text);
    }

    [Fact]
    public void Rewrite_NearestAttributeWins()
    {
        const string source = """
            using LoggingWeaving;
            using Microsoft.Extensions.Logging;

            [LoggingGuard(LoggingGuardMode.Disabled)]
            internal sealed class Worker
            {
                [LoggingGuard(LoggingGuardMode.Enabled)]
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Message");
                }
            }
            """;

        RewriteProjectResult result = Rewrite(source);

        Assert.Equal(1, result.RewrittenCallCount);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("Worker.cs", false, false)]
    [InlineData(@"C:\Source Files\Worker.cs", false, false)]
    [InlineData(@"C:\Source Files\Worker.cs", true, false)]
    [InlineData("Worker.cs", false, true)]
    public void Rewrite_MapsBreakpointsToAuthoredSource(string path, bool multiline, bool sameLine)
    {
        string source = """
            using Microsoft.Extensions.Logging;

            internal sealed class Worker
            {
                public void Run(ILogger logger)
                {
                    logger.LogInformation("Message");
                    int value = 1;
                }
            }
            """;

        source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (multiline)
        {
            source = source.Replace("logger.LogInformation(\"Message\");",
                "logger.LogInformation(\n            \"Message\");", StringComparison.Ordinal);
        }
        if (sameLine)
        {
            source = source.Replace(";\n        int value", "; int value", StringComparison.Ordinal);
        }
        SyntaxNode originalRoot = CSharpSyntaxTree.ParseText(source).GetRoot();
        FileLinePositionSpan originalCall = originalRoot.DescendantNodes()
            .OfType<ExpressionStatementSyntax>().Single().GetLocation().GetLineSpan();
        FileLinePositionSpan originalDeclaration = originalRoot.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>().Single().GetLocation().GetLineSpan();
        int nextLine = originalDeclaration.StartLinePosition.Line + 1;

        string rewritten = Assert.Single(Rewrite(source, path: path).Documents).Text;
        // Newer SDK compilers require an explicit offset to expand the brace's span.
        Assert.Contains($") 1 \"{path}\"", rewritten);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            rewritten, path: "GeneratedWorker.cs", encoding: Encoding.UTF8);
        SyntaxNode root = syntaxTree.GetRoot();
        MethodDeclarationSyntax method = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        InvocationExpressionSyntax loggingCall = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single(invocation => invocation.ToString().Contains("LogInformation", StringComparison.Ordinal));
        LocalDeclarationStatementSyntax authoredDeclaration = root.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>()
            .Single(declaration => declaration.Declaration.Variables.Any(variable => variable.Identifier.ValueText == "value"));

        AssertMappedLocation(method.Identifier.GetLocation(), path, 5);
        Assert.Equal("GeneratedWorker.cs", loggingCall.GetLocation().GetMappedLineSpan().Path);
        AssertMappedLocation(authoredDeclaration.GetLocation(), path, nextLine);
        Assert.Equal(originalDeclaration.StartLinePosition.Character,
            authoredDeclaration.GetLocation().GetMappedLineSpan().StartLinePosition.Character);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "BreakpointVerification",
            [syntaxTree],
            GetReferencePaths().Select(reference => MetadataReference.CreateFromFile(reference)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug));
        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        EmitResult result = compilation.Emit(assembly, pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        pdb.Position = 0;
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb);
        MetadataReader reader = provider.GetMetadataReader();
        SequencePoint[] points = reader.MethodDebugInformation
            .SelectMany(handle => reader.GetMethodDebugInformation(handle).GetSequencePoints())
            .Where(point => !point.IsHidden)
            .ToArray();
        SequencePoint[] authoredPoints = points.Where(point =>
            reader.GetString(reader.GetDocument(point.Document).Name) == path).ToArray();
        Assert.Contains(authoredPoints, point => point.StartLine == 6);
        Assert.Contains(authoredPoints, point => point.StartLine == nextLine);
        SequencePoint entry = Assert.Single(authoredPoints, point => point.StartLine == 7 &&
            point.StartColumn == originalCall.StartLinePosition.Character + 1);
        Assert.Equal(originalCall.StartLinePosition.Character + 1, entry.StartColumn);
        Assert.Equal(originalCall.EndLinePosition.Line + 1, entry.EndLine);
        Assert.Equal(originalCall.EndLinePosition.Character + 1, entry.EndColumn);

        int entryIndex = Array.FindIndex(points, point => point.Equals(entry));
        int nextIndex = Array.FindIndex(points, entryIndex + 1, point =>
            reader.GetString(reader.GetDocument(point.Document).Name) == path);
        Assert.True(nextIndex > entryIndex + 1);
        Assert.All(points[(entryIndex + 1)..nextIndex], point =>
            Assert.Equal("GeneratedWorker.cs", reader.GetString(reader.GetDocument(point.Document).Name)));
        Assert.Equal(nextLine, points[nextIndex].StartLine);
        Assert.Equal(originalDeclaration.StartLinePosition.Character + 1, points[nextIndex].StartColumn);
        Assert.Contains(points, point =>
            reader.GetString(reader.GetDocument(point.Document).Name) == "GeneratedWorker.cs" &&
            point.StartLine == loggingCall.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
    }

    private static RewriteProjectResult Rewrite(string source, bool defaultEnabled = true, string path = "Worker.cs")
    {
        var rewriter = new LoggingSourceRewriter();

        return rewriter.Rewrite(new RewriteProjectRequest(
            [new SourceDocument(path, source)],
            GetReferencePaths(),
            Options: new RewriteOptions(ProjectGuardEnabled: defaultEnabled)));
    }

    private static string WithLineMapping(string source) => $"#line 1 \"Worker.cs\"\n{source}";

    private static void AssertMappedLocation(Location location, string path, int line)
    {
        FileLinePositionSpan mappedSpan = location.GetMappedLineSpan();

        Assert.Equal(path, mappedSpan.Path);
        Assert.Equal(line, mappedSpan.StartLinePosition.Line + 1);
    }

    private static IReadOnlyList<string> GetReferencePaths()
    {
        string[] trustedPlatformAssemblies = ((string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        return trustedPlatformAssemblies
            .Append(typeof(ILogger).Assembly.Location)
            .Append(typeof(LoggerExtensions).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AssertCompiles(
        string source,
        LanguageVersion languageVersion = LanguageVersion.CSharp14)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(languageVersion));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RewriteVerification",
            [syntaxTree],
            GetReferencePaths().Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Diagnostic[] errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(errors);
    }
}
