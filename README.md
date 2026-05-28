# BayrolConnect
C# library to connect to Automatic Salt using bayrol-poolaccess.de and/or MQTT

It includes a Dockerfile to build a docker image that can publish your device-data to a Prometheus/Grafana monitoring solution.

## Known Issues
Since I needed this to work before my vacation, it isn't as clean & robust as I would
like it to be - the WebConnector should be more reliable than the MqttConnector,
but both are designed to throw exceptions when something goes wrong, instead of trying
to recover from it - I simply rely on the container to be restarted for now.

The MqttConnector used to suffer from stalled values: the Bayrol server pushes value
changes but does not reliably keep pushing every topic (redox and production rate in
particular could go silent), leaving frozen readings. The connector now actively
re-requests all values on an interval, subscribes with QoS 1, and runs a watchdog that
logs a warning when a topic goes stale and forces a reconnect (full re-subscribe) if a
topic stays silent for too long. See the optional `RepollIntervalSeconds`,
`StaleWarnSeconds` and `StaleReconnectSeconds` config fields below to tune this.

## Supported Devices
I've written this for Bayrol's Automatic Salt device, but the WebConnector should be easily adaptable
to other devices. I don't know if other devices also support MQTT, but if they do we'd probably need
to make the MqttConnector more generic.

## What about sending commands via MQTT?
This is implemented for setting the RedoxTargetValue.

## Usage
You can use the library for your own project, or use the provided application to connect
to a Prometheus/Grafana monitoring solution.

To do this, just pass a json via the environment variable `CONFIG` to the container, which
contains the following fields:

```json
{
    "User": "<bayrol-poolaccess user>",
    "Password": "<bayrol-poolaccess password>",
    "Cid": "<cid extracted from the url to your device>",
    "UseMqtt": true,
    "RedoxTargetValues":
    {
        "05:00:00" : 635,
        "10:00:00" : 625,
        "17:30:00" : 720
    },
    "RepollIntervalSeconds": 60,
    "StaleWarnSeconds": 180,
    "StaleReconnectSeconds": 360
}
```
UseMqtt = true will use the MqttConnector, otherwise the WebConnector will be used.

`RepollIntervalSeconds`, `StaleWarnSeconds` and `StaleReconnectSeconds` are optional
(MQTT only) and default to 60 / 180 / 360 seconds respectively. They control how often
the connector re-requests all values, when it logs a stalled-topic warning, and when it
forces a reconnect because a topic has stopped updating despite re-polling.
The WebConnector is probably more reliable, but the MqttConnector gives you more data;
see [AutomaticSaltDeviceData.cs](BayrolLib/AutomaticSaltDeviceData.cs).
vs. [ExtendedAutomaticSaltDeviceData.cs](BayrolLib/ExtendedAutomaticSaltDeviceData.cs).

Also, when using MQTT you can set the RedoxTargetValue for specific times of the day. Please
note that the times are in UTC.

To run the solution, execute the following command:
```bash
docker-compose up -d
```

You can then access Grafana at http://localhost:3000.

## Contributing
I'm happy to accept pull requests, so if you feel that something is missing... go ahead :)