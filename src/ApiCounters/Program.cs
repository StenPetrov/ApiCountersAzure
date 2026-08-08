using Azure.Data.Tables;
using Microsoft.Extensions.Azure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        var connectionString = context.Configuration["StorageConnectionString"]
            ?? context.Configuration["AzureWebJobsStorage"]
            ?? "UseDevelopmentStorage=true";

        services.AddAzureClients(clientBuilder =>
        {
            clientBuilder.AddTableServiceClient(connectionString);
        });
    })
    .Build();

await host.RunAsync();
