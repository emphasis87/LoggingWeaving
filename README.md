# LoggingWeaving

LoggingWeaving is an experimental build-time source rewriter for
`Microsoft.Extensions.Logging`. It preserves ordinary structured message
templates while moving argument evaluation behind logger null and log-level
checks.

```csharp
logger.LogInformation(
    "Application {ApplicationName} finished",
    GetApplicationName());
```

The project compiles a generated equivalent under `obj`:

```csharp
{
    ILogger? temporaryLogger = logger;
    if (temporaryLogger is not null)
    {
        if (temporaryLogger.IsEnabled(LogLevel.Information))
        {
            temporaryLogger.LogInformation(
                "Application {ApplicationName} finished",
                GetApplicationName());
        }
    }
}
```

When information logging is disabled, `GetApplicationName()` is not evaluated.
The explicit template name `{ApplicationName}` remains available to Serilog,
Sentry, and other structured logging providers.

## Status

LoggingWeaving is an experimental `0.1.x` package. The current implementation
targets .NET 8 and C# 12 projects.

Supported calls:

- Standalone `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`,
  and `LogCritical` extension calls.
- Reduced calls such as `logger.LogInformation(...)`.
- Static calls such as `LoggerExtensions.LogInformation(logger, ...)`.
- Partial methods decorated with `LoggerMessageAttribute` when they have a fixed
  level and an `ILogger` argument.

Calls are matched semantically. Unrelated methods with the same names are not
rewritten. Unsupported recognized forms remain unchanged and produce `LW0001`.

## Installation

After publishing the package, add one private build dependency:

```xml
<PackageReference Include="LoggingWeaving" Version="0.1.0" PrivateAssets="all" />
```

Weaving is enabled by default. The package contributes an internal configuration
attribute through a source generator, so no runtime assembly is added.

Disable weaving for a method or type:

```csharp
using LoggingWeaving;

[LoggingGuard(LoggingGuardMode.Disabled)]
private void RunWithoutWeaving(ILogger logger)
{
    logger.LogInformation("Value: {Value}", CreateValue());
}
```

Enable weaving inside a project whose default is disabled:

```xml
<PropertyGroup>
  <LoggingWeavingDefaultEnabled>false</LoggingWeavingDefaultEnabled>
</PropertyGroup>
```

```csharp
[LoggingGuard(LoggingGuardMode.Enabled)]
private void RunWithWeaving(ILogger logger)
{
    logger.LogInformation("Value: {Value}", CreateValue());
}
```

Method configuration overrides containing-type configuration. The nearest
containing type overrides outer types, and assembly configuration overrides the
project default.

Disable the build integration completely with:

```xml
<PropertyGroup>
  <LoggingWeavingEnabled>false</LoggingWeavingEnabled>
</PropertyGroup>
```

## Debugging

The compiler receives rewritten copies from:

```text
obj/<Configuration>/<TargetFramework>/LoggingWeaving/
```

The rewritten copies contain `#line` mappings back to the authored files, so
breakpoints can be set directly in the original source. The entry of each
rewritten block maps to the full authored logging statement. Stepping then
continues in the generated source under `obj`, including receiver evaluation,
guards, the logging call, and closing braces. `F11` can enter called methods
normally, including the Microsoft `LoggerMessage` implementation. Mapping
returns to the authored file only after the rewritten block.

The authored source is never overwritten. Edit and Continue and Hot Reload are
not currently supported for rewritten methods.

## Build And Test

```powershell
dotnet test LoggingWeaving.slnx --configuration Release
dotnet pack src/LoggingWeaving.Package/LoggingWeaving.Package.csproj `
    --configuration Release `
    --output artifacts
dotnet run --project tests/LoggingWeaving.PackageConsumer/LoggingWeaving.PackageConsumer.csproj `
    --configuration Release
```

The test suite contains semantic rewriter tests, build-time runtime tests, and a
consumer that restores and executes the generated NuGet package.

Run the Serilog sample with:

```powershell
dotnet run --project samples/LoggingWeaving.Sample/LoggingWeaving.Sample.csproj
```

It writes events to `example.log` in the current working directory.

## Publishing

`LoggingWeaving.Package.csproj` creates `LoggingWeaving.<version>.nupkg`. The
GitHub CI workflow builds, tests, packs, and runs the packaged consumer on Linux.

The publish workflow runs when a GitHub release is published. Its tag must be a
NuGet-compatible version such as `v0.1.0`, and the repository must define the
`NUGET_API_KEY` Actions secret.

## Limitations

- Only C# 12 syntax is supported by the current rewriter host.
- Logging invocations must be representable as standalone statements.
- Conditional-access calls and dynamic log levels are not currently rewritten.
- Logger receiver evaluation is preserved exactly once.
- The null guard intentionally changes a null logger from an exception into a
  skipped log call.
- `LoggerMessage` methods retain their generated internal `IsEnabled` check, so
  enabled calls may check the level twice. Disabled calls never invoke the
  generated method or evaluate its arguments.
- Source files containing directives inside a rewritten statement are left
  unchanged with `LW0001`.
