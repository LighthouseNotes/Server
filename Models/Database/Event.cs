using System.Text.Json;

// ReSharper disable UnusedMember.Global

namespace Server.Models.Database;

public class Event
{
    [Key] public long Id { get; init; }
    public DateTime Created { get; init; }
    public DateTime Updated { get; init; }
    public required JsonDocument Data { get; init; }
    [StringLength(50)] public required string EventType { get; init; }
    public User? User { get; init; }

    public void Dispose()
    {
        Data.Dispose();
    }
}