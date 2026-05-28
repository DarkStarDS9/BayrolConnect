using BayrolLib;
using FluentAssertions;
using FluentAssertions.Execution;

namespace BayrolLibTests;

public class MqttStalenessTests
{
    private static readonly TimeSpan WarnThreshold = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ReconnectThreshold = TimeSpan.FromMinutes(6);

    [Test]
    public void GivenAllTopicsFresh_WhenEvaluating_ThenNothingIsStale()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lastUpdate = new Dictionary<string, DateTimeOffset>
        {
            [MqttMapping.RedoxValue] = now - TimeSpan.FromSeconds(30),
            [MqttMapping.SaltProductionRate] = now - TimeSpan.FromSeconds(10)
        };

        // Act
        var (warn, reconnect) = BayrolMqttConnector.EvaluateStaleness(lastUpdate, now, WarnThreshold, ReconnectThreshold);

        // Assert
        using (new AssertionScope())
        {
            warn.Should().BeEmpty();
            reconnect.Should().BeEmpty();
        }
    }

    [Test]
    public void GivenTopicPastWarnButNotReconnect_WhenEvaluating_ThenOnlyWarned()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lastUpdate = new Dictionary<string, DateTimeOffset>
        {
            [MqttMapping.RedoxValue] = now - TimeSpan.FromMinutes(4)
        };

        // Act
        var (warn, reconnect) = BayrolMqttConnector.EvaluateStaleness(lastUpdate, now, WarnThreshold, ReconnectThreshold);

        // Assert
        using (new AssertionScope())
        {
            warn.Should().ContainSingle(kv => kv.Key == MqttMapping.RedoxValue);
            reconnect.Should().BeEmpty();
        }
    }

    [Test]
    public void GivenTopicPastReconnect_WhenEvaluating_ThenReconnectAndNotDoubleCountedAsWarn()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lastUpdate = new Dictionary<string, DateTimeOffset>
        {
            [MqttMapping.SaltProductionRate] = now - TimeSpan.FromMinutes(7)
        };

        // Act
        var (warn, reconnect) = BayrolMqttConnector.EvaluateStaleness(lastUpdate, now, WarnThreshold, ReconnectThreshold);

        // Assert
        using (new AssertionScope())
        {
            reconnect.Should().ContainSingle(kv => kv.Key == MqttMapping.SaltProductionRate);
            warn.Should().BeEmpty();
        }
    }

    [Test]
    public void GivenMixedTopics_WhenEvaluating_ThenEachClassifiedIndependently()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var lastUpdate = new Dictionary<string, DateTimeOffset>
        {
            [MqttMapping.PhValue] = now - TimeSpan.FromSeconds(15),       // fresh
            [MqttMapping.RedoxValue] = now - TimeSpan.FromMinutes(4),     // warn
            [MqttMapping.SaltProductionRate] = now - TimeSpan.FromMinutes(8) // reconnect
        };

        // Act
        var (warn, reconnect) = BayrolMqttConnector.EvaluateStaleness(lastUpdate, now, WarnThreshold, ReconnectThreshold);

        // Assert
        using (new AssertionScope())
        {
            warn.Should().ContainSingle(kv => kv.Key == MqttMapping.RedoxValue);
            reconnect.Should().ContainSingle(kv => kv.Key == MqttMapping.SaltProductionRate);
        }
    }
}
