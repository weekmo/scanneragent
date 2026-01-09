using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WIA;

namespace scanneragent
{
    /// <summary>
    /// Background service that hosts an HTTP server for document scanning.
    /// Provides RESTful endpoints to initiate scans and retrieve scanner status.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IConfiguration _config;
        private HttpListener? _listener;
        private string[] _prefixes = Array.Empty<string>();
        private long _maxRequestBodySize;

        /// <summary>
        /// Initializes a new instance of the Worker class.
        /// Reads HTTP server configuration from appsettings.json.
        /// </summary>
        public Worker(ILogger<Worker> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;

            var section = _config.GetSection("HttpServer");
            _prefixes = section.GetSection("Prefixes").Get<string[]>() ?? new[] { "http://+:5000/" };
            _maxRequestBodySize = section.GetValue<long>("MaxRequestBodySize", 10 * 1024 * 1024); // Default to 10 MB
        }

        /// <summary>
        /// Initializes the HTTP listener and starts it.
        /// Called when the service starts.
        /// </summary>
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _listener = new HttpListener();
            foreach (var prefix in _prefixes)
            {
                _listener.Prefixes.Add(prefix);
                _logger.LogInformation("Listening on {Prefix}", prefix);
            }

            try
            {
                _listener.Start();
                _logger.LogInformation("HttpListener started successfully.");
            }
            catch (HttpListenerException ex)
            {
                _logger.LogError(ex, "Failed to start HttpListener. Make sure you have the necessary permissions.");
                throw;
            }
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Main execution loop that accepts and processes HTTP requests.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_listener == null) throw new InvalidOperationException("HttpListener is not initialized.");
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;

                try
                {
                    // Wait for an incoming HTTP request with cancellation support
                    ctx = await _listener.GetContextAsync().WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while getting HTTP context.");
                    continue;
                }

                // Handle request asynchronously in background task
                _ = Task.Run(() => HandleRequestAsync(ctx, stoppingToken));
            }
        }

        /// <summary>
        /// Processes an individual HTTP request and routes it to the appropriate handler.
        /// </summary>
        private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken stoppingToken)
        {
            try
            {
                var req = ctx.Request;
                var res = ctx.Response;
                var path = req.Url?.AbsolutePath?.TrimEnd('/') ?? "/";
                var method = req.HttpMethod?.ToUpperInvariant() ?? "GET";

                // Add CORS headers to allow cross-origin requests
                res.AppendHeader("Access-Control-Allow-Origin", "*");
                res.AppendHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
                res.AppendHeader("Access-Control-Allow-Headers", "Content-Type");

                // Handle CORS preflight requests
                if (method == "OPTIONS")
                {
                    res.StatusCode = 200;
                    res.Close();
                    return;
                }

                // Health check endpoint
                if (path.Equals("/health", StringComparison.OrdinalIgnoreCase) && method == "GET")
                {
                    await SendJson(res, new { status = "OK" }, HttpStatusCode.OK);
                    return;
                }

                // Document scan endpoint
                if (path.Equals("/scan", StringComparison.OrdinalIgnoreCase) && method == "POST")
                {
                    // Validate request body size
                    if (req.ContentLength64 > 0 && req.ContentLength64 > _maxRequestBodySize)
                    {
                        await SendJson(res, new { error = "Request body too large" }, HttpStatusCode.BadRequest);
                        return;
                    }

                    try
                    {
                        // Perform document scan and return result as base64-encoded PNG
                        var imageBase64 = await ScanDocument();
                        await SendJson(res, new { image = imageBase64, format = "png" }, HttpStatusCode.OK);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogWarning(ex, "Scanner not available or error during scan.");
                        await SendJson(res, new { error = ex.Message }, HttpStatusCode.ServiceUnavailable);
                    }
                    return;
                }

                // Return 404 for unknown routes
                await SendJson(res, new { error = "Not found" }, HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error.");
                try
                {
                    await SendJson(ctx.Response, new { error = "Internal server error" }, HttpStatusCode.InternalServerError);
                }
                catch { }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
                try { ctx.Response.Close(); } catch { }
            }
        }

        /// <summary>
        /// Sends a JSON response to the client.
        /// </summary>
        private async Task SendJson(HttpListenerResponse res, object payload, HttpStatusCode status)
        {
            // Serialize object to JSON with camelCase property names
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var buffer = Encoding.UTF8.GetBytes(json);

            res.StatusCode = (int)status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentEncoding = Encoding.UTF8;
            res.ContentLength64 = buffer.LongLength;
            await res.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Scans a document from a connected WIA scanner and returns it as base64-encoded PNG.
        /// Runs on an STA thread as required by WIA COM interop.
        /// </summary>
        private async Task<string> ScanDocument()
        {
            string? result = null;
            Exception? threadException = null;

            // Create STA thread for WIA COM interop
            var thread = new Thread(() =>
            {
                IDeviceManager? deviceManager = null;
                try
                {
                    _logger.LogInformation("ScanDocument: Starting WIA scan...");

                    // Create WIA DeviceManager through COM interop
                    var deviceManagerType = Type.GetTypeFromProgID("WIA.DeviceManager");
                    if (deviceManagerType == null)
                    {
                        _logger.LogWarning("WIA DeviceManager type not found. WIA may not be installed.");
                        throw new InvalidOperationException("WIA is not installed on this system.");
                    }

                    _logger.LogInformation("ScanDocument: Creating DeviceManager instance...");
                    deviceManager = (IDeviceManager?)Activator.CreateInstance(deviceManagerType);

                    if (deviceManager == null)
                    {
                        throw new InvalidOperationException("Failed to create WIA DeviceManager instance.");
                    }

                    // Enumerate connected WIA devices
                    int deviceCount = deviceManager.DeviceInfos.Count;
                    _logger.LogInformation("ScanDocument: Found {DeviceCount} WIA devices", deviceCount);

                    if (deviceCount == 0)
                    {
                        throw new InvalidOperationException("No WIA devices found. Please ensure a scanner is connected and installed.");
                    }

                    // Connect to the first scanner device
                    _logger.LogInformation("ScanDocument: Connecting to device 1...");
                    DeviceInfo deviceInfo = deviceManager.DeviceInfos[1];
                    Device? device = null;

                    try
                    {
                        device = deviceInfo.Connect();
                        _logger.LogInformation("ScanDocument: Device connected. Items count: {ItemCount}", device?.Items.Count ?? 0);

                        if (device == null || device.Items.Count < 1)
                        {
                            throw new InvalidOperationException("Failed to connect to scanner or scanner has no items.");
                        }

                        // Get the first scannable item from the device
                        Item item = device.Items[1];
                        _logger.LogInformation("ScanDocument: Got scanner item");

                        // Configure scanner resolution and intent
                        SetScannerProperty(item, "Horizontal Resolution", 300);
                        SetScannerProperty(item, "Vertical Resolution", 300);
                        SetScannerProperty(item, "Current Intent", 0x00000001);
                        _logger.LogInformation("ScanDocument: Scanner properties set");

                        // Transfer image from scanner in PNG format
                        const string wiaFormatPng = "{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}";
                        _logger.LogInformation("ScanDocument: Transferring image...");
                        ImageFile imageFile = (ImageFile)item.Transfer(wiaFormatPng);

                        // Extract image data and encode as base64
                        _logger.LogInformation("ScanDocument: Getting image data...");
                        Vector fileData = (Vector)imageFile.FileData;
                        byte[] imageBytes = (byte[])fileData.get_BinaryData();

                        result = Convert.ToBase64String(imageBytes);
                        _logger.LogInformation("ScanDocument: Successfully scanned document. Image size: {Size} bytes", imageBytes.Length);

                        // Release COM object references
                        try { Marshal.ReleaseComObject(imageFile); } catch { }
                        try { Marshal.ReleaseComObject(item); } catch { }
                    }
                    finally
                    {
                        if (device != null)
                        {
                            try { Marshal.ReleaseComObject(device); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ScanDocument: Error in thread");
                    threadException = ex;
                }
                finally
                {
                    if (deviceManager != null)
                    {
                        try { Marshal.ReleaseComObject(deviceManager); } catch { }
                    }
                }
            })
            {
                ApartmentState = ApartmentState.STA,
                IsBackground = true,
                Name = "WIA-Scanner-Thread"
            };

            _logger.LogInformation("ScanDocument: Starting thread...");
            thread.Start();

            // Wait for scan operation to complete with 5-minute timeout
            bool threadCompleted = thread.Join(TimeSpan.FromMinutes(5));

            if (!threadCompleted)
            {
                _logger.LogError("ScanDocument: Thread timed out after 5 minutes");
                thread.Abort();
                throw new InvalidOperationException("Scan operation timed out.");
            }

            if (threadException != null)
            {
                _logger.LogError(threadException, "ScanDocument: Thread had exception");
                throw threadException;
            }

            if (string.IsNullOrEmpty(result))
            {
                throw new InvalidOperationException("Scan completed but no image data was retrieved.");
            }

            return result;
        }

        /// <summary>
        /// Sets a property on a WIA scanner item if it exists.
        /// Silently ignores properties not supported by the scanner.
        /// </summary>
        private static void SetScannerProperty(Item item, string propertyName, object value)
        {
            try
            {
                foreach (Property prop in item.Properties)
                {
                    if (prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        prop.set_Value(value);
                        return;
                    }
                }
            }
            catch
            {
                // Property may not be supported by this scanner
            }
        }

        /// <summary>
        /// Stops the HTTP listener when the service is shutting down.
        /// </summary>
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _listener?.Stop();
            _logger.LogInformation("Server stopped");
            return Task.CompletedTask;
        }
    }
}
