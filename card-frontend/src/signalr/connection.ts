import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

// ⭐ [주소 수정] ngrok 터미널에 표시된 https 주소를 복사해서 여기에 넣으세요.
// 주의: 주소 끝에 /gamehub를 반드시 붙여야 합니다.
const NGROK_URL = "https://astrally-propitiative-donette.ngrok-free.dev";

export const connection = new HubConnectionBuilder()
    .withUrl(NGROK_URL, {
        accessTokenFactory: () => authStorage.getToken() ?? ""
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();

/**
 * 안전하게 연결을 시작하고 보장하는 함수
 */
export const ensureConnection = async (): Promise<boolean> => {
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
        console.log("SignalR 연결 성공 (via ngrok)");
        return true;
    } catch (err) {
        console.error("SignalR 연결 시도 중 에러:", err);
        return false;
    }
};