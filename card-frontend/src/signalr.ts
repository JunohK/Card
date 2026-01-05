import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5101/gamehub") // 🔥 https → http
  .withAutomaticReconnect()
  .build();

await connection.start();
