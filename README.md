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

LoggingWeaving is an experimental `1.0.0-alpha1` package. Supported targets are
.NET 8, 9, and 10, plus .NET Standard 2.0 and 2.1 libraries.
The current rewriter uses C# 12.

Supported calls:

- Standalone `LogTrace`, `LogDebug`, `LogInformation`, `LogWarning`, `LogError`,
  and `LogCritical` extension calls.
- Reduced calls such as `logger.LogInformation(...)`.
- Static calls such as `LoggerExtensions.LogInformation(logger, ...)`.
- Partial methods decorated with `LoggerMessageAttribute` when they have a fixed
  level and an `ILogger` argument.

Calls are matched semantically. Unrelated methods with the same names are not
rewritten. Unsupported recognized forms remain unchanged and produce `LW0001`.

## Build Requirements

Consumer target frameworks and build-host runtimes are separate requirements:

- Applications and libraries may target `net8.0`, `net9.0`, `net10.0`, or
  `netstandard2.0` / `netstandard2.1`, with compatible logging dependencies.
- Build with a modern SDK/compiler (Roslyn 4.8 or newer) and explicitly set
  `<LangVersion>12.0</LangVersion>`. A newer target framework does not enable
  newer C# syntax in the rewriter.
- The out-of-process build host targets .NET 8 and uses `RollForward=Major`.
  It can run without .NET 8 when a compatible newer runtime is installed.
  Older runtimes cannot run the build host and are not supported consumer
  targets.
- Building this repository requires the .NET 10 SDK specified by `global.json`.
  Running the complete test suite additionally requires runtimes 8 and 9.

Before replacing compiler inputs, MSBuild starts the host with `--check-runtime`.
This uses the actual .NET host resolution rules rather than guessing from an
installed-version list. A startup failure stops the build with `LW0002` and
installation guidance; it never silently disables weaving. Install a supported
.NET runtime/SDK from https://dotnet.microsoft.com/download, or point
`LoggingWeavingDotNetPath` at a compatible `dotnet` executable. Check runtime
architecture and any `DOTNET_ROLL_FORWARD` override if startup still fails.

## Installation

After publishing the package, add one private build dependency:

```xml
<PackageReference Include="LoggingWeaving" Version="1.0.0-alpha1" PrivateAssets="all" />
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

The test suite contains semantic rewriter tests, build-time runtime tests for
both the standard Microsoft logging generator and the Telemetry generator,
and a consumer that restores and executes the generated NuGet package.
The same runtime behavior tests run against both generators. Telemetry tests
also cover nullable logger signatures with and without `SkipEnabledCheck`.
Both runtime suites target .NET 8 through 10. Two separate fixture assemblies
target .NET Standard 2.0 and 2.1; every runtime suite loads and exercises both
exact assemblies, rather than selecting just the nearest compatible target.
.NET Standard has no independent runtime to execute tests on.

After building the solution, run the build-host startup checks with:

```powershell
./tests/VerifyBuildHostRuntime.ps1
```

The script checks normal startup, roll-forward to the newest installed runtime
(the test environment includes .NET 10), and an actionable `LW0002` failure.
Optional `-NewerOnlyDotNetPath` and `-UnavailableDotNetPath` parameters exercise
isolated installations containing only a newer runtime or only older runtimes.
These paths are test inputs and are not embedded in the package.

Run the .NET 10 Serilog sample using `Microsoft.Extensions.Telemetry.Abstractions` with:

```powershell
dotnet run --project samples/LoggingWeaving.TelemetrySample/LoggingWeaving.TelemetrySample.csproj
```

It writes events to the console, debugger output, and `example.log` in the
current working directory. It demonstrates all four combinations of nullable
logger signatures and `SkipEnabledCheck`, including calls with a null logger.

Run the .NET 10 sample using the standard Microsoft logging generator with:

```powershell
dotnet run --project samples/LoggingWeaving.StandardSample/LoggingWeaving.StandardSample.csproj
```

This sample uses non-nullable `ILogger` signatures. Its null-logger calls use
`logger!`: the null-forgiving operator only affects compiler analysis; the
rewriter supplies the runtime null guard. Both samples keep `SkipEnabledCheck`
as authored. The rewriter targets the `ILogger` and `LoggerMessageAttribute`
contracts, not a particular generator implementation.

Run the attribute-control sample with:

```powershell
dotnet run --project samples/LoggingWeaving.AttributeSample/LoggingWeaving.AttributeSample.csproj
```

This sample uses a disabled logger and checks argument evaluation counts for
default weaving, method-level opt-out, type-level opt-out, and method-level
opt-in overriding its containing type. The expected counts are `0, 1, 1, 0`.

All three projects are included in the solution. In Visual Studio, set the
desired sample as the startup project and use the Debug configuration.
The LoggerMessage declarations are at the end of each logging sample's class
under `// LOGGING`.

## Publishing

`LoggingWeaving.Package.csproj` creates `LoggingWeaving.<version>.nupkg`. The
GitHub CI workflow builds, tests, packs, and runs the packaged consumer on Linux.

The publish workflow runs when a GitHub release is published. Its tag must be a
NuGet-compatible version such as `v1.0.0-alpha1`, and the repository must define the
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
