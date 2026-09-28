namespace HRPortal.Models;

public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string Module { get; set; } = "";
}
