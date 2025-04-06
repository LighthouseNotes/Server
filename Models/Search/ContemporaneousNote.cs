// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Server.Models.Search;

public class ContemporaneousNote
{
    public long Id { get; init; }
    public long CaseId { get; set; }
    public string? EmailAddress { get; set; }
    public string? Content { get; set; }
}

public class SharedContemporaneousNote
{
    public long Id { get; init; }
    public long CaseId { get; set; }
    public string? Content { get; set; }
}