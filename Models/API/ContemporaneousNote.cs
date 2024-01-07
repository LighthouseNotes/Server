namespace Server.Models.API;

public class ContemporaneousNotes
{
    public required Guid Id { get; set; }
    public required DateTime Created { get; set; }
}

public class SharedContemporaneousNotes
{
    public required Guid Id { get; set; }
    public required DateTime Created { get; set; }
    public required User Creator { get; set; }
}