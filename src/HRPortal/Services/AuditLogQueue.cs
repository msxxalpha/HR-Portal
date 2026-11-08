using System.Threading.Channels;
using HRPortal.Data;
using HRPortal.Models;
using Microsoft.EntityFrameworkCore;

namespace HRPortal.Services;

/// <summary>
/// Non-blocking, bounded audit queue. Business requests never wait for audit SQL writes.
/// </summary>
public sealed class AuditLogQueue
{
    private readonly Channel<AuditLog> _channel = Channel.CreateBounded<AuditLog>(
        new BoundedChannelOptions(8192)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });

    public bool TryEnqueue(AuditLog entry) => _channel.Writer.TryWrite(entry);

    internal ChannelReader<AuditLog> Reader => _channel.Reader;
}

public sealed class AuditLogWriter(
    AuditLogQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AuditLogWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            var batch = new List<AuditLog>(100);
            while (batch.Count < 100 && queue.Reader.TryRead(out var item))
                batch.Add(item);
            if (batch.Count == 0) continue;

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HRPortalDbContext>();
                db.AuditLogs.AddRange(batch);
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not persist {AuditCount} audit records; retrying once.", batch.Count);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    await using var retryScope = scopeFactory.CreateAsyncScope();
                    var retryDb = retryScope.ServiceProvider.GetRequiredService<HRPortalDbContext>();
                    retryDb.AuditLogs.AddRange(batch);
                    await retryDb.SaveChangesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception retryException)
                {
                    logger.LogError(retryException, "Audit records were dropped after retry. Count: {AuditCount}", batch.Count);
                }
            }
        }
    }
}
