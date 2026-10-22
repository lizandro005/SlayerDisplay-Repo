using SlayerDisplay;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "SlayerDisplay");
builder.Services.AddHostedService<DisplayWorker>();
builder.Build().Run();
