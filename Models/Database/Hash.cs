using System.ComponentModel.DataAnnotations;

namespace LighthouseNotesServer.Models.Database;

public class Hash : Base
{
    [Key] public int Id { get; set; }

    public virtual Case Case { get; set; } = null!;
    public virtual User? User { get; set; }
    public required string ObjectName { get; set; }
    public required string VersionId { get; set; }
    public required string Md5Hash { get; set; }
    public required string ShaHash { get; set; }
}