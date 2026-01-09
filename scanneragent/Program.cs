using scanneragent;

// Build the host application
var builder = Host.CreateApplicationBuilder(args);

// Register the Worker hosted service, which handles HTTP requests and scanner operations
builder.Services.AddHostedService<Worker>();

// Build and run the host
var host = builder.Build();
host.Run();
