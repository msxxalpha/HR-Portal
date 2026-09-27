namespace HRPortal.Models;
public class OrganizationNode
{
 public int Id{get;set;} public int OrganizationStructureRevisionId{get;set;} public int? ParentId{get;set;} public string Code{get;set;}=""; public string Title{get;set;}=""; public string RankType{get;set;}="مدیریت"; public int SortOrder{get;set;} public bool IsActive{get;set;}=true; public string? Notes{get;set;}
 public OrganizationStructureRevision Revision{get;set;}=null!; public OrganizationNode? Parent{get;set;} public ICollection<OrganizationNode> Children{get;set;}=new List<OrganizationNode>();
}