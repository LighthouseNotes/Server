using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Text.Json;

namespace LighthouseNotesServer.Models.Database;

public class Event
{
    [Key]
    public long Id { get; set; }
    public DateTime Created { get; set; }
    public DateTime Updated { get; set; }
    public required JsonDocument Data { get; set; }
    public required string EventType { get; set; }
    public virtual Organization? Organization { get; set; }
    public virtual User? User { get; set; }

    public void Dispose() => Data?.Dispose();
}