using System.Net;
using System.Net.Sockets;
using Qulinlin.Network.Http.Abstractions;

namespace Qulinlin.Network.Http;

/// <summary>
/// 处理 TCP 网络连接
/// </summary>
public class NetworkAccessManager:IDisposable
{

    public NetworkAccessManager()
    {
        _resolver = new SystemDnsResolver();
    }

    private NetworkAccessManager(DnsResolver resolver)
    {
        _resolver = resolver;
    }

    private DnsResolver _resolver;

    private bool _isStopped;

    private Dictionary<string,List<Socket>> _connectionPool = new();

    /// <summary>
    /// 查找已存在的 Socket 会话
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NetworkException"></exception>
    public SocketSession? GetSocketSession()
    {
        if (_isStopped) throw new NetworkException("Network access is paused");
        return null;
    }
    /// <summary>
    /// 创建一个 Socket 会话
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NetworkException"></exception>
    public SocketSession? CreateSocketSession()
    {
        throw new NetworkException();
    }
    public SocketSession? GetOrCreateSocketSession()
    {
        throw new NetworkException();
    }
    /// <summary>
    /// 关闭 <see cref="NetworkAccessManager"/>，并等待所有连接完成。
    /// </summary>
    public void Stop()
    {
        if(_isStopped) return;
        _isStopped = true;
    }
    /// <summary>
    /// 启动 NetworkAccessManager
    /// </summary>
    public void Start()
    {
        if(!_isStopped) return;
        _isStopped = false;
    }
    public void Dispose()
    {
        foreach(var socketList in _connectionPool.Values) 
            socketList.(s => s.Dispose())
    }

