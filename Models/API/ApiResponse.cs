// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.API;

public class ApiResponse
{
    public ApiResponse(string message)
    {
        Message = message;
    }

    public string Message { get; set; }
}