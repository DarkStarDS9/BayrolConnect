using BayrolLib;
using FluentAssertions;

namespace BayrolLibTests;

public class MqttMappingTests
{
    [Test]
    public void GivenTheStaticClass_WhenAccessingAllTopics_ThenAllTopicsAreReturned()
    {
        // Act
        var topics = MqttMapping.AllTopics;

        // Assert
        topics.Should().NotBeEmpty();
        
        // Check a few of the topics
        topics.Should().Contain(MqttMapping.RedoxValue);
        topics.Should().Contain(MqttMapping.CanisterState);
    }
}