// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace LighthouseNotesServer.Models.API;

public class UserAudit
{
    public DateTime DateTime { get; init; }
    public required string Action { get; init; }
}