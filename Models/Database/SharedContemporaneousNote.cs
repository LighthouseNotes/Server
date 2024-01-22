// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace Server.Models.Database;

public class SharedContemporaneousNote : Base
{
    [Key] public long Id { get; init; }
    public User Creator { get; init; } = null!;
}