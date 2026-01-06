import * as signalR from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

export const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5101/gamehub", {
        accessTokenFactory: () => authStorage.getToken() || ""
    })
    .withAutomaticReconnect()
    .build();