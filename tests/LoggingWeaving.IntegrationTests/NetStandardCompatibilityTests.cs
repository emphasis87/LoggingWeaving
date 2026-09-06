extern alias Standard20;
extern alias Standard21;

using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Xunit;
using Fixture20 = Standard20::LoggingWeaving.NetStandardFixture.NetStandardLoggingCase;
using Fixture21 = Standard21::LoggingWeaving.NetStandardFixture.NetStandardLoggingCase;

namespace LoggingWeaving.IntegrationTests;

public sealed partial class WeavingBehaviorTests
{
    [Fact]
    public void NetStandardFixtures_LoadBothExactFrameworks()
    {
        var standard20 = typeof(Fixture20).Assembly;
        var standard21 = typeof(Fixture21).Assembly;

        Assert.NotSame(standard20, standard21);
        Assert.Equal(".NETStandard,Version=v2.0", standard20.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        Assert.Equal(".NETStandard,Version=v2.1", standard21.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    [Theory]
    [InlineData(false, null, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, null, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, null, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, null, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void NetStandardLogging_GuardsReceiverAndArguments(bool standard21, bool? enabled, bool generated)
    {
        Action<Func<ILogger?>, Func<string>> log = generated
            ? (standard21 ? Fixture21.LogGenerated : Fixture20.LogGenerated)
            : (standard21 ? Fixture21.LogExtension : Fixture20.LogExtension);

        AssertStandardCall(log, enabled, guarded: true, generated);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, null)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NetStandardMethodOptOut_LeavesCallUnwoven(bool standard21, bool? enabled)
    {
        AssertStandardCall(standard21 ? Fixture21.LogUnwoven : Fixture20.LogUnwoven, enabled, guarded: false);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, null)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NetStandardMethodOptIn_OverridesDisabledClass(bool standard21, bool? enabled)
    {
        AssertStandardCall(
            standard21 ? Fixture21.DisabledLogging.LogOptedIn : Fixture20.DisabledLogging.LogOptedIn,
            enabled,
            guarded: true);
    }

    private void AssertStandardCall(
        Action<Func<ILogger?>, Func<string>> log,
        bool? enabled,
        bool guarded,
        bool generated = false)
    {
        var logger = new RecordingLogger(enabled: enabled == true);
        ILogger? GetNullableLogger()
        {
            loggerEvaluationCount++;
            return enabled.HasValue ? logger : null;
        }

        if (!guarded && !enabled.HasValue)
        {
            Assert.Throws<ArgumentNullException>(() => log(GetNullableLogger, EvaluateArgument));
        }
        else
        {
            log(GetNullableLogger, EvaluateArgument);
        }

        Assert.Equal(1, loggerEvaluationCount);
        Assert.Equal(!guarded || enabled == true ? 1 : 0, evaluationCount);
        Assert.Equal(enabled.HasValue && (!guarded || enabled.Value) ? 1 : 0, logger.LogCount);
        Assert.Equal(guarded && enabled.HasValue ? (generated && enabled.Value ? 2 : 1) : 0, logger.IsEnabledCount);
    }
}
