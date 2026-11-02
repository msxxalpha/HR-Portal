using System.ComponentModel.DataAnnotations;
namespace HRPortal.Models;
public class Suggestion {
 public int Id{get;set;} [Required,MaxLength(30)] public string SuggestionNumber{get;set;}="";
 [Required,MaxLength(200)] public string Title{get;set;}=""; [Required,MaxLength(80)] public string Category{get;set;}="";
 [MaxLength(100)] public string? RelatedArea{get;set;} [Required,MaxLength(500)] public string ShortDescription{get;set;}="";
 [Required] public string ProblemDescription{get;set;}=""; [Required] public string ProposedSolution{get;set;}="";
 public string BenefitsJson{get;set;}="[]"; [MaxLength(1000)] public string? BenefitDetails{get;set;}
 [MaxLength(100)] public string? ImplementationScope{get;set;} public string? ImplementationMethod{get;set;}
 public string ResourceTypesJson{get;set;}="[]"; [MaxLength(1500)] public string? ResourceDetails{get;set;}
 [Required,MaxLength(20)] public string Status{get;set;}="Draft"; public int ProposerEmployeeId{get;set;}
 public DateTime CreatedAtUtc{get;set;}=DateTime.UtcNow; public DateTime UpdatedAtUtc{get;set;}=DateTime.UtcNow; public DateTime? SubmittedAtUtc{get;set;}
 public ICollection<SuggestionMember> Members{get;set;}=new List<SuggestionMember>(); public ICollection<SuggestionAttachment> Attachments{get;set;}=new List<SuggestionAttachment>();
}
public class SuggestionMember { public int Id{get;set;} public int SuggestionId{get;set;} public Suggestion? Suggestion{get;set;} public int EmployeeId{get;set;} public string EmployeeNameSnapshot{get;set;}=""; public string PersonnelNumberSnapshot{get;set;}=""; public DateTime AddedAtUtc{get;set;}=DateTime.UtcNow; }
public class SuggestionAttachment { public int Id{get;set;} public int SuggestionId{get;set;} public Suggestion? Suggestion{get;set;} [Required,MaxLength(255)] public string OriginalFileName{get;set;}=""; [Required,MaxLength(500)] public string StoredFileName{get;set;}=""; [Required,MaxLength(100)] public string ContentType{get;set;}="application/octet-stream"; public long FileSize{get;set;} public DateTime UploadedAtUtc{get;set;}=DateTime.UtcNow; }
public class SuggestionFormVm {
 public int? Id{get;set;} public int ProposerEmployeeId{get;set;} public string Title{get;set;}=""; public string Category{get;set;}=""; public string? RelatedArea{get;set;} public string ShortDescription{get;set;}="";
 public string ProblemDescription{get;set;}=""; public string ProposedSolution{get;set;}=""; public List<string> Benefits{get;set;}=[]; public string? BenefitDetails{get;set;}
 public string? ImplementationScope{get;set;} public string? ImplementationMethod{get;set;} public List<string> ResourceTypes{get;set;}=[]; public string? ResourceDetails{get;set;}
 public List<int> SelectedEmployeeIds{get;set;}=[]; public List<SuggestionEmployeeOption> EmployeeOptions{get;set;}=[]; public List<SuggestionAttachment> Attachments{get;set;}=[]; public int CurrentStep{get;set;}=1;
}
public class SuggestionEmployeeOption { public int Id{get;set;} public string Name{get;set;}=""; public string PersonnelNumber{get;set;}=""; public string Organization{get;set;}=""; }
