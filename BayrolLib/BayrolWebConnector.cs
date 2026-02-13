using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace BayrolLib;

public class BayrolWebConnector
{
    private const string BaseUrl = "https://www.bayrol-poolaccess.de";
    private const string BasePath = "/webview/p";
    private const string LoginUri = "/login.php?r=reg";
    private const string DataPath = "/webview/getdata.php?cid=";
    private const string DevicePath = "/device.php?c=";
    private const string GetAccessTokenPath = "/api/?code=";
    private const int MaxRetryAttempts = 10;
    private const int RetryDelayMinutes = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string _username;
    private readonly string _password;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;

    private bool _loginSuccess;
    
    public BayrolWebConnector(string username, string password, ILogger logger, TimeProvider timeProvider)
    {
        _username = username;
        _password = password;
        _logger = logger;
        _timeProvider = timeProvider;

        _httpClient = new HttpClient(new HttpClientHandler { UseCookies = true, AllowAutoRedirect = false});
    }

    private void PrintHeaders(HttpResponseMessage response)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            foreach (var header in response.Headers)
            {
                _logger.LogTrace("{HeaderKey} : {HeaderValue}", header.Key, string.Join(", ", header.Value));
            }
        }
    }

    private async Task ReconnectAfterFailureAsync()
    {
        int retryCount = 0;
        do
        {
            try
            {
                _loginSuccess = await LoginAsync();

                if (!_loginSuccess) throw new Exception("Login failed");
            }
            catch (Exception e)
            {
                retryCount++;
                if (retryCount >= MaxRetryAttempts)
                {
                    _logger.LogError(e, "Max retry attempts ({MaxRetries}) reached. Giving up.", MaxRetryAttempts);
                    throw new InvalidOperationException($"Failed to reconnect after {MaxRetryAttempts} attempts", e);
                }
                
                _logger.LogError(e, "Failed to reconnect (attempt {RetryCount}/{MaxRetries}). Retrying in {DelayMinutes} minutes", 
                    retryCount, MaxRetryAttempts, RetryDelayMinutes);
                await Task.Delay(TimeSpan.FromMinutes(RetryDelayMinutes));
            }
        } while (!_loginSuccess);
    }
    
    private async Task GetSessionIdAsync()
    {
        var response = await _httpClient.GetAsync(BaseUrl + BasePath + LoginUri);
        if (response.Headers.Contains("PHPSESSID"))
        {
            var sessionId = response.Headers.GetValues("PHPSESSID").FirstOrDefault();
            _logger.LogInformation("Getting session ID : {SessionId}", sessionId);
        }
        PrintHeaders(response);
    }

    private async Task<bool> LoginAsync()
    {
        await GetSessionIdAsync();
        
        var formData = new Dictionary<string, string>
        {
            { "username", _username },
            { "password", _password },
            { "login", "Anmelden" }
        };
        var response = await _httpClient.PostAsync(BaseUrl + BasePath + LoginUri, new FormUrlEncodedContent(formData));

        PrintHeaders(response);

        return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Redirect;
    }
    
    internal async Task<MqttSessionIdResponse?> GetMqttSessionIdAsync(string cid)
    {
        if (!_loginSuccess) await ReconnectAfterFailureAsync();

        var response = await _httpClient.GetAsync(BaseUrl + BasePath + DevicePath + cid);

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            _loginSuccess = false;
            _logger.LogWarning("{MethodName}: returned code is {StatusCode}", nameof(GetMqttSessionIdAsync), response.StatusCode);
            return null;
        }

        var code = HtmlParser.GetCode(await response.Content.ReadAsStringAsync());
        response = await _httpClient.GetAsync(BaseUrl + GetAccessTokenPath + code);

        var responseString = await response.Content.ReadAsStringAsync();

        var sessionIdResponse = JsonSerializer.Deserialize<MqttSessionIdResponse>(responseString, JsonOptions)
                        ?? throw new Exception("Failed to deserialize MQTT session ID response");
        
        return sessionIdResponse;
    }

    public async Task<AutomaticSaltDeviceData> GetDeviceDataAsync(string cid, bool retryIfExpired = true)
    {
        if (!_loginSuccess) await ReconnectAfterFailureAsync();

        var response = await _httpClient.GetAsync(BaseUrl + DataPath + cid);

        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            _loginSuccess = false;

            _logger.LogError("{MethodName}: returned code is {StatusCode}", nameof(GetDeviceDataAsync), response.StatusCode);
            return new AutomaticSaltDeviceData { DeviceState = DeviceState.Error, ErrorMessage = "Unable to retrieve data" };
        }

        var data = HtmlParser.ParseDeviceData(await response.Content.ReadAsStringAsync());
        data.ObtainedAt = _timeProvider.GetUtcNow();

        if (data is { DeviceState: DeviceState.Error, ErrorMessage: "Webportal-Session ist abgelaufen" } && retryIfExpired)
        {
            _loginSuccess = false;
            return await GetDeviceDataAsync(cid, false);
        }

        return data;
    }

    public class MqttSessionIdResponse
    {
        public string? AccessToken { get; set; }
        public string? DeviceSerial { get; set; }
    }
}