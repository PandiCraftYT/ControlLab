using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace ControlLab.Manager.Server;

public sealed class ScreenStreamHandler
{
    private readonly Func<string, ScreenCaptureData?> _getStreamFrame;

    private readonly Func<string, bool> _isAgentAuthorized;

    private readonly Func<string, bool> _isAgentOnline;

    private readonly Func<HttpListenerContext, bool> _isAuthenticated;

    public ScreenStreamHandler(
        Func<string, ScreenCaptureData?> getStreamFrame,
        Func<string, bool> isAgentAuthorized,
        Func<string, bool> isAgentOnline,
        Func<HttpListenerContext, bool> isAuthenticated)
    {
        _getStreamFrame =
            getStreamFrame;

        _isAgentAuthorized =
            isAgentAuthorized;

        _isAgentOnline =
            isAgentOnline;

        _isAuthenticated =
            isAuthenticated;
    }

    // =========================================================
    // MANEJAR CONEXIÓN DEL VISOR
    // =========================================================

    public async Task HandleAsync(
        HttpListenerContext context,
        string machineId)
    {
        // =====================================================
        // VALIDAR MACHINE ID
        // =====================================================

        if (
            string.IsNullOrWhiteSpace(
                machineId
            )
        )
        {
            context.Response.StatusCode =
                400;

            context.Response.Close();

            return;
        }
        // =====================================================
        // VALIDAR SESIÓN DEL ADMINISTRADOR
        // =====================================================

        if (!_isAuthenticated(context))
        {
            await SendHttpErrorAsync(
                context,
                401,
                "Sesión no válida o expirada."
            );

            return;
        }
        // =====================================================
        // COMPROBAR AUTORIZACIÓN
        // =====================================================

        if (
            !_isAgentAuthorized(
                machineId
            )
        )
        {
            await SendHttpErrorAsync(
                context,
                403,
                "El equipo no está autorizado."
            );

            return;
        }

        // =====================================================
        // COMPROBAR CONEXIÓN
        // =====================================================

        if (
            !_isAgentOnline(
                machineId
            )
        )
        {
            await SendHttpErrorAsync(
                context,
                409,
                "El equipo no está conectado."
            );

            return;
        }

        // =====================================================
        // ACEPTAR WEBSOCKET
        // =====================================================

        HttpListenerWebSocketContext wsContext;

        try
        {
            wsContext =
                await context.AcceptWebSocketAsync(
                    null
                );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error aceptando visor de {machineId}: " +
                $"{ex.Message}"
            );

            try
            {
                context.Response.StatusCode =
                    500;

                context.Response.Close();
            }
            catch
            {
            }

            return;
        }

        WebSocket socket =
            wsContext.WebSocket;

        Console.WriteLine(
            $"🖥️ Visor conectado: {machineId}"
        );

        string? lastTimestamp =
            null;

        try
        {
            // =================================================
            // TRANSMISIÓN
            // =================================================

            while (
                socket.State ==
                WebSocketState.Open
            )
            {
                // ---------------------------------------------
                // COMPROBAR AUTORIZACIÓN
                // ---------------------------------------------

                if (
                    !_isAgentAuthorized(
                        machineId
                    )
                )
                {
                    await CloseViewerAsync(
                        socket,
                        "Autorización revocada"
                    );

                    break;
                }

                // ---------------------------------------------
                // COMPROBAR AGENT ONLINE
                // ---------------------------------------------

                if (
                    !_isAgentOnline(
                        machineId
                    )
                )
                {
                    await CloseViewerAsync(
                        socket,
                        "Equipo desconectado"
                    );

                    break;
                }

                // ---------------------------------------------
                // OBTENER ÚLTIMO FRAME
                // ---------------------------------------------

                ScreenCaptureData? frame =
                    _getStreamFrame(
                        machineId
                    );

                if (
                    frame != null &&
                    !string.Equals(
                        frame.Timestamp,
                        lastTimestamp,
                        StringComparison.Ordinal
                    )
                )
                {
                    try
                    {
                        byte[] imageBytes =
                            Convert.FromBase64String(
                                frame.Image
                            );

                        if (
                            imageBytes.Length > 0
                        )
                        {
                            await socket.SendAsync(
                                new ArraySegment<byte>(
                                    imageBytes
                                ),
                                WebSocketMessageType.Binary,
                                true,
                                CancellationToken.None
                            );

                            lastTimestamp =
                                frame.Timestamp;
                        }
                    }
                    catch (FormatException)
                    {
                        Console.WriteLine(
                            $"⚠️ Frame inválido para {machineId}"
                        );
                    }
                }

                // ---------------------------------------------
                // ~30 COMPROBACIONES POR SEGUNDO
                // ---------------------------------------------

                await Task.Delay(
                    33
                );
            }
        }
        catch (WebSocketException ex)
        {
            Console.WriteLine(
                $"⚠️ Visor desconectado: " +
                $"{machineId} | {ex.Message}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ Error en visor {machineId}: " +
                $"{ex.Message}"
            );
        }
        finally
        {
            try
            {
                socket.Dispose();
            }
            catch
            {
            }

            Console.WriteLine(
                $"🔴 Visor cerrado: {machineId}"
            );
        }
    }

    // =========================================================
    // CERRAR VISOR
    // =========================================================

    private static async Task CloseViewerAsync(
        WebSocket socket,
        string reason)
    {
        try
        {
            if (
                socket.State ==
                WebSocketState.Open
            )
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    reason,
                    CancellationToken.None
                );
            }
        }
        catch
        {
        }
    }

    // =========================================================
    // ERROR HTTP
    // =========================================================

    private static async Task SendHttpErrorAsync(
        HttpListenerContext context,
        int statusCode,
        string message)
    {
        try
        {
            byte[] bytes =
                Encoding.UTF8.GetBytes(
                    message
                );

            context.Response.StatusCode =
                statusCode;

            context.Response.ContentType =
                "text/plain; charset=utf-8";

            context.Response.ContentLength64 =
                bytes.Length;

            await context.Response.OutputStream.WriteAsync(
                bytes
            );

            context.Response.OutputStream.Close();
        }
        catch
        {
            try
            {
                context.Response.Close();
            }
            catch
            {
            }
        }
    }
}