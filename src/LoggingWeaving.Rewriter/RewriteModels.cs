namespace LoggingWeaving.Rewriter;

public sealed record SourceDocument(string Path, string Text);

public sealed record RewriteProjectRequest(
    IReadOnlyList<SourceDocument> Sources,
    IReadOnlyList<string> ReferencePaths,
    string AssemblyName = "LoggingWeaving.RewriteTarget",
    IReadOnlyList<string>? PreprocessorSymbols = null,
    RewriteOptions? Options = null);

public sealed record RewriteOptions(
    bool DefaultEnabled = true,
    string LanguageVersion = "14.0");

public sealed record RewrittenDocument(string Path, string Text);

public sealed record RewriteDiagnostic(
    string Id,
    string Message,
    string Path,
    int Line,
    int Column);

public sealed record RewriteProjectResult(
    IReadOnlyList<RewrittenDocument> Documents,
    IReadOnlyList<RewriteDiagnostic> Diagnostics,
    int RewrittenCallCount);
