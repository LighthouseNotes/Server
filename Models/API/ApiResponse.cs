// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.API;

public class ApiResponse(string message)
{
    public string Message { get; set; } = message;
}