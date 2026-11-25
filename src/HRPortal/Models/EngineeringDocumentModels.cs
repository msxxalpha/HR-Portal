namespace HRPortal.Models;

public sealed class EngineeringDocumentNode
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public EngineeringDocumentNode? Parent { get; set; }
    public ICollection<EngineeringDocumentNode> Children { get; set; } = new List<EngineeringDocumentNode>();
    public ICollection<EngineeringDocument> Documents { get; set; } = new List<EngineeringDocument>();
}

public sealed class EngineeringDocumentType
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<EngineeringDocument> Documents { get; set; } = new List<EngineeringDocument>();
}

public sealed class EngineeringDocument
{
    public int Id { get; set; }
    public int NodeId { get; set; }
    public int TypeId { get; set; }
    public string Title { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string RevisionLabel { get; set; } = "";
    public DateTime? PublishDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string Keywords { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool IsPublished { get; set; }
    public bool HasBeenPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByName { get; set; } = "";
    public string CreatedByPersonnelNumber { get; set; } = "";
    public EngineeringDocumentNode Node { get; set; } = null!;
    public EngineeringDocumentType Type { get; set; } = null!;
    public ICollection<EngineeringDocumentVersion> Versions { get; set; } = new List<EngineeringDocumentVersion>();
}

public sealed class EngineeringDocumentVersion
{
    public int Id { get; set; }
    public int EngineeringDocumentId { get; set; }
    public int VersionNumber { get; set; }
    public byte[] PdfContent { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "application/pdf";
    public long FileSize { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public string UploadedByName { get; set; } = "";
    public string UploadedByPersonnelNumber { get; set; } = "";
    public string ChangeNote { get; set; } = "";
    public EngineeringDocument Document { get; set; } = null!;
}

public sealed class EngineeringDocumentListItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string TypeTitle { get; set; } = "";
    public string NodeTitle { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string RevisionLabel { get; set; } = "";
    public DateTime PublishDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public int VersionNumber { get; set; }
    public string OriginalFileName { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Keywords { get; set; } = "";
}

public sealed class EngineeringDocumentListViewModel
{
    public List<EngineeringDocumentListItem> Documents { get; set; } = [];
    public string Search { get; set; } = "";
    public int? NodeId { get; set; }
    public int? TypeId { get; set; }
    public string FromDate { get; set; } = "";
    public string ToDate { get; set; } = "";
    public string Sort { get; set; } = "published";
    public bool Descending { get; set; } = true;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int PageCount { get; set; }
}

public sealed class EngineeringDocumentPublishItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string TypeTitle { get; set; } = "";
    public string NodeTitle { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string RevisionLabel { get; set; } = "";
    public int VersionNumber { get; set; }
    public string OriginalFileName { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; }
    public string UploadedByName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Keywords { get; set; } = "";
}

public sealed class EngineeringDocumentPublishViewModel
{
    public List<EngineeringDocumentPublishItem> Documents { get; set; } = [];
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int PageCount { get; set; }
}
