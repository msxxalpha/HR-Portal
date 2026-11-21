using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRPortal.Services;

public sealed class ContractRenewalEvaluationTimeoutService(
    IServiceScopeFactory scopeFactory,
    ILogger<ContractRenewalEvaluationTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var workflow = scope.ServiceProvider.GetRequiredService<WorkflowService>();
                var advanced = await workflow.AutoAdvanceExpiredContractRenewalEvaluationStepsAsync(stoppingToken);
                if (advanced > 0)
                    logger.LogInformation("Automatically advanced {Count} overdue contract-renewal evaluation stage(s).", advanced);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Automatic contract-renewal evaluation timeout processing failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
