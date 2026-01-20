import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

export const connection = new HubConnectionBuilder()
    .withUrl("http://localhost:5101/gamehub", {
        accessTokenFactory: () => authStorage.getToken() ?? ""
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();

/**
 * 안전하게 연결을 시작하고 보장하는 함수
 */
export const ensureConnection = async (): Promise<boolean> => {
    // Enum 대신 문자열 값을 직접 체크하여 타입 에러 방지
    const currentState = connection.state;

    if (currentState === "Connected") {
        return true;
    }

    if (currentState === "Connecting" || currentState === "Reconnecting") {
        let attempts = 0;
        while (connection.state !== "Connected" && attempts < 25) {
            await new Promise(resolve => setTimeout(resolve, 100));
            attempts++;
        }
        return connection.state === "Connected";
    }

    try {
        await connection.start();
        console.log("SignalR 연결 성공");
        return true;
    } catch (err) {
        console.error("SignalR 연결 시도 중 에러:", err);
        return false;
    }
};