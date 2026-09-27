namespace HRPortal.Models;
public class OrganizationStructureRevision
{
 public int Id{get;set;} public string RevisionCode{get;set;}=""; public DateTime EffectiveDate{get;set;} public string Title{get;set;}=""; public string? Notes{get;set;} public bool IsFinalized{get;set;} public DateTime? FinalizedAt{get;set;} public DateTime CreatedAt{get;set;}=DateTime.UtcNow;
 public ICollection<OrganizationNode> Nodes{get;set;}=new List<OrganizationNode>(); public ICollection<OrganizationChange> Changes{get;set;}=new List<OrganizationChange>();
}