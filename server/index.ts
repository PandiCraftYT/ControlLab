import http from "http";
import { WebSocketServer, WebSocket } from "ws";

const PORT = 8080;

interface Agent {
  machineId: string;
  hostname: string;
  platform: string;
  agentVersion: string;
  connectedAt: string;
  lastHeartbeat: string;
  socket: WebSocket;
}

interface ScreenCapture {
  machineId: string;
  image: string;
  timestamp: string;
}

const agents = new Map<string, Agent>();

const screenCaptures = new Map<
  string,
  ScreenCapture
>();

// ==========================================
// RESPUESTAS HTTP
// ==========================================

function sendJson(
  res: http.ServerResponse,
  statusCode: number,
  data: unknown
) {
  res.writeHead(statusCode, {
    "Content-Type":
      "application/json; charset=utf-8",
    "Access-Control-Allow-Origin": "*",
  });

  res.end(
    JSON.stringify(data, null, 2)
  );
}

// ==========================================
// SERVIDOR HTTP
// ==========================================

const httpServer =
  http.createServer(
    (req, res) => {

      // ==========================================
      // LISTAR AGENTES
      // ==========================================

      if (
        req.method === "GET" &&
        req.url === "/api/agents"
      ) {
        const result =
          Array.from(
            agents.values()
          ).map((agent) => ({
            machineId:
              agent.machineId,

            hostname:
              agent.hostname,

            platform:
              agent.platform,

            agentVersion:
              agent.agentVersion,

            connectedAt:
              agent.connectedAt,

            lastHeartbeat:
              agent.lastHeartbeat,

            status:
              agent.socket.readyState ===
              WebSocket.OPEN
                ? "online"
                : "offline",
          }));

        sendJson(
          res,
          200,
          {
            total: result.length,
            agents: result,
          }
        );

        return;
      }

      // ==========================================
      // SOLICITAR PING
      // ==========================================

      if (
        req.method === "POST" &&
        req.url?.startsWith(
          "/api/agents/"
        ) &&
        req.url.endsWith("/ping")
      ) {
        const parts =
          req.url.split("/");

        const machineId =
          decodeURIComponent(
            parts[3] ?? ""
          );

        if (!machineId) {
          sendJson(
            res,
            400,
            {
              success: false,
              message:
                "MachineId no especificado",
            }
          );

          return;
        }

        const agent =
          agents.get(machineId);

        if (!agent) {
          sendJson(
            res,
            404,
            {
              success: false,
              message:
                `El equipo ${machineId} no está conectado`,
            }
          );

          return;
        }

        if (
          agent.socket.readyState !==
          WebSocket.OPEN
        ) {
          sendJson(
            res,
            409,
            {
              success: false,
              message:
                `El equipo ${machineId} está desconectado`,
            }
          );

          return;
        }

        const commandId =
          `${machineId}-${Date.now()}`;

        const command = {
          type: "COMMAND",
          command: "PING",
          machineId,
          commandId,
          timestamp:
            new Date().toISOString(),
        };

        try {
          agent.socket.send(
            JSON.stringify(command)
          );

          console.log("");
          console.log(
            "📤 COMANDO PING ENVIADO"
          );
          console.log(
            `   Equipo: ${machineId}`
          );
          console.log("");

          sendJson(
            res,
            200,
            {
              success: true,
              message:
                `PING enviado a ${machineId}`,
              commandId,
            }
          );
        }
        catch (error) {
          console.error(
            "❌ Error enviando PING:",
            error
          );

          sendJson(
            res,
            500,
            {
              success: false,
              message:
                "No se pudo enviar el comando",
            }
          );
        }

        return;
      }

      // ==========================================
      // SOLICITAR CAPTURA DE PANTALLA
      // ==========================================

      if (
        req.method === "POST" &&
        req.url?.startsWith(
          "/api/agents/"
        ) &&
        req.url.endsWith("/screen")
      ) {
        const parts =
          req.url.split("/");

        const machineId =
          decodeURIComponent(
            parts[3] ?? ""
          );

        if (!machineId) {
          sendJson(
            res,
            400,
            {
              success: false,
              message:
                "MachineId no especificado",
            }
          );

          return;
        }

        const agent =
          agents.get(machineId);

        if (!agent) {
          sendJson(
            res,
            404,
            {
              success: false,
              message:
                `El equipo ${machineId} no está conectado`,
            }
          );

          return;
        }

        if (
          agent.socket.readyState !==
          WebSocket.OPEN
        ) {
          sendJson(
            res,
            409,
            {
              success: false,
              message:
                `El equipo ${machineId} está desconectado`,
            }
          );

          return;
        }

        const commandId =
          `${machineId}-screen-${Date.now()}`;

        // Eliminamos una captura anterior
        screenCaptures.delete(
          machineId
        );

        const command = {
          type: "COMMAND",
          command: "SCREEN_CAPTURE",
          machineId,
          commandId,
          timestamp:
            new Date().toISOString(),
        };

        try {
          agent.socket.send(
            JSON.stringify(command)
          );

          console.log("");
          console.log(
            "📸 SOLICITUD DE CAPTURA ENVIADA"
          );
          console.log(
            `   Equipo: ${machineId}`
          );
          console.log(
            `   ID:     ${commandId}`
          );
          console.log("");

          sendJson(
            res,
            200,
            {
              success: true,
              message:
                `Captura solicitada a ${machineId}`,
              commandId,
            }
          );
        }
        catch (error) {
          console.error(
            "❌ Error solicitando captura:",
            error
          );

          sendJson(
            res,
            500,
            {
              success: false,
              message:
                "No se pudo solicitar la captura",
            }
          );
        }

        return;
      }

      // ==========================================
      // OBTENER ÚLTIMA CAPTURA
      // ==========================================

      if (
        req.method === "GET" &&
        req.url?.startsWith(
          "/api/agents/"
        ) &&
        req.url.endsWith("/screen")
      ) {
        const parts =
          req.url.split("/");

        const machineId =
          decodeURIComponent(
            parts[3] ?? ""
          );

        const capture =
          screenCaptures.get(
            machineId
          );

        if (!capture) {
          sendJson(
            res,
            404,
            {
              success: false,
              message:
                `No hay una captura disponible para ${machineId}`,
            }
          );

          return;
        }

        try {
          const imageBuffer =
            Buffer.from(
              capture.image,
              "base64"
            );

          res.writeHead(
            200,
            {
              "Content-Type":
                "image/jpeg",

              "Content-Length":
                imageBuffer.length,

              "Cache-Control":
                "no-store",

              "Access-Control-Allow-Origin":
                "*",
            }
          );

          res.end(
            imageBuffer
          );
        }
        catch (error) {
          console.error(
            "❌ Error devolviendo imagen:",
            error
          );

          sendJson(
            res,
            500,
            {
              success: false,
              message:
                "No se pudo devolver la captura",
            }
          );
        }

        return;
      }

      // ==========================================
      // INFORMACIÓN SOBRE CAPTURA
      // ==========================================

      if (
        req.method === "GET" &&
        req.url?.startsWith(
          "/api/agents/"
        ) &&
        req.url.endsWith("/screen/info")
      ) {
        const parts =
          req.url.split("/");

        const machineId =
          decodeURIComponent(
            parts[3] ?? ""
          );

        const capture =
          screenCaptures.get(
            machineId
          );

        if (!capture) {
          sendJson(
            res,
            404,
            {
              success: false,
              message:
                "No hay captura disponible",
            }
          );

          return;
        }

        sendJson(
          res,
          200,
          {
            success: true,
            machineId:
              capture.machineId,
            timestamp:
              capture.timestamp,
          }
        );

        return;
      }

      // ==========================================
      // INICIO
      // ==========================================

      if (
        req.method === "GET" &&
        req.url === "/"
      ) {
        res.writeHead(
          200,
          {
            "Content-Type":
              "text/plain; charset=utf-8",
          }
        );

        res.end(
          "ControlLab Server funcionando correctamente."
        );

        return;
      }

      // ==========================================
      // 404
      // ==========================================

      res.writeHead(404);
      res.end("Not Found");
    }
  );

// ==========================================
// WEBSOCKET SERVER
// ==========================================

const wss =
  new WebSocketServer({
    server: httpServer,
  });

console.log(
  "================================="
);

console.log(
  "       ControlLab Server"
);

console.log(
  "================================="
);

console.log(
  `Servidor iniciado en puerto ${PORT}`
);

console.log(
  "Esperando agentes...\n"
);

// ==========================================
// CONEXIONES WEBSOCKET
// ==========================================

wss.on(
  "connection",
  (socket, request) => {

    const remoteAddress =
      request.socket.remoteAddress;

    console.log(
      `🔌 Nueva conexión: ${remoteAddress}`
    );

    // ==========================================
    // RECIBIR MENSAJES
    // ==========================================

    socket.on(
      "message",
      (data) => {

        try {

          const message =
            JSON.parse(
              data.toString()
            );

          // ==========================================
          // REGISTRO DEL AGENTE
          // ==========================================

          if (
            message.type ===
            "AGENT_REGISTER"
          ) {

            if (
              !message.machineId ||
              !message.hostname ||
              !message.platform ||
              !message.agentVersion
            ) {

              console.log(
                "❌ Registro rechazado: datos incompletos"
              );

              return;
            }

            const now =
              new Date().toISOString();

            const previousAgent =
              agents.get(
                message.machineId
              );

            if (
              previousAgent &&
              previousAgent.socket !==
                socket
            ) {

              previousAgent.socket.close();

              console.log(
                `🔄 Conexión anterior reemplazada: ${message.machineId}`
              );
            }

            const agent: Agent = {

              machineId:
                message.machineId,

              hostname:
                message.hostname,

              platform:
                message.platform,

              agentVersion:
                message.agentVersion,

              connectedAt:
                now,

              lastHeartbeat:
                now,

              socket,
            };

            agents.set(
              agent.machineId,
              agent
            );

            console.log("");
            console.log(
              "🖥️ AGENTE REGISTRADO"
            );

            console.log(
              `   ID:       ${agent.machineId}`
            );

            console.log(
              `   Hostname: ${agent.hostname}`
            );

            console.log(
              `   Sistema:  ${agent.platform}`
            );

            console.log(
              `   Versión:  ${agent.agentVersion}`
            );

            console.log(
              `   IP:       ${remoteAddress}`
            );

            console.log(
              `   Total:    ${agents.size}`
            );

            console.log("");

            socket.send(
              JSON.stringify({
                type:
                  "REGISTER_ACCEPTED",

                machineId:
                  agent.machineId,

                message:
                  "Agente registrado correctamente",
              })
            );

            return;
          }

          // ==========================================
          // HEARTBEAT
          // ==========================================

          if (
            message.type ===
            "HEARTBEAT"
          ) {

            const agent =
              agents.get(
                message.machineId
              );

            if (!agent) {

              console.log(
                `⚠️ Heartbeat de equipo no registrado: ${message.machineId}`
              );

              return;
            }

            if (
              agent.socket !==
              socket
            ) {
              return;
            }

            agent.lastHeartbeat =
              new Date().toISOString();

            socket.send(
              JSON.stringify({
                type:
                  "HEARTBEAT_ACK",

                timestamp:
                  agent.lastHeartbeat,
              })
            );

            return;
          }

          // ==========================================
          // RESULTADO DE PING
          // ==========================================

          if (
            message.type ===
            "COMMAND_RESULT"
          ) {

            console.log("");
            console.log(
              "📥 RESULTADO DE COMANDO"
            );

            console.log(
              `   Equipo:  ${message.machineId}`
            );

            console.log(
              `   Comando: ${message.command}`
            );

            console.log(
              `   Éxito:   ${message.success}`
            );

            console.log(
              `   Mensaje: ${message.message}`
            );

            console.log("");

            return;
          }

          // ==========================================
          // RESULTADO DE CAPTURA
          // ==========================================

          if (
            message.type ===
            "SCREEN_CAPTURE_RESULT"
          ) {

            if (
              !message.machineId ||
              !message.image
            ) {

              console.log(
                "❌ Captura rechazada: faltan datos"
              );

              return;
            }

            const agent =
              agents.get(
                message.machineId
              );

            if (
              !agent ||
              agent.socket !==
                socket
            ) {

              console.log(
                `⚠️ Captura de equipo no válido: ${message.machineId}`
              );

              return;
            }

            screenCaptures.set(
              message.machineId,
              {
                machineId:
                  message.machineId,

                image:
                  message.image,

                timestamp:
                  message.timestamp ??
                  new Date().toISOString(),
              }
            );

            console.log("");
            console.log(
              "📸 CAPTURA RECIBIDA"
            );

            console.log(
              `   Equipo: ${message.machineId}`
            );

            console.log(
              `   Tamaño Base64: ${message.image.length} caracteres`
            );

            console.log(
              `   Fecha: ${message.timestamp}`
            );

            console.log("");

            return;
          }

          // ==========================================
          // MENSAJE DESCONOCIDO
          // ==========================================

          console.log(
            `📨 Mensaje desconocido: ${message.type}`
          );

        }
        catch (error) {

          console.error(
            "❌ Mensaje inválido:",
            error
          );

        }

      }
    );

    // ==========================================
    // DESCONEXIÓN
    // ==========================================

    socket.on(
      "close",
      () => {

        for (
          const [
            machineId,
            agent
          ]
          of agents.entries()
        ) {

          if (
            agent.socket ===
            socket
          ) {

            agents.delete(
              machineId
            );

            console.log("");
            console.log(
              `🔴 Agente desconectado: ${machineId}`
            );

            console.log(
              `   Equipos conectados: ${agents.size}`
            );

            console.log("");

            break;
          }
        }
      }
    );

    // ==========================================
    // ERROR
    // ==========================================

    socket.on(
      "error",
      (error) => {

        console.error(
          "❌ Error WebSocket:",
          error.message
        );

      }
    );

  }
);

// ==========================================
// INICIAR SERVIDOR
// ==========================================

httpServer.listen(
  PORT,
  () => {

    console.log(
      `🌐 HTTP API: http://localhost:${PORT}/api/agents`
    );

    console.log(
      `🏓 API PING: POST /api/agents/{PC}/ping`
    );

    console.log(
      `📸 API SCREEN: POST /api/agents/{PC}/screen`
    );

    console.log(
      `🖥️ API IMAGE: GET /api/agents/{PC}/screen`
    );

  }
);