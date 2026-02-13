# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in BayrolConnect, please report it by creating a private security advisory on GitHub or by emailing the maintainer directly. Do not create a public issue for security vulnerabilities.

## Security Best Practices

### Credential Management

1. **Never commit credentials**: Never commit your Bayrol username, password, or device CID to version control
2. **Use environment variables**: Always pass sensitive configuration through environment variables
3. **Change default passwords**: Change the default Grafana admin password by setting `GRAFANA_ADMIN_PASSWORD` environment variable

### Configuration Example

```bash
# Set your credentials as environment variables
export CONFIG='{"User":"your-username","Password":"your-password","Cid":"your-device-cid","UseMqtt":true}'
export GRAFANA_ADMIN_PASSWORD='your-secure-password'

# Run docker-compose
docker-compose up -d
```

### Network Security

1. **Use HTTPS**: When deploying to production, configure TLS/HTTPS for all services
2. **Restrict access**: Use firewall rules to restrict access to Grafana (port 3000) and Prometheus (port 9090)
3. **Internal networks**: Consider running on an internal network or VPN

### Container Security

1. **Keep images updated**: Regularly update Docker images to get security patches
2. **Scan for vulnerabilities**: Use tools like `docker scan` or Trivy to check for vulnerabilities
3. **Resource limits**: Set appropriate CPU and memory limits in docker-compose.yml

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| Latest  | :white_check_mark: |

## Known Security Considerations

1. The application stores metrics data unencrypted in Prometheus and Grafana
2. MQTT connections use WebSocket without additional authentication beyond the access token
3. HTTP API exposes metrics on port 8080 without authentication
