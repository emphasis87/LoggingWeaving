param(
    [string] $Configuration = 'Release',
    [string] $NewerOnlyDotNetPath,
    [string] $UnavailableDotNetPath
)

$repo = Split-Path -Parent $PSScriptRoot
$hostDll = Join-Path $repo "src/LoggingWeaving.BuildHost/bin/$Configuration/net8.0/LoggingWeaving.BuildHost.dll"
$project = Join-Path $repo 'samples/LoggingWeaving.StandardSample/LoggingWeaving.StandardSample.csproj'
if (-not (Test-Path -LiteralPath $hostDll)) {
    throw 'Build the solution before running the runtime checks.'
}
foreach ($dotnetPath in @($NewerOnlyDotNetPath, $UnavailableDotNetPath)) {
    if ($dotnetPath -and -not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
        throw "Explicit isolated dotnet executable does not exist: $dotnetPath"
    }
}

$output = & dotnet $hostDll --check-runtime
if ($LASTEXITCODE -ne 0 -or "$output" -notmatch 'LoggingWeaving build host runtime: .NET') {
    throw "BuildHost startup probe failed: $output"
}

$previousRollForward = $env:DOTNET_ROLL_FORWARD
try {
    $env:DOTNET_ROLL_FORWARD = 'LatestMajor'
    $output = & dotnet $hostDll --check-runtime
    if ($LASTEXITCODE -ne 0 -or "$output" -notmatch 'LoggingWeaving build host runtime: .NET (\d+)\.') {
        throw "BuildHost roll-forward probe failed: $output"
    }
    if ([int]$Matches[1] -lt 10) {
        throw "Expected roll-forward to .NET 10 or newer in the test environment: $output"
    }
}
finally {
    $env:DOTNET_ROLL_FORWARD = $previousRollForward
}

$previousMultilevelLookup = $env:DOTNET_MULTILEVEL_LOOKUP
try {
    # Older muxers must not fall back to the machine-wide runtime installation.
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    if ($NewerOnlyDotNetPath) {
        & dotnet msbuild $project -nologo -v:minimal -t:VerifyLoggingWeavingBuildHost `
            "-p:Configuration=$Configuration" "-p:LoggingWeavingDotnetPath=$NewerOnlyDotNetPath"
        if ($LASTEXITCODE -ne 0) {
            throw 'BuildHost preflight failed with the newer-only dotnet installation.'
        }
    }

    if (-not $UnavailableDotNetPath) {
        $UnavailableDotNetPath = Join-Path $repo "missing-dotnet-$([Guid]::NewGuid().ToString('N'))"
    }
    $previousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
    try {
        # GitHub's PowerShell 7 runner treats nonzero native exit codes as
        # terminating errors. This invocation is expected to fail with LW0002.
        $PSNativeCommandUseErrorActionPreference = $false
        $output = & dotnet msbuild $project -nologo -v:minimal -t:VerifyLoggingWeavingBuildHost `
            "-p:Configuration=$Configuration" "-p:LoggingWeavingDotnetPath=$UnavailableDotNetPath" 2>&1
        $expectedFailureExitCode = $LASTEXITCODE
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $previousNativeErrorPreference
    }
    if ($expectedFailureExitCode -eq 0 -or "$output" -notmatch 'LW0002') {
        throw "Expected actionable LW0002 failure, not a successful or silently unwoven build: $output"
    }
}
finally {
    $env:DOTNET_MULTILEVEL_LOOKUP = $previousMultilevelLookup
}

'Verified BuildHost startup, major roll-forward, and actionable startup failure.'
