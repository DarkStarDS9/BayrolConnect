using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace BayrolLib;

/// <summary>
/// This class is used to parse HTML content from bayrol-poolaccess.de
/// </summary>
public static partial class HtmlParser
{
    // <iframe src="../../app/index.html?code=A-CODE1&direct" name="device" frameborder="0" scrolling="no"></iframe>
    [GeneratedRegex(@"index\.html\?code=(?<code>[^&]+)&")]
    private static partial Regex CodeExtractor();
    
    public static AutomaticSaltDeviceData ParseDeviceData(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var container = doc.DocumentNode.SelectSingleNode("//div[@class='tab_data_link']");
        var status = container.SelectSingleNode("//div[@class='gstat_ok' or @class='gstat_warning']");

        if (status != null)
        {
            var ph = doc.DocumentNode.SelectSingleNode("//div[span[contains(text(), 'pH')]]/h1").InnerText;
            var redox = doc.DocumentNode.SelectSingleNode("//div[span[contains(text(), 'Redox')]]/h1").InnerText;
            var temperature = doc.DocumentNode.SelectSingleNode("//div[span[contains(text(), 'Temp')]]/h1").InnerText;
            var salt = doc.DocumentNode.SelectSingleNode("//div[span[contains(text(), 'Salt')]]/h1").InnerText;

            var result = new AutomaticSaltDeviceData
            {
                Ph = decimal.Parse(ph, CultureInfo.InvariantCulture),
                Redox = int.Parse(redox, CultureInfo.InvariantCulture),
                Temperature = decimal.Parse(temperature, CultureInfo.InvariantCulture),
                Salt = decimal.Parse(salt, CultureInfo.InvariantCulture),
                DeviceState = status.GetAttributeValue("class", "") == "gstat_ok" ? DeviceState.Ok : DeviceState.Warning 
            };

            return result;
        }

        if (container.SelectSingleNode("//div[@class='gstat_error']") != null)
        {
            var error = container.SelectSingleNode("//div[@class='tab_error']")?.ChildNodes.FirstOrDefault()?.InnerText;

            var result = new AutomaticSaltDeviceData
            {
                DeviceState = DeviceState.Error,
                ErrorMessage = error
            };

            return result;
        }

        throw new ArgumentOutOfRangeException(nameof(html), "expected gstat_ok or gstat_error class in HTML content");
    }
    
    public static string GetCode(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var iframe = doc.DocumentNode.SelectSingleNode("//iframe");
        
        if(iframe == null) throw new Exception("iframe not found");

        var match = CodeExtractor().Match(iframe.GetAttributeValue("src", ""));
        
        if(!match.Success) throw new Exception("Failed to extract code from iframe src");
        
        return match.Groups["code"].Value;
    }
}