using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartControl.Core.Transport;

/// <summary>
/// Cliente de pruebas que envía un mensaje al receptor local por 127.0.0.1 y lee la
/// respuesta. Sustituye al reloj mientras no exista el transporte real.
/// </summary>
public static class LoopbackTestClient
{
    /// <summary>Envía <paramref name="message"/> seguido de un salto de línea y devuelve la línea de respuesta.</summary>
    public static async Task<string> SendAsync(int port, byte[] message, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port, cts.Token).ConfigureAwait(false);
        var stream = client.GetStream();

        var line = new byte[message.Length + 1];
        message.CopyTo(line, 0);
        line[^1] = (byte)'\n';
        await stream.WriteAsync(line.AsMemory(), cts.Token).ConfigureAwait(false);

        using var response = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cts.Token).ConfigureAwait(false);
            if (read == 0)
                break;
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            response.Write(buffer, 0, newline < 0 ? read : newline);
            if (newline >= 0)
                break;
        }
        if (response.Length == 0)
            throw new IOException("El receptor cerró la conexión sin responder");
        return Encoding.UTF8.GetString(response.ToArray());
    }
}
