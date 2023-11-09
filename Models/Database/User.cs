using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LighthouseNotesServer.Models.Database;

public class User : Base
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public required string Id { get; set; }

    public required string JobTitle { get; set; }
    public required string DisplayName { get; set; }
    public required string GivenName { get; set; }
    public required string LastName { get; set; }
    public required string EmailAddress { get; set; }
    public required string ProfilePicture { get; set; }
    public virtual ICollection<Role> Roles { get; set; } = new List<Role>();
    public virtual Organization Organization { get; set; } = null!;
    public virtual UserSettings Settings { get; set; } = null!;
    public virtual IEnumerable<Event> Events { get; } = new List<Event>();
    public virtual ICollection<CaseUser> Cases { get; set; } = new List<CaseUser>();
    public virtual ICollection<Tab> Tabs { get; set; } = new List<Tab>();
}