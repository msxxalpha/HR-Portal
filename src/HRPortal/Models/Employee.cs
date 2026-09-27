namespace HRPortal.Models;
public class Employee
{
 public int Id{get;set;} public string PersonnelNumber{get;set;}=""; public string NationalId{get;set;}=""; public string FirstName{get;set;}=""; public string LastName{get;set;}=""; public string Mobile{get;set;}=""; public string Gender{get;set;}="";
 public int? OrganizationUnitId{get;set;} public int? OrganizationDepartmentId{get;set;} public int? OrganizationSectionId{get;set;} public string? PositionTitle{get;set;} public string? EmploymentType{get;set;} public string? Email{get;set;} public string? FatherName{get;set;} public string Status{get;set;}="فعال"; public bool IsSystemUser{get;set;}=true; public bool IsSystemAdministrator{get;set;}
 public DateTime CreatedAt{get;set;}=DateTime.UtcNow; public DateTime UpdatedAt{get;set;}=DateTime.UtcNow;
 public OrganizationNode? OrganizationUnit{get;set;} public OrganizationNode? OrganizationDepartment{get;set;} public OrganizationNode? OrganizationSection{get;set;}
}