namespace Server.Models.Search;

public class ContemporaneousNote
{
        public long Id { get; set; }
        public long CaseId { get; set; }
        public long UserId { get; set; }
        public string? Content { get; set; }
}

public class SharedContemporaneousNote
{
        public long Id { get; set; }
        public long CaseId { get; set; }
        public string? Content { get; set; }
}