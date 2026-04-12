using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectra.Contracts.Events;

public sealed class ConsoleEventSink : IEventSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public Task PublishAsync(WorkflowEvent evt, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize<object>(evt, JsonOptions);
        Console.WriteLine($"[{evt.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{evt.EventType}] RunId={evt.RunId} {payload}");
        return Task.CompletedTask;
    }
}
