import WebSocket from "ws";
import os from "os";

const SERVER_URL = "ws://127.0.0.1:8080";

const MACHINE_ID = "PC-02";

console.log("=================================");
console.log("       ControlLab Agent");
console.log("=================================");
console.log(`Equipo: ${MACHINE_ID}`);
console.log(`Servidor: ${SERVER_URL}`);
console.log("");

const socket = new WebSocket(SERVER_URL);

socket.on("open", () => {
  console.log("🟢 Conectado al servidor");

  socket.send(
    JSON.stringify({
      type: "AGENT_REGISTER",
      machineId: MACHINE_ID,
      hostname: os.hostname(),
      platform: os.platform(),
      agentVersion: "1.0.0",
    })
  );

  console.log("📡 Registro enviado");

  // Heartbeat cada 10 segundos
  setInterval(() => {
    if (socket.readyState === WebSocket.OPEN) {
      socket.send(
        JSON.stringify({
          type: "HEARTBEAT",
          machineId: MACHINE_ID,
        })
      );
    }
  }, 10000);
});

socket.on("message", (data) => {
  try {
    const message = JSON.parse(data.toString());

    if (message.type === "REGISTER_ACCEPTED") {
      console.log(`✅ Registro aceptado: ${message.machineId}`);
    }

    if (message.type === "HEARTBEAT_ACK") {
      console.log("❤️ Heartbeat confirmado");
    }
  } catch {
    console.log("📨 Mensaje:", data.toString());
  }
});

socket.on("close", () => {
  console.log("🔴 Servidor desconectado");
});

socket.on("error", (error) => {
  console.error("❌ Error:", error.message);
});