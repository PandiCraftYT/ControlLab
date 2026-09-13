import WebSocket from "ws";

const SERVER_URL = "ws://127.0.0.1:8080";

console.log("Conectando con ControlLab Server...");

const socket = new WebSocket(SERVER_URL);

socket.on("open", () => {
  console.log("✅ Conectado al servidor");

  socket.send(
    JSON.stringify({
      type: "TEST_CLIENT",
      machineId: "PC-TEST",
      message: "Hola ControlLab",
    })
  );
});

socket.on("message", (data) => {
  console.log("📨 Mensaje del servidor:");
  console.log(data.toString());
});

socket.on("close", () => {
  console.log("🔴 Conexión cerrada");
});

socket.on("error", (error) => {
  console.error("❌ Error:", error.message);
});