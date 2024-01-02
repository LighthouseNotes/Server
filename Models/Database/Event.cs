using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Server.Models.Database;

public class Event
{
    [Key] public long Id { get; init; }
    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }
    public required JsonDocument Data { get; init; }
    public required string EventType { get; init; }
    public virtual User? User { get; init; }

    public void Dispose()
    {
        Data?.Dispose();
    }
}