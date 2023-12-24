namespace LighthouseNotesServer.Models.API;

public class ContemporaneousNotes
{
    public required List<string> Notes { get; set; }
}
public class AddContemporaneousNotes
{
    public required string Content { get; set; }
}