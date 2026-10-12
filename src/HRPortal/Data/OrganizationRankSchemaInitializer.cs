using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class OrganizationRankSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.OrganizationRankDefinitions', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OrganizationRankDefinitions](
        [Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_OrganizationRankDefinitions] PRIMARY KEY,
        [Title] nvarchar(100) NOT NULL,
        [SortOrder] int NOT NULL CONSTRAINT [DF_OrganizationRankDefinitions_SortOrder] DEFAULT 10,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_OrganizationRankDefinitions_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetime2 NOT NULL CONSTRAINT [DF_OrganizationRankDefinitions_UpdatedAt] DEFAULT SYSUTCDATETIME()
    );
END;

IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'UX_OrganizationRankDefinitions_Title'
    AND object_id=OBJECT_ID(N'dbo.OrganizationRankDefinitions'))
    CREATE UNIQUE INDEX [UX_OrganizationRankDefinitions_Title]
        ON [dbo].[OrganizationRankDefinitions]([Title]);

INSERT INTO dbo.OrganizationRankDefinitions(Title, SortOrder)
SELECT seeds.Title, seeds.SortOrder
FROM (VALUES
    (N'مدیرعامل', 10),
    (N'معاونت', 20),
    (N'مدیریت', 30),
    (N'ریاست', 40),
    (N'سرپرستی', 50)
) AS seeds(Title, SortOrder)
WHERE NOT EXISTS(SELECT 1 FROM dbo.OrganizationRankDefinitions r WHERE r.Title=seeds.Title);

IF OBJECT_ID(N'dbo.OrganizationNodes', N'U') IS NOT NULL
BEGIN
    ;WITH legacyRanks AS
    (
        SELECT DISTINCT NULLIF(LTRIM(RTRIM(RankType)), N'') AS Title
        FROM dbo.OrganizationNodes
    )
    INSERT INTO dbo.OrganizationRankDefinitions(Title, SortOrder)
    SELECT l.Title, 100 + ROW_NUMBER() OVER(ORDER BY l.Title) * 10
    FROM legacyRanks l
    WHERE l.Title IS NOT NULL
      AND NOT EXISTS(SELECT 1 FROM dbo.OrganizationRankDefinitions r WHERE r.Title=l.Title);
END;";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
