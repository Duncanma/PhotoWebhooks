using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using System.Threading.Tasks;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(
        app =>
        {
            app.UseMiddleware<ForwardedForHeaderMiddleware>();
        }
    )
    .ConfigureServices(services => {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

host.Run();

public class ForwardedForHeaderMiddleware : IFunctionsWorkerMiddleware
{
    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        if (context.FunctionDefinition.Name == "event")
        {
            await RestoreForwardedForAsync(context);
        }
        await next(context);
    }

    private static async Task RestoreForwardedForAsync(FunctionContext context)
    {
        var val = GetForwardedFor(context);
        var req = await context.GetHttpRequestDataAsync();
        if (!string.IsNullOrWhiteSpace(val))
        {
            req?.Headers.TryAddWithoutValidation(ForwardedHeadersDefaults.XForwardedForHeaderName, val);
        }
    }

    private static string? GetForwardedFor(FunctionContext context)
    {
        if (!context.BindingContext.BindingData.TryGetValue("Headers", out object? rawHeaders)
            || rawHeaders == null)
        {
            return null;
        }

        dynamic obj = JsonConvert.DeserializeObject(rawHeaders.ToString() ?? string.Empty);

        var val = obj?[ForwardedHeadersDefaults.XForwardedForHeaderName]?.Value as string;
        return val;
    }
}