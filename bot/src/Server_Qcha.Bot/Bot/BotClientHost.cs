using Microsoft.Extensions.Hosting;

namespace Server.Qcat.Bot;

/// <summary>
/// 机器人接入端点的宿主基类。
///
/// 之所以不用 <see cref="BackgroundService"/>：后者内部的停止令牌是「一次性」的，
/// 一旦 StopAsync 就无法再次 StartAsync（循环会立即退出）。
/// 而本项目的需求是 NapCat 与官方 QQ 两套实现按配置热切换、可反复启停，
/// 因此这里自行管理 CancellationTokenSource 与后台任务。
/// </summary>
public abstract class BotClientHost : IHostedService, IDisposable
{
    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _disposed;

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleLock)
                return _loop is { IsCompleted: false };
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);

            if (_loop is { IsCompleted: false })
                return Task.CompletedTask; // 已在运行

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var token = _cts.Token;
            _loop = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        CancellationTokenSource? cts;

        lock (_lifecycleLock)
        {
            loop = _loop;
            cts = _cts;
            _loop = null;
            _cts = null;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }

        if (loop is not null)
        {
            try
            {
                await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(8), cancellationToken));
            }
            catch (OperationCanceledException)
            {
                // 停止超时也不阻塞宿主关闭
            }
        }

        cts?.Dispose();
    }

    /// <summary>连接主循环。实现方需自行处理重连与退避。</summary>
    protected abstract Task RunLoopAsync(CancellationToken stoppingToken);

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { _cts?.Dispose(); } catch { /* ignore */ }
        _cts = null;
        _loop = null;
        GC.SuppressFinalize(this);
    }
}
