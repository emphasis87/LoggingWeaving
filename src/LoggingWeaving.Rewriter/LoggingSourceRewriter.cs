using LoggingWeaving.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace LoggingWeaving.Rewriter;

public sealed class LoggingSourceRewriter : ILoggingSourceRewriter
{
    public RewriteProjectResult Rewrite(
        RewriteProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Sources.Count == 0)
        {
            return new RewriteProjectResult([], [], 0);
        }

        RewriteOptions options = request.Options ?? new RewriteOptions();

        if (!LanguageVersionFacts.TryParse(options.LanguageVersion, out LanguageVersion languageVersion))
        {
            throw new ArgumentException(
                $"Unsupported C# language version '{options.LanguageVersion}'.",
                nameof(request));
        }

        CSharpParseOptions parseOptions = new(
            languageVersion,
            preprocessorSymbols: request.PreprocessorSymbols ?? []);

        SyntaxTree[] sourceTrees = request.Sources
            .Select(source => CSharpSyntaxTree.ParseText(
                source.Text,
                parseOptions,
                source.Path,
                cancellationToken: cancellationToken))
            .ToArray();

        SyntaxTree attributeTree = CSharpSyntaxTree.ParseText(
            GeneratedAttributeSource.Text,
            parseOptions,
            "LoggingGuardAttribute.analysis.g.cs",
            cancellationToken: cancellationToken);

        MetadataReference[] references = request.ReferencePaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            request.AssemblyName,
            sourceTrees.Append(attributeTree),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<RewrittenDocument> documents = [];
        List<RewriteDiagnostic> diagnostics = [];
        int rewrittenCallCount = 0;

        foreach (SyntaxTree syntaxTree in sourceTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            var callRewriter = new LoggingCallSyntaxRewriter(
                semanticModel,
                diagnostics,
                options.DefaultEnabled);
            SyntaxNode originalRoot = syntaxTree.GetRoot(cancellationToken);
            SyntaxNode rewrittenRoot = callRewriter.Visit(originalRoot) ?? originalRoot;
            string rewrittenText = rewrittenRoot.ToFullString();
            string mappedText = $"#line 1 \"{EscapeLineDirectivePath(syntaxTree.FilePath)}\"\n{rewrittenText}";

            documents.Add(new RewrittenDocument(syntaxTree.FilePath, mappedText));
            rewrittenCallCount += callRewriter.RewrittenCallCount;
        }

        return new RewriteProjectResult(documents, diagnostics, rewrittenCallCount);
    }

    private static string EscapeLineDirectivePath(string path) =>
        path.Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class LoggingCallSyntaxRewriter(
        SemanticModel semanticModel,
        List<RewriteDiagnostic> diagnostics,
        bool defaultEnabled) : CSharpSyntaxRewriter
    {
        private const string LoggerExtensionsType =
            "Microsoft.Extensions.Logging.LoggerExtensions";
        private const string LoggerInterfaceType =
            "Microsoft.Extensions.Logging.ILogger";
        private const string LoggerMessageAttributeType =
            "Microsoft.Extensions.Logging.LoggerMessageAttribute";
        private const string LoggingGuardAttributeType =
            "LoggingWeaving.LoggingGuardAttribute";

        private static readonly IReadOnlyDictionary<string, string> LevelsByMethod =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["LogTrace"] = "Trace",
                ["LogDebug"] = "Debug",
                ["LogInformation"] = "Information",
                ["LogWarning"] = "Warning",
                ["LogError"] = "Error",
                ["LogCritical"] = "Critical"
            };

        private static readonly string[] LevelsByValue =
        [
            "Trace",
            "Debug",
            "Information",
            "Warning",
            "Error",
            "Critical",
            "None"
        ];

        private readonly HashSet<string> identifiers = semanticModel.SyntaxTree
            .GetRoot()
            .DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        public int RewrittenCallCount { get; private set; }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (!IsWeavingEnabled(node))
            {
                return base.VisitExpressionStatement(node);
            }

            if (node.Expression is not InvocationExpressionSyntax invocation)
            {
                ReportNestedUnsupportedLoggingCall(node);
                return base.VisitExpressionStatement(node);
            }

            if (!TryGetLoggingCall(invocation, out LoggingCall call))
            {
                return base.VisitExpressionStatement(node);
            }

            if (node.ContainsDirectives)
            {
                AddUnsupportedDiagnostic(node, call.MethodName, "preprocessor directives");
                return base.VisitExpressionStatement(node);
            }

            string temporaryName = CreateTemporaryName(node);
            FileLinePositionSpan lineSpan = node.GetLocation().GetLineSpan();
            int sourceLine = lineSpan.StartLinePosition.Line + 1;
            int endSourceLine = lineSpan.EndLinePosition.Line + 1;
            int sourceColumn = lineSpan.StartLinePosition.Character + 1;
            int endSourceColumn = lineSpan.EndLinePosition.Character;
            string sourcePath = EscapeLineDirectivePath(lineSpan.Path);
            InvocationExpressionSyntax guardedInvocation = invocation.ReplaceNode(
                call.LoggerExpression,
                SyntaxFactory.IdentifierName(temporaryName));

            string guardedSource = $$"""
                {
                    #line default
                    {{call.TemporaryType}} {{temporaryName}} = {{call.LoggerExpression.WithoutTrivia()}};
                    if ({{temporaryName}} is not null)
                    {
                        if ({{temporaryName}}.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.{{call.Level}}))
                        {
                            {{guardedInvocation.WithoutTrivia()}};
                        }
                    }
                }
                """;

            string indentation = GetIndentation(node);

            if (indentation.Length > 0)
            {
                guardedSource = guardedSource.Replace(
                    "\n",
                    "\n" + indentation,
                    StringComparison.Ordinal);
            }

            // Only the block entry maps to the authored call. Keep the complete
            // body in generated source so stepping into and out of calls stays there.
            // The offset makes the brace use the full mapped span, not one character.
            SyntaxTriviaList leadingTrivia = node.GetLeadingTrivia().AddRange(
                SyntaxFactory.ParseLeadingTrivia(
                    $"\n#line ({sourceLine}, {sourceColumn}) - ({endSourceLine}, {endSourceColumn}) 1 \"{sourcePath}\"\n"));
            SyntaxTriviaList trailingTrivia = SyntaxFactory.ParseLeadingTrivia(
                    $"\n#line {endSourceLine} \"{sourcePath}\"\n{new string(' ', endSourceColumn)}")
                .AddRange(node.GetTrailingTrivia());
            StatementSyntax guardedStatement = SyntaxFactory.ParseStatement(guardedSource)
                .WithLeadingTrivia(leadingTrivia)
                .WithTrailingTrivia(trailingTrivia);

            RewrittenCallCount++;
            return guardedStatement;
        }

        private static string GetIndentation(SyntaxNode node)
        {
            string leadingTrivia = node.GetLeadingTrivia().ToFullString();
            int lineStart = Math.Max(
                leadingTrivia.LastIndexOf('\n'),
                leadingTrivia.LastIndexOf('\r')) + 1;
            string currentLine = leadingTrivia[lineStart..];

            return new string(currentLine.TakeWhile(character => character is ' ' or '\t').ToArray());
        }

        private bool TryGetLoggingCall(
            InvocationExpressionSyntax invocation,
            out LoggingCall call)
        {
            call = null!;

            if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            {
                return false;
            }

            IMethodSymbol definition = method.ReducedFrom ?? method;

            if (definition.ContainingType.ToDisplayString() == LoggerExtensionsType &&
                LevelsByMethod.TryGetValue(definition.Name, out string? extensionLevel))
            {
                ExpressionSyntax? loggerExpression = method.ReducedFrom is not null
                    ? (invocation.Expression as MemberAccessExpressionSyntax)?.Expression
                    : GetLoggerArgument(invocation);

                if (loggerExpression is null)
                {
                    AddUnsupportedDiagnostic(invocation, definition.Name, "logger argument");
                    return false;
                }

                if (method.ReducedFrom is null && !IsFirstArgument(invocation, loggerExpression))
                {
                    AddUnsupportedDiagnostic(invocation, definition.Name, "the logger as the first evaluated argument");
                    return false;
                }

                call = new LoggingCall(
                    definition.Name,
                    extensionLevel,
                    loggerExpression,
                    "global::Microsoft.Extensions.Logging.ILogger?");
                return true;
            }

            AttributeData? loggerMessageAttribute = GetLoggerMessageAttribute(method);

            if (loggerMessageAttribute is null)
            {
                return false;
            }

            string? generatedLevel = GetLoggerMessageLevel(loggerMessageAttribute);
            IMethodSymbol generatedDefinition = method.ReducedFrom ?? method;
            IParameterSymbol? generatedLoggerParameter = generatedDefinition.Parameters
                .FirstOrDefault(parameter => IsLoggerType(parameter.Type));
            ExpressionSyntax? generatedLoggerExpression = method.ReducedFrom is not null &&
                generatedLoggerParameter?.Ordinal == 0
                    ? (invocation.Expression as MemberAccessExpressionSyntax)?.Expression
                    : GetLoggerArgument(invocation);

            if (generatedLevel is null || generatedLevel == "None" || generatedLoggerExpression is null)
            {
                AddUnsupportedDiagnostic(
                    invocation,
                    method.Name,
                    generatedLevel is null || generatedLevel == "None"
                        ? "a fixed LoggerMessage level"
                        : "an ILogger argument");
                return false;
            }

            if (method.ReducedFrom is null && !IsFirstArgument(invocation, generatedLoggerExpression))
            {
                AddUnsupportedDiagnostic(invocation, method.Name, "the logger as the first evaluated argument");
                return false;
            }

            string temporaryType = generatedLoggerParameter!.Type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
            call = new LoggingCall(
                method.Name,
                generatedLevel,
                generatedLoggerExpression,
                temporaryType);
            return true;
        }

        private static bool IsFirstArgument(
            InvocationExpressionSyntax invocation,
            ExpressionSyntax loggerExpression)
        {
            ArgumentSyntax? loggerArgument = loggerExpression.Parent as ArgumentSyntax;
            return loggerArgument is not null && invocation.ArgumentList.Arguments.FirstOrDefault() == loggerArgument;
        }

        private ExpressionSyntax? GetLoggerArgument(InvocationExpressionSyntax invocation)
        {
            if (semanticModel.GetOperation(invocation) is not IInvocationOperation operation)
            {
                return null;
            }

            IArgumentOperation? loggerArgument = operation.Arguments.FirstOrDefault(argument =>
                argument.Parameter is not null && IsLoggerType(argument.Parameter.Type));

            ArgumentSyntax? syntax = loggerArgument?.Syntax.FirstAncestorOrSelf<ArgumentSyntax>();
            return syntax?.Parent == invocation.ArgumentList ? syntax.Expression : null;
        }

        private bool IsLoggerType(ITypeSymbol type)
        {
            INamedTypeSymbol? loggerType = semanticModel.Compilation.GetTypeByMetadataName(LoggerInterfaceType);
            return loggerType is not null &&
                (SymbolEqualityComparer.Default.Equals(type, loggerType) ||
                 type.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, loggerType)));
        }

        private static AttributeData? GetLoggerMessageAttribute(IMethodSymbol method)
        {
            IEnumerable<IMethodSymbol?> candidates =
            [
                method,
                method.ReducedFrom,
                method.OriginalDefinition,
                method.PartialDefinitionPart,
                method.PartialImplementationPart
            ];

            return candidates
                .Where(candidate => candidate is not null)
                .SelectMany(candidate => candidate!.GetAttributes())
                .FirstOrDefault(attribute =>
                    attribute.AttributeClass?.ToDisplayString() == LoggerMessageAttributeType);
        }

        private static string? GetLoggerMessageLevel(AttributeData attribute)
        {
            TypedConstant? level = attribute.NamedArguments
                .Where(argument => argument.Key == "Level")
                .Select(argument => (TypedConstant?)argument.Value)
                .FirstOrDefault();

            if (level is null && attribute.AttributeConstructor is not null)
            {
                for (int index = 0; index < attribute.AttributeConstructor.Parameters.Length; index++)
                {
                    if (attribute.AttributeConstructor.Parameters[index].Name == "level")
                    {
                        level = attribute.ConstructorArguments[index];
                        break;
                    }
                }
            }

            if (level?.Value is not int levelValue ||
                levelValue < 0 ||
                levelValue >= LevelsByValue.Length)
            {
                return null;
            }

            return LevelsByValue[levelValue];
        }

        private bool IsWeavingEnabled(SyntaxNode node)
        {
            ISymbol? symbol = semanticModel.GetEnclosingSymbol(node.SpanStart);

            while (symbol is not null)
            {
                if (symbol is IMethodSymbol or INamedTypeSymbol)
                {
                    bool? configured = GetConfiguredMode(symbol);

                    if (configured.HasValue)
                    {
                        return configured.Value;
                    }
                }

                symbol = symbol.ContainingSymbol;
            }

            return GetConfiguredMode(semanticModel.Compilation.Assembly) ?? defaultEnabled;
        }

        private static bool? GetConfiguredMode(ISymbol symbol)
        {
            AttributeData? attribute = symbol.GetAttributes().FirstOrDefault(candidate =>
                candidate.AttributeClass?.ToDisplayString() == LoggingGuardAttributeType);

            if (attribute?.ConstructorArguments is not [{ Value: int mode }])
            {
                return null;
            }

            return mode switch
            {
                0 => false,
                1 => true,
                _ => null
            };
        }

        private string CreateTemporaryName(SyntaxNode node)
        {
            string baseName = $"__loggingWeavingLogger_{node.SpanStart}";
            string name = baseName;
            int suffix = 1;

            while (semanticModel.LookupSymbols(node.SpanStart, name: name).Length > 0)
            {
                name = $"{baseName}_{suffix++}";
            }

            while (identifiers.Contains(name))
            {
                name = $"{baseName}_{suffix++}";
            }

            return name;
        }

        private void ReportNestedUnsupportedLoggingCall(ExpressionStatementSyntax node)
        {
            InvocationExpressionSyntax? invocation = node
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault(candidate => TryGetLoggingCall(candidate, out _));

            if (invocation is not null)
            {
                string methodName =
                    (semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol)?.Name ?? "Log";
                AddUnsupportedDiagnostic(invocation, methodName, "a non-standalone expression");
            }
        }

        private void AddUnsupportedDiagnostic(
            SyntaxNode node,
            string methodName,
            string unsupportedFeature)
        {
            FileLinePositionSpan span = node.SyntaxTree.GetLineSpan(node.Span);

            diagnostics.Add(new RewriteDiagnostic(
                "LW0001",
                $"The logging call '{methodName}' requires {unsupportedFeature}, which is not supported.",
                node.SyntaxTree.FilePath,
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1));
        }

        private sealed record LoggingCall(
            string MethodName,
            string Level,
            ExpressionSyntax LoggerExpression,
            string TemporaryType);
    }
}
