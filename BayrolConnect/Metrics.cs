using Prometheus;

namespace BayrolConnect;

public static class Metrics
{
    public static readonly Gauge DeviceState = Prometheus.Metrics
        .CreateGauge("bayrol_device_state", "Device state (0=Ok, 1=Error, 2=Offline, 3=Warning)");

    public static readonly Gauge PhValue = Prometheus.Metrics
        .CreateGauge("bayrol_ph_value", "Current pH value");

    public static readonly Gauge RedoxValue = Prometheus.Metrics
        .CreateGauge("bayrol_redox_value", "Current redox value in mV");

    public static readonly Gauge TemperatureValue = Prometheus.Metrics
        .CreateGauge("bayrol_temperature_value", "Current temperature value in °C");

    public static readonly Gauge SaltValue = Prometheus.Metrics
        .CreateGauge("bayrol_salt_value", "Current salt value in g/L");

    public static readonly Gauge RedoxTargetValue = Prometheus.Metrics
        .CreateGauge("bayrol_redox_target_value", "Current redox target value in mV");

    public static readonly Gauge FilterPumpState = Prometheus.Metrics
        .CreateGauge("bayrol_filter_pump_state", "Filter pump state (0=off, 1=on)");

    public static readonly Gauge PhAutomationState = Prometheus.Metrics
        .CreateGauge("bayrol_ph_automation_state", "pH automation state (0=off, 1=on)");

    public static readonly Gauge SaltProductionState = Prometheus.Metrics
        .CreateGauge("bayrol_salt_production_state", "Salt production state (0=off, 1=on)");

    public static readonly Gauge BoostModeState = Prometheus.Metrics
        .CreateGauge("bayrol_boost_mode_state", "Boost mode state (0=off, 1=on)");

    public static readonly Gauge PhDosingRate = Prometheus.Metrics
        .CreateGauge("bayrol_ph_dosing_rate", "pH dosing rate in %");

    public static readonly Gauge SaltProductionRate = Prometheus.Metrics
        .CreateGauge("bayrol_salt_production_rate", "Salt production rate in %");

    public static readonly Gauge CanisterState = Prometheus.Metrics
        .CreateGauge("bayrol_canister_state", "Canister state (0=ok, 1=empty)");

    public static readonly Gauge WeightedOpTimeHours = Prometheus.Metrics
        .CreateGauge("bayrol_weighted_op_time_hours", "Cumulative weighted operating hours (Zeit × %) for electrode wear tracking");

    public static readonly Gauge SeManualActive = Prometheus.Metrics
        .CreateGauge("bayrol_se_manual_active", "SE timed manual production run active (0=off, 1=on)");

    public static readonly Gauge SeManualProgressMinutes = Prometheus.Metrics
        .CreateGauge("bayrol_se_manual_progress_minutes", "Remaining runtime of active SE manual production run in minutes");
}
