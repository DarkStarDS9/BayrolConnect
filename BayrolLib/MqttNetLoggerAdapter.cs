using Microsoft.Extensions.Logging;
using MQTTnet.Diagnostics;

namespace BayrolLib
{
    public class MqttNetLoggerAdapter : IMqttNetLogger
    {
        private readonly ILogger _logger;

        public MqttNetLoggerAdapter(ILogger logger)
        {
            _logger = logger;
        }

        public bool IsEnabled => true;

        public void Publish(MqttNetLogLevel logLevel, string source, string message, object[]? parameters, Exception? exception)
        {
            var formattedMessage = parameters?.Length > 0 ? string.Format(message, parameters) : message;

            LogLevel msLogLevel = logLevel switch
            {
                MqttNetLogLevel.Verbose => LogLevel.Trace,
                MqttNetLogLevel.Info    => LogLevel.Information,
                MqttNetLogLevel.Warning => LogLevel.Warning,
                MqttNetLogLevel.Error   => LogLevel.Error,
                _                       => LogLevel.Debug // Default for anything else
            };

            // Prefix message with source to know it's from MQTTnet internals
            _logger.Log(msLogLevel, exception, $"[MQTTnet::{source}] {formattedMessage}");
        }
    }
}
