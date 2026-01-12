[![.NET CI/CD](https://github.com/weekmo/scanneragent/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/weekmo/scanneragent/actions/workflows/dotnet-desktop.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)

# Scanner Agent

HTTP API service for document scanning via Windows Image Acquisition (WIA).

## Installation

Download the latest release from the [Releases](https://github.com/weekmo/scanneragent/releases) page.

Extract the archive to your preferred location (e.g., `C:\Services\ScannerAgent`).

### System Requirements

- **Windows 10/11/Server 2016+**: WIA is included by default
- **Windows 7/8/Server 2012**: May require WIA 2.0 installation from Windows Update or manual download
- WIA-compatible scanner device connected and installed

### Configuration

Edit `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "HttpServer": {
    "Prefixes": [ "http://127.0.0.1:5001/" ],
    "MaxRequestBodySize": 10485760,
    "ContextAcceptTimeoutSeconds": 30,
    "ApiSecret": "your-secret-key-here"
  }
}
```

**Required:** Set a unique value for `ApiSecret`. The application will not start without it.

**Configuration options:**
- **Prefixes**: Listener address. Use `http://127.0.0.1:PORT/` or `http://localhost:PORT/`. Do not use `http://+:PORT/`.
- **MaxRequestBodySize**: Maximum request size in bytes (10485760 = 10 MB)
- **ContextAcceptTimeoutSeconds**: HTTP context timeout
- **ApiSecret**: Authentication secret for `/scan` endpoint (required)

### Running the Service

#### Option 1: Run Directly

Double-click `scanneragent.exe` or run from command line:

```
scanneragent.exe
```

The service listens on `http://127.0.0.1:5001/` by default.

#### Option 2: Install as Windows Service

Run commands in Administrator PowerShell or Command Prompt.

**Note:** Replace `C:\Services\ScannerAgent\scanneragent.exe` with your actual installation path.

**Create service:**
```
sc create ScannerAgent binPath= "C:\Services\ScannerAgent\scanneragent.exe" start= auto DisplayName= "Scanner Agent Service"
sc description ScannerAgent "HTTP API service for document scanning via WIA"
```

**Start service:**
```
sc start ScannerAgent
```

**Manage service:**
```
sc query ScannerAgent
sc stop ScannerAgent
sc delete ScannerAgent
```

**Using PowerShell (optional alternative):**
```powershell
New-Service -Name "ScannerAgent" -BinaryPathName "C:\Services\ScannerAgent\scanneragent.exe" -DisplayName "Scanner Agent Service" -Description "HTTP API service for document scanning via WIA" -StartupType Automatic
Start-Service -Name "ScannerAgent"
```

**View logs:**

When running as a service, logs are in the Windows Application event log:
1. Open Event Viewer (`eventvwr.msc`)
2. Navigate to Windows Logs > Application
3. Filter by source "scanneragent"

## API Usage

### GET /health

Check service status.

**Response:**
```json
{
  "status": "OK"
}
```

### POST /scan

Scan a document and return base64-encoded PNG image.

**Headers:**
```
X-API-Secret: your-secret-key-here
```

**Response (200):**
```json
{
  "image": "iVBORw0KGgoAAAANSUhEUg...",
  "format": "png"
}
```

**Response (401):**
```json
{
  "error": "Unauthorized. Valid API secret required."
}
```

**Response (503):**
```json
{
  "error": "No WIA devices found. Please ensure a scanner is connected and installed."
}
```

### Examples

**cURL:**
```bash
curl -X POST http://localhost:5001/scan \
  -H "X-API-Secret: your-secret-key-here"
```

**JavaScript:**
```javascript
fetch('http://localhost:5001/scan', {
  method: 'POST',
  headers: { 'X-API-Secret': 'your-secret-key-here' }
})
.then(response => response.json())
.then(data => console.log('Image:', data.image));
```

**Python:**
```python
import requests

response = requests.post(
    'http://localhost:5001/scan',
    headers={'X-API-Secret': 'your-secret-key-here'}
)

if response.status_code == 200:
    data = response.json()
    print(f"Scanned image: {data['image'][:50]}...")
else:
    print(f"Error: {response.json()['error']}")
```

**C#:**
```csharp
using var client = new HttpClient();
client.DefaultRequestHeaders.Add("X-API-Secret", "your-secret-key-here");
var response = await client.PostAsync("http://localhost:5001/scan", null);
var content = await response.Content.ReadAsStringAsync();
```

### Example Application

See [Local Scanner Web App](https://github.com/weekmo/localscanner) for a complete web application integration example.

## Technical Details

- **Threading**: Scanning runs on STA thread (required for WIA COM)
- **Resolution**: 300 DPI (horizontal and vertical)
- **Format**: PNG, base64-encoded
- **Timeout**: 5 minutes per scan operation
- **CORS**: Enabled for all origins

---

## Development

### Requirements

- .NET 10 SDK
- Windows

### Build

```
dotnet build
```

### Run (Development)

```
dotnet run
```

Service listens on `http://127.0.0.1:5001/` by default. Edit `appsettings.json` to change configuration.

### Publish

**Self-contained (includes .NET runtime):**
```
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

**Framework-dependent (requires .NET installed):**
```
dotnet publish -c Release -r win-x64 --self-contained false -o ./publish
```

**Target platforms:**
- `win-x64`: Windows 64-bit (most common)
- `win-x86`: Windows 32-bit (optional, for older systems)
- `win-arm64`: Windows ARM 64-bit (optional, for ARM devices)

### Project Structure

- `Program.cs`: Application entry point
- `Worker.cs`: HTTP server and scanning logic
- `appsettings.json`: Configuration

### Dependencies

- Microsoft.Extensions.Hosting 10.0.1
- WIA COM library

## Contributing

Contributions are welcome! To contribute:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Commit your changes (`git commit -am 'Add new feature'`)
4. Push to the branch (`git push origin feature/your-feature`)
5. Open a Pull Request

Please ensure:
- Code follows existing style and conventions
- Changes are tested on Windows with a WIA scanner
- Commit messages are clear and descriptive

## Support

If you find this project useful, consider supporting its development:

[![Sponsor](https://img.shields.io/badge/Sponsor-?-red?style=for-the-badge)](https://github.com/sponsors/weekmo)

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.
