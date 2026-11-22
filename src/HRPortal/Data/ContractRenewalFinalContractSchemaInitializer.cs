using Microsoft.EntityFrameworkCore;

namespace HRPortal.Data;

public static class ContractRenewalFinalContractSchemaInitializer
{
    public static async Task InitializeAsync(HRPortalDbContext db)
    {
        const string sql = @"
IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'NewContractStartDate') IS NULL
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD [NewContractStartDate] date NULL;

IF OBJECT_ID(N'dbo.ContractRenewalRequests', N'U') IS NOT NULL
AND COL_LENGTH(N'dbo.ContractRenewalRequests', N'NewContractEndDate') IS NULL
    ALTER TABLE [dbo].[ContractRenewalRequests] ADD [NewContractEndDate] date NULL;
";
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