    private async Task<Socket> _HandleConnectionAsync(string hostName,int port)
    {
        // 解析地址并按地址族分组
        var addresses = (await _resolver.GetAddressAsync(hostName)).ToList();
        var inet6 = addresses.Where(ip => ip.Type == AddressType.Inet6).ToList();
        var inet4 = addresses.Where(ip => ip.Type == AddressType.Inet4).ToList();

        // 索引表示下一个要尝试的地址位置
        var idx6 = 0;
        var idx4 = 0;

        // RFC 8305 (Happy Eyeballs) 的简单实现：
        // 先尝试一个地址（优先 IPv6 若存在），等待短暂延迟（200ms），再尝试另一个地址族的下一个地址。
        // 任意一个连接成功就返回对应的 Socket，失败则继续尝试下一个地址。
        while (true)
        {
            // 如果两个族的地址都耗尽，则不可达
            if (idx6 >= inet6.Count && idx4 >= inet4.Count)
            {
                throw new NetworkException(NetworkErrorCode.Unreachable);
            }

            // 决定本轮优先尝试的族（若有 IPv6 则优先 IPv6，否则优先 IPv4）
            var tryIpv6First = idx6 < inet6.Count;

            Socket? socketFirst = null;
            Task? taskFirst = null;
            CancellationTokenSource? ctsFirst = null;

            Socket? socketSecond = null;
            Task? taskSecond = null;
            CancellationTokenSource? ctsSecond = null;

            if (tryIpv6First)
            {
                if (idx6 < inet6.Count)
                {
                    var addr = inet6[idx6++].Address;
                    socketFirst = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    ctsFirst = new CancellationTokenSource();
                    taskFirst = _ConnectWithCancellationAsync(socketFirst, new IPEndPoint(addr, port), ctsFirst.Token);
                }

                // 等待短暂延迟后再启动另一个族
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }
                catch
                {
                    // ignore; we don't use a global cancellation here
                }

                if (idx4 < inet4.Count)
                {
                    var addr = inet4[idx4++].Address;
                    socketSecond = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    ctsSecond = new CancellationTokenSource();
                    taskSecond = _ConnectWithCancellationAsync(socketSecond, new IPEndPoint(addr, port), ctsSecond.Token);
                }
            }
            else
            {
                if (idx4 < inet4.Count)
                {
                    var addr = inet4[idx4++].Address;
                    socketFirst = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    ctsFirst = new CancellationTokenSource();
                    taskFirst = _ConnectWithCancellationAsync(socketFirst, new IPEndPoint(addr, port), ctsFirst.Token);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }
                catch
                {
                    // ignore
                }

                if (idx6 < inet6.Count)
                {
                    var addr = inet6[idx6++].Address;
                    socketSecond = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    ctsSecond = new CancellationTokenSource();
                    taskSecond = _ConnectWithCancellationAsync(socketSecond, new IPEndPoint(addr, port), ctsSecond.Token);
                }
            }

            // 收集本轮所有正在进行的连接任务
            var running = new List<Task>();
            if (taskFirst != null) running.Add(taskFirst);
            if (taskSecond != null) running.Add(taskSecond);

            if (running.Count == 0)
            {
                // 没有可启动的任务（应该不会到这里，因为前面检查了地址耗尽），继续循环以触发不可达判断
                continue;
            }

            // 等待任意连接完成（成功或失败）
            var completed = await Task.WhenAny(running);

            // 如果完成的是成功的连接，则返回对应的 Socket
            if (completed.IsCompletedSuccessfully)
            {
                if (taskFirst != null && completed == taskFirst)
                {
                    // 取消另一个尝试并释放资源
                    try { ctsSecond?.Cancel(); } catch { }
                    try { socketSecond?.Dispose(); } catch { }
                    ctsFirst?.Dispose();
                    ctsSecond?.Dispose();
                    return socketFirst!;
                }

                if (taskSecond != null && completed == taskSecond)
                {
                    try { ctsFirst?.Cancel(); } catch { }
                    try { socketFirst?.Dispose(); } catch { }
                    ctsFirst?.Dispose();
                    ctsSecond?.Dispose();
                    return socketSecond!;
                }
            }

            // 如果到这里，完成的任务失败了（抛出或被取消），释放对应的 Socket 并继续循环尝试下一个地址
            if (taskFirst != null && taskFirst.IsCompleted)
            {
                try { socketFirst?.Dispose(); } catch { }
                try { ctsFirst?.Dispose(); } catch { }
            }

            if (taskSecond != null && taskSecond.IsCompleted)
            {
                try { socketSecond?.Dispose(); } catch { }
                try { ctsSecond?.Dispose(); } catch { }
            }

            // 继续循环，尝试下一个地址
        }
    }

    // 使用 SocketAsyncEventArgs 对 Connect 进行包装并支持 CancellationToken（通过在取消时关闭 socket）
    private Task _ConnectWithCancellationAsync(Socket socket, EndPoint endpoint, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var args = new SocketAsyncEventArgs { RemoteEndPoint = endpoint };

        void CompletedHandler(object? s, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetException(new SocketException((int)e.SocketError));
            }

            e.Completed -= CompletedHandler;
        }

        args.Completed += CompletedHandler;

        try
        {
            if (!socket.ConnectAsync(args))
            {
                if (args.SocketError == SocketError.Success)
                {
                    args.Completed -= CompletedHandler;
                    args.Dispose();
                    tcs.TrySetResult(true);
                }
                else
                {
                    args.Completed -= CompletedHandler;
                    args.Dispose();
                    tcs.TrySetException(new SocketException((int)args.SocketError));
                }
            }
        }
        catch (Exception ex)
        {
            args.Completed -= CompletedHandler;
            args.Dispose();
            tcs.TrySetException(ex);
        }

        // 在取消时关闭 socket（这 会 触发 CompletedHandler 或导致连接失败），并尝试将任务标记为已取消
        var registration = ct.Register(() =>
        {
            try { socket.Close(); } catch { }
            tcs.TrySetCanceled(ct);
        });

        return tcs.Task.ContinueWith(t =>
        {
            registration.Dispose();
            try { args.Dispose(); } catch { }
            return t;
        }, TaskScheduler.Default).Unwrap();
    }

}