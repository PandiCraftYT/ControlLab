using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ControlLab.Agent;

public static class ManagerDiscovery
{
    private const int DiscoveryPort = 45678;

    public static async Task<string?> FindManagerAsync(
        int timeoutMilliseconds = 5000)
    {
        using UdpClient udp = new();

        udp.EnableBroadcast = true;

        udp.Client.SetSocketOption(
            SocketOptionLevel.Socket,
            SocketOptionName.Broadcast,
            true
        );

        udp.Client.Bind(
            new IPEndPoint(IPAddress.Any, 0)
        );

        byte[] message =
            Encoding.UTF8.GetBytes(
                "CONTROLLAB_DISCOVER"
            );

        IPEndPoint broadcastEndpoint =
            new(
                IPAddress.Broadcast,
                DiscoveryPort
            );

        await udp.SendAsync(
            message,
            message.Length,
            broadcastEndpoint
        );

        Console.WriteLine(
            "📡 Buscando ControlLab Manager en la red..."
        );

        using CancellationTokenSource cts =
            new(timeoutMilliseconds);

        try
        {
            while (true)
            {
                UdpReceiveResult result =
                    await udp.ReceiveAsync(
                        cts.Token
                    );

                string response =
                    Encoding.UTF8.GetString(
                        result.Buffer
                    );

                if (
                    response.StartsWith(
                        "CONTROLLAB_MANAGER|",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string address =
                        response[
                            "CONTROLLAB_MANAGER|".Length..
                        ];

                    Console.WriteLine(
                        $"🟢 Manager encontrado: {address}"
                    );

                    return address;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                "⚠️ No se encontró un Manager."
            );

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error durante descubrimiento: {ex.Message}"
            );

            return null;
        }
    }
}