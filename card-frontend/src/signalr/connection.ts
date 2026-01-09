import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

export const connection = new HubConnectionBuilder()
    .withUrl("http://localhost:5101/gamehub", {
        accessTokenFactory: () => authStorage.getToken() ?? ""
    })
    .withAutomaticReconnect()
    // .configureLogging(LogLevel.Information)
    .build();

connection.onreconnected(() => {
    console.log("SignalR reconnected");
    connection.invoke("RequestRoomList").catch(console.error);
});