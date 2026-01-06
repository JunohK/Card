import * as signalR from "@microsoft/signalr";

// SignalR 연결 객체 생성
// export const connection = new signalR.HubConnectionBuilder()
//     .withUrl("http://localhost:5101/gamehub")  // 서버 허브 URL
//     .withAutomaticReconnect()
//     .build();

export function createConnection(token: string) {
    return new signalR.HubConnectionBuilder()
        .withUrl("http://localhost:5101/gamehub", {
            accessTokenFactory: () => token
        })
        .withAutomaticReconnect()
        .build();
}