[![.NET CI/CD](https://github.com/weekmo/scanneragent/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/weekmo/scanneragent/actions/workflows/dotnet-desktop.yml)

# Scanner Agent

A .NET Worker Service that provides a RESTful API for document scanning via Windows Image Acquisition (WIA).

## Overview

Scanner Agent is a background service that hosts an HTTP server and exposes endpoints for scanning documents from connected WIA-compatible scanner devices. It handles scanner device enumeration, configuration, and image transfer with support for base64 encoding.

## Requirements

- .NET 10 or later
- Windows operating system
- WIA (Windows Image Acquisition) installed on the system
- A WIA-compatible scanner device connected and properly installed

## Building

```
dotnet build
```

## Running

```
dotnet run
```

The service will start listening on the URL specified in `appsettings.json`. Default: `http://+:5001/`

## Publishing

### Self-Contained Executable

To create a self-contained executable that includes the .NET runtime:

```
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

This creates a standalone executable in the `./publish` directory that can run without .NET installed on the target machine.

**Parameters:**
- `-c Release`: Build in release configuration
- `-r win-x64`: Target Windows x64 runtime
- `--self-contained true`: Include .NET runtime in output
- `-o ./publish`: Output directory

**Alternative runtimes:**
- `win-x64`: Windows 64-bit
- `win-x86`: Windows 32-bit
- `win-arm64`: Windows ARM 64-bit

### Framework-Dependent Executable

For smaller deployments where .NET is already installed:

```
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

## Installing as a Windows Service

### Prerequisites

Run all commands in an elevated (Administrator) PowerShell or Command Prompt.

### Step 1: Publish the Application

First, publish the application as a self-contained executable:

```
dotnet publish -c Release -r win-x64 --self-contained true -o C:\Services\ScannerAgent
```

### Step 2: Create the Windows Service

Use the `sc` command to create the service:

```
sc create ScannerAgent binPath= "C:\Services\ScannerAgent\scanneragent.exe" start= auto DisplayName= "Scanner Agent Service"
```

**Parameters:**
- `binPath=`: Full path to the published executable (space after `=` is required)
- `start=`: Service startup type (`auto`, `demand`, or `disabled`)
- `DisplayName=`: Friendly name shown in Services console

### Step 3: Configure Service Description (Optional)

```
sc description ScannerAgent "HTTP API service for document scanning via WIA"
```

### Step 4: Start the Service

```
sc start ScannerAgent
```

### Managing the Service

**Check service status:**
```
sc query ScannerAgent
```

**Stop the service:**
```
sc stop ScannerAgent
```

**Restart the service:**
```
sc stop ScannerAgent
sc start ScannerAgent
```

**Delete the service:**
```
sc stop ScannerAgent
sc delete ScannerAgent
```

### Alternative: Using PowerShell

Create and start the service using PowerShell:

```powershell
New-Service -Name "ScannerAgent" -BinaryPathName "C:\Services\ScannerAgent\scanneragent.exe" -DisplayName "Scanner Agent Service" -Description "HTTP API service for document scanning via WIA" -StartupType Automatic

Start-Service -Name "ScannerAgent"
```

**Verify service:**
```powershell
Get-Service -Name "ScannerAgent"
```

**Remove service:**
```powershell
Stop-Service -Name "ScannerAgent"
Remove-Service -Name "ScannerAgent"
```

### Network Permissions

If using `http://+:5001/` (listening on all interfaces), you may need to reserve the URL:

```
netsh http add urlacl url=http://+:5001/ user=BUILTIN\Users
```

Or grant permission to the service account (typically `NT AUTHORITY\SYSTEM` for services):

```
netsh http add urlacl url=http://+:5001/ user="NT AUTHORITY\SYSTEM"
```

### Viewing Service Logs

When running as a Windows service, console output is not visible. Configure file logging or use Event Viewer to view logs. The service logs to the Application event log by default.

To view in Event Viewer:
1. Open Event Viewer (`eventvwr.msc`)
2. Navigate to Windows Logs > Application
3. Look for entries from source "scanneragent"

## Configuration

Configuration is managed through `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "HttpServer": {
    "Prefixes": [ "http://+:5001/" ],
    "MaxRequestBodySize": 10485760,
    "ContextAcceptTimeoutSeconds": 30
  }
}
```

### Configuration Options

- **Prefixes**: Array of HTTP listener prefixes. Use `http://+:PORT/` for any interface or `http://localhost:PORT/` for localhost only
- **MaxRequestBodySize**: Maximum allowed request body size in bytes (default: 10 MB)
- **ContextAcceptTimeoutSeconds**: Timeout for accepting HTTP contexts

## API Endpoints

### GET /health

Returns the health status of the service.

**Response:**
```json
{
  "status": "OK"
}
```

### POST /scan

Initiates a document scan from the connected scanner and returns the scanned image.

**Response:**
```json
{
  "image": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
  "format": "png"
}
```

The `image` field contains the scanned document as a base64-encoded PNG image.

**Error Response (if scanner unavailable):**
```json
{
  "error": "No WIA devices found. Please ensure a scanner is connected and installed."
}
```

### CORS

All endpoints support CORS requests. CORS headers are automatically added to all responses.

## Technical Details

### Threading Model

The scanning operation runs on a dedicated Single-Threaded Apartment (STA) thread, which is required for WIA COM interop. This ensures proper initialization of COM objects.

### Scanner Properties

The service configures the following scanner properties before scanning:
- Horizontal Resolution: 300 DPI
- Vertical Resolution: 300 DPI
- Current Intent: Document scanning intent flag

### Image Format

Scanned images are returned in PNG format and encoded as base64 strings for HTTP transmission.

### Error Handling

The service handles various error scenarios:
- WIA not installed on the system
- No scanner devices detected
- Scanner connection failures
- Timeout during scan operation (5-minute limit)
- Request body exceeds size limit
- COM interop exceptions

Detailed error messages are logged and returned to the client.

## Logging

The service uses structured logging via Microsoft.Extensions.Logging. Log messages include:
- HTTP server startup and listener initialization
- Device enumeration and connection status
- Scanner property configuration
- Image transfer progress
- Errors and warnings with context

Configure logging levels in `appsettings.json` under the `Logging` section.

## Project Structure

- `Program.cs`: Application entry point and dependency injection setup
- `Worker.cs`: Main background service implementing the HTTP server and scanning logic
- `appsettings.json`: Configuration file

## Dependencies

- Microsoft.Extensions.Hosting 10.0.1
- WIA (Windows Image Acquisition) COM library
