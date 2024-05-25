using System.Text;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;

namespace BayrolConnect;

public sealed class AzureIotCentralDevice(string idScope, string deviceId, string primaryKey, ILogger logger) : IDisposable, IAsyncDisposable
{
    private const string GlobalDeviceEndpoint = "global.azure-devices-provisioning.net";
    private DeviceClient? _deviceClient;


    public async Task<bool> Connect()
    {
        var security = new SecurityProviderSymmetricKey(deviceId, primaryKey, null);
        var transport = new ProvisioningTransportHandlerMqtt();
        var provisioningClient = ProvisioningDeviceClient.Create(GlobalDeviceEndpoint, idScope, security, transport);

        logger.LogInformation($"Provisioning device with ID: {deviceId}");
        var result = await provisioningClient.RegisterAsync();

        if (result.Status != ProvisioningRegistrationStatusType.Assigned)
        {
            logger.LogError($"Failed to register device: {result.Status}");
            return false;
        }

        logger.LogInformation($"Device successfully registered: {result.AssignedHub}; DeviceId: {result.DeviceId}");

        _deviceClient = DeviceClient.Create(result.AssignedHub,
            new DeviceAuthenticationWithRegistrySymmetricKey(result.DeviceId, primaryKey), TransportType.Mqtt);

        _deviceClient.SetRetryPolicy(new NoRetry());
        await _deviceClient.OpenAsync();

        return true;
    }

    public Task SendEventAsync(string json)
    {
        if(_deviceClient == null) throw new InvalidOperationException("device not connected");
        
        var message = new Message(Encoding.UTF8.GetBytes(json));
        return _deviceClient.SendEventAsync(message);
    }

    public void Dispose()
    {
        _deviceClient?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_deviceClient != null) await _deviceClient.DisposeAsync();
    }
}