using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Options;
using TransactionProcessor;
using TransactionProcessor.Host;

// Make the Azure SDK emit traces (Service Bus receive and send spans) for Application Insights.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ProcessorOptions>()
    .Bind(builder.Configuration.GetSection(ProcessorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Application Insights. The connection string comes from the APPLICATIONINSIGHTS_CONNECTION_STRING app setting;
// without it (local runs) telemetry is simply off.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

// One credential for everything: the user-assigned managed identity chosen by client ID.
builder.Services.AddSingleton<TokenCredential>(sp =>
    new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = sp.GetRequiredService<IOptions<ProcessorOptions>>().Value.ManagedIdentityClientId,
    }));

builder.Services.AddSingleton(sp =>
    new ServiceBusClient(sp.GetRequiredService<IOptions<ProcessorOptions>>().Value.ServiceBusNamespace, sp.GetRequiredService<TokenCredential>()));
builder.Services.AddSingleton<IQueueSender>(sp =>
    new ServiceBusQueueSender(sp.GetRequiredService<ServiceBusClient>(), sp.GetRequiredService<IOptions<ProcessorOptions>>().Value.QueueName));
builder.Services.AddSingleton<IEventHubSender>(sp =>
{
    var o = sp.GetRequiredService<IOptions<ProcessorOptions>>().Value;
    return new EventHubProducerSender(o.EventHubsNamespace, o.EventHubName, sp.GetRequiredService<TokenCredential>());
});
builder.Services.AddSingleton(sp =>
    new TransactionHandler(
        SqlConnectionStrings.ForManagedIdentity(sp.GetRequiredService<IOptions<ProcessorOptions>>().Value),
        sp.GetRequiredService<IQueueSender>(),
        sp.GetRequiredService<IEventHubSender>()));

builder.Services.AddSingleton<HealthState>();
builder.Services.AddHostedService<ReceiveLoop>();

var app = builder.Build();

// App Service health check path: 200 while the receive loop is running.
app.MapGet("/health", (HealthState health) =>
    health.Processing
        ? Results.Ok(new { status = "healthy" })
        : Results.Json(new { status = "not processing" }, statusCode: 503));

app.Run();
