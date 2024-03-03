namespace Server.Models;

public class Document
{
        public long Id { get; set; }
        public long CaseId { get; set; }
        public long UserId { get; set; }
        public string? Content { get; set; }
}

public class SharedDocument
{
        public long Id { get; set; }
        public long CaseId { get; set; }
        public string? Content { get; set; }
}