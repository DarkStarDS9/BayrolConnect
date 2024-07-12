using BayrolConnect;
using FluentAssertions;

namespace BayrolConnectTests;

public class ProgramTests
{
    public static object[] RedoxTargetValues =
    [
        new object[]
        {
            720,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(7, 0, 0),
            default(int?)
        },
        new object[]
        {
            720,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(7, 30, 0),
            640
        },
        new object[]
        {
            720,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(22, 00, 0),
            default(int?)
        },
        new object[]
        {
            640,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(22, 00, 0),
            720
        },
        new object[]
        {
            640,
            new List<KeyValuePair<TimeSpan, int>>
            {
            },
            new TimeSpan(22, 00, 0),
            default(int?)
        },
        new object[]
        {
            640,
            default(List<KeyValuePair<TimeSpan, int>>)!,
            new TimeSpan(22, 00, 0),
            default(int?)
        },
        new object[]
        {
            640,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(14, 0, 0), 650),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(15, 00, 0),
            650
        },
        new object[]
        {
            720,
            new List<KeyValuePair<TimeSpan, int>>
            {
                new(new TimeSpan(7, 30, 0), 640),
                new(new TimeSpan(14, 0, 0), 650),
                new(new TimeSpan(18, 0, 0), 720)
            },
            new TimeSpan(12, 00, 0),
            640
        },
    ];
    
    [SetUp]
    public void Setup()
    {
    }

    [Theory]
    [TestCaseSource(nameof(RedoxTargetValues))]
    public void GivenRedoxTargetValues_WhenGetNewRedoxTargetIsCalled_ThenExpectedValueIsReturned(int currentTargetValue, List<KeyValuePair<TimeSpan, int>>? sortedTargetValues, TimeSpan currentTime, int? expectedValue)
    {
        // Arrange
        var timeProvider = new FakeTimeProvider { FakeUtcNow = DateTimeOffset.UtcNow.Date.Add(currentTime) };
        
        // Act
        var result = Program.GetNewRedoxTarget(sortedTargetValues, currentTargetValue, timeProvider);
        
        // Assert
        result.Should().Be(expectedValue);
    }
}