import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";
import { startConnection,joinRoom } from "./signalr/gameHub";

startConnection();

// 버튼 클릭 시 룸 입장
document.getElementById("joinBtn")?.addEventListener("click", () => {
  const roomId = "room1";
  const playerName = "jun";
  joinRoom(roomId, playerName);
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
