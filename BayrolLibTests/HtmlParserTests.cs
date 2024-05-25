using BayrolLib;
using FluentAssertions;
using FluentAssertions.Execution;

namespace BayrolLibTests;

public class HtmlParserTests
{
    const string TestDataDirectory = "TestData";
    const string TestDataSaysSo = "this value is in the test data file";
    
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void GivenHtmlWithOkDeviceData_WhenParsing_ReturnedDeviceDataShouldContainExpectedValues()
    {
        // Arrange
        var html = File.ReadAllText(Path.Combine(TestDataDirectory, "AutomaticSaltOk.html"));
        
        // Act
        var result = HtmlParser.ParseDeviceData(html);
        
        // Assert
        using (new AssertionScope())
        {
            result.Ph.Should().Be(7.2m, TestDataSaysSo);
            result.Redox.Should().Be(676, TestDataSaysSo);
            result.Temperature.Should().Be(24.8m, TestDataSaysSo);
            result.Salt.Should().Be(1.7m, TestDataSaysSo);
            result.DeviceState.Should().Be(DeviceState.Ok, TestDataSaysSo);
            result.ErrorMessage.Should().BeNullOrEmpty(TestDataSaysSo);
            result.ObtainedAt.Should().Be(DateTimeOffset.MinValue, "the date is not part of the test data");
        }
    }

    [Test]
    public void GivenHtmlWithWarningDeviceData_WhenParsing_ReturnedDeviceDataShouldContainExpectedValues()
    {
        // Arrange
        var html = File.ReadAllText(Path.Combine(TestDataDirectory, "AutomaticSaltWarning.html"));
        
        // Act
        var result = HtmlParser.ParseDeviceData(html);
        
        // Assert
        using (new AssertionScope())
        {
            result.Ph.Should().Be(7.1m, TestDataSaysSo);
            result.Redox.Should().Be(705, TestDataSaysSo);
            result.Temperature.Should().Be(25.0m, TestDataSaysSo);
            result.Salt.Should().Be(2.5m, TestDataSaysSo);
            result.DeviceState.Should().Be(DeviceState.Warning, TestDataSaysSo);
            result.ErrorMessage.Should().BeNullOrEmpty(TestDataSaysSo);
            result.ObtainedAt.Should().Be(DateTimeOffset.MinValue, "the date is not part of the test data");
        }
    }

    [Test]
    public void GivenHtmlWithErrorDeviceData_WhenParsing_ReturnedDeviceDataShouldContainExpectedValues()
    {
        // Arrange
        var html = File.ReadAllText(Path.Combine(TestDataDirectory, "AutomaticSaltError.html"));
        
        // Act
        var result = HtmlParser.ParseDeviceData(html);
        
        // Assert
        using (new AssertionScope())
        {
            result.DeviceState.Should().Be(DeviceState.Error, TestDataSaysSo);
            result.ErrorMessage.Should().Be(@"No connection to the controller since 22.05.24, 17:27 UTC", TestDataSaysSo);
            result.ObtainedAt.Should().Be(DateTimeOffset.MinValue, "the date is not part of the test data");
        }
    }
    
    [Test]
    public void GivenHtmlWithAPlantList_WhenGettingTheCodeForADevice_ThenTheCorrectCodeShouldBeReturned()
    {
        // Arrange
        var html = File.ReadAllText(Path.Combine(TestDataDirectory, "AutomaticSaltDevice.html"));
        
        // Act
        var result = HtmlParser.GetCode(html);
        
        // Assert
        result.Should().Be("A-CODE1", TestDataSaysSo);
    }
}