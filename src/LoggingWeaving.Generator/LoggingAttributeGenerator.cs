using LoggingWeaving.Internal;
using Microsoft.CodeAnalysis;

namespace LoggingWeaving.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class LoggingAttributeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(output =>
            output.AddSource("LoggingGuardAttribute.g.cs", GeneratedAttributeSource.Text));
    }
}
