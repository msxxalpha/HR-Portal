using System.ComponentModel.DataAnnotations;
namespace HRPortal.Models;
public class Employee
{
 public int Id{get;set;}
 [Required,MaxLength(50)]public string PersonnelNumber{get;set;}="";
 [Required,MaxLength(20)]public string NationalId{get;set;}="";
 [Required,MaxLength(100)]public string FirstName{get;set;}="";
 [Required,MaxLength(150)]public string LastName{get;set;}="";
 [Required,MaxLength(30)]public string Mobile{get;set;}="";
 [Required,MaxLength(20)]public string Gender{get;set;}="";
 public int? OrganizationUnitId{get;set;} public int? OrganizationDepartmentId{get;set;} public int? OrganizationSectionId{get;set;}
 public string? PositionTitle{get;set;} public string? EmploymentType{get;set;} public string? Email{get;set;} public string? FatherName{get;set;} public string Status{get;set;}="فعال"; public bool IsSystemUser{get;set;}=true; public bool IsSystemAdministrator{get;set;}
 public DateTime CreatedAt{get;set;}=DateTime.UtcNow; public DateTime UpdatedAt{get;set;}=DateTime.UtcNow;
 public OrganizationNode? OrganizationUnit{get;set;} public OrganizationNode? OrganizationDepartment{get;set;} public OrganizationNode? OrganizationSection{get;set;}
}