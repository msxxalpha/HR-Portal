namespace HRPortal.Models;
public class OrganizationChange
{
 public int Id{get;set;} public int OrganizationStructureRevisionId{get;set;} public string ChangeType{get;set;}=""; public string EntityCode{get;set;}=""; public string Description{get;set;}=""; public DateTime ChangedAt{get;set;}=DateTime.UtcNow; public OrganizationStructureRevision Revision{get;set;}=null!;
}