# Umbraco Community Cloud Health Checks

<p align="center">
  <img src="https://raw.githubusercontent.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/develop/v1/src/Umbraco.Community.Cloud.HealthChecks/cloud-healthchecks.png" alt="Cloud Health Checks" width="128" height="128">
</p>

A package that provides health checks for Umbraco Cloud environments, helping you monitor critical system resources and storage usage.

## Overview

This package adds several health checks specifically designed for Umbraco Cloud deployments. These checks help you monitor storage usage in Umbraco Cloud.

## Features

### Included Health Checks

- **Azure Storage Usage** - Monitors storage usage for `C:\home` and `C:\local` directories in Umbraco Cloud
  - Warning threshold at 75% usage
  - Error threshold at 90% usage
  - Windows-only support

- **NuGet Cache Size** - Monitors the size of the NuGet cache directory
  
- **Umbraco Logs Folder Size** - Tracks the size of Umbraco log files

## Installation

Install via NuGet Package Manager or the .NET CLI:

```bash
dotnet add package Umbraco.Community.Cloud.HealthChecks
```

Or via NuGet Package Manager:

```bash
Install-Package Umbraco.Community.Cloud.HealthChecks
```

## Usage

After installation, the health checks will automatically appear in the Umbraco backoffice under:

**Settings → Health Check**

The health checks are grouped under **"Umbraco Cloud"** for easy identification.

## Configuration

All health check thresholds can be customized via `appsettings.json`. The package uses sensible defaults, but you can override them to suit your environment.

See the [Configuration Guide](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/blob/develop/v1/CONFIGURATION.md) for detailed configuration options and examples.

Quick example:

```json
{
  "CloudHealthChecks": {
    "UmbracoLogs": {
      "WarningThresholdMb": 200,
      "ErrorThresholdMb": 1000
    }
  }
}
```

## Requirements

- Umbraco CMS v17+

## Proactive Monitoring

To get the most value from these health checks, set up automated monitoring using Umbraco's built-in notification system or third-party integrations.

### Built-in Email Notifications

Umbraco includes a built-in email notification system for health checks. Configure it in **Settings → Health Check** to receive email alerts when issues are detected. See the [official documentation](https://docs.umbraco.com/umbraco-cms/reference/configuration/healthchecks#notification) for setup instructions. This allows you to:
- Get early warnings when storage reaches 75% capacity
- Receive critical alerts at 90% usage before your site runs out of space
- Monitor log file growth and aging
- Take proactive action to prevent downtime

### Slack Notifications

For real-time team alerts, combine this package with the [Health Check Slack Notification Method](https://marketplace.umbraco.com/package/our.umbraco.healthcheckslacknotificationmethod) package. This sends automated Slack messages to your team channel when storage thresholds are exceeded.

Both notification methods are especially valuable in Umbraco Cloud environments where storage space is limited and can impact site availability.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Repository

[https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks)

## License

This project is licensed under the terms specified in the LICENSE file.

## Credits

Developed by **Crumpled Dog** and the **Umbraco Community**.

## Support

For issues, questions, or feature requests, please use the [GitHub Issues](https://github.com/CrumpledDog/Umbraco.Community.Cloud.HealthChecks/issues) page.
