using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Globalization;

namespace GB.Experiments;

/// <summary>Serves local HTTP requests using text returned by an emulated link port.</summary>
public sealed class VirtualSerialTcpAdapter
{
    private const int MaxRequestBytes = 8_192;
    private readonly TcpListener listener;
    private readonly ISerialRequestHandler serialServer;

    public VirtualSerialTcpAdapter(ISerialRequestHandler serialServer, int port)
    {
        this.serialServer = serialServer;
        this.listener = new TcpListener(IPAddress.Loopback, port);
    }

    public int Port => ((IPEndPoint)this.listener.LocalEndpoint).Port;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        this.listener.Start();
        Console.WriteLine($"Game Boy serial web experiment: http://127.0.0.1:{this.Port}/");
        var activeRequests = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (activeRequests.Count >= 128)
                {
                    await Task.WhenAny(activeRequests);
                    activeRequests.RemoveAll(task => task.IsCompleted);
                }
                TcpClient client = await this.listener.AcceptTcpClientAsync(cancellationToken);
                activeRequests.Add(this.HandleSafelyAsync(client, cancellationToken));
                activeRequests.RemoveAll(task => task.IsCompleted);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            this.listener.Stop();
            await Task.WhenAll(activeRequests);
        }
    }

    private async Task HandleSafelyAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                await this.HandleAsync(client, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException)
            {
                // A client may disconnect while its Game Boy request is running.
            }
            catch (SocketException)
            {
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        NetworkStream stream = client.GetStream();
        byte[] request = new byte[MaxRequestBytes];
        int count = 0;
        while (count < request.Length)
        {
            int read = await stream.ReadAsync(request.AsMemory(count, request.Length - count), cancellationToken);
            if (read == 0) break;
            count += read;
            if (request.AsSpan(0, count).IndexOf("\r\n\r\n"u8) >= 0) break;
        }

        string requestLine = Encoding.ASCII.GetString(request, 0, count).Split("\r\n", 2)[0];
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int status;
        string reason;
        string body;
        string contentType = "text/plain; charset=utf-8";
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            status = 400;
            reason = "Bad Request";
            body = "Expected an HTTP/1.x request.\n";
        }
        else if (parts[0] != "GET")
        {
            status = 405;
            reason = "Method Not Allowed";
            body = "Only GET is supported.\n";
        }
        else if (parts[1] != "/")
        {
            if (parts[1].StartsWith("/api/primes/", StringComparison.Ordinal))
            {
                contentType = "application/json; charset=utf-8";
                string indexText = parts[1]["/api/primes/".Length..];
                if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                    || index is < 1 or > DemoRom.MaximumPrimeIndex)
                {
                    status = 400;
                    reason = "Bad Request";
                    body = $"{{\"error\":\"n must be an integer from 1 to {DemoRom.MaximumPrimeIndex}.\"}}\n";
                }
                else
                {
                    try
                    {
                        int prime = await this.serialServer.GetPrimeAsync(index, cancellationToken);
                        status = 200;
                        reason = "OK";
                        body = $"{{\"n\":{index},\"prime\":{prime}}}\n";
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception);
                        status = 502;
                        reason = "Bad Gateway";
                        body = "{\"error\":\"Serial prime calculation failed.\"}\n";
                    }
                }
            }
            else
            {
                status = 404;
                reason = "Not Found";
                body = "No page at that path.\n";
            }
        }
        else
        {
            try
            {
                body = await this.serialServer.GetPageAsync(cancellationToken);
                status = 200;
                reason = "OK";
            }
            catch (Exception exception)
            {
                status = 502;
                reason = "Bad Gateway";
                body = $"Serial protocol failed: {exception.Message}\n";
            }
        }

        byte[] content = Encoding.UTF8.GetBytes(body);
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }

}
