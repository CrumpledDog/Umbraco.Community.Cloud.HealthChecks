# Umbraco Community Cloud Health Checks

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

## Requirements

- Umbraco CMS v17+

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
