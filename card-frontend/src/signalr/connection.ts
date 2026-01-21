import { HubConnectionBuilder, LogLevel, HttpTransportType, HubConnectionState, HubConnection } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

// 브라우저 환경에 따라 백엔드 주소 자동 감지
const getBaseUrl = (): string => {
    if (typeof window !== "undefined") {
        const hostname = window.location.hostname;
        // 로컬 개발 환경(localhost)이면 5101 포트 사용, 아니면 현재 접속 도메인 사용
        return hostname === "localhost" || hostname === "127.0.0.1" 
            ? "http://localhost:5101/gamehub" 
            : `${window.location.origin}/gamehub`;
    }
    return "http://localhost:5101/gamehub";
};

// HubConnection 타입을 명시적으로 지정하여 빨간 줄 방지
export const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(getBaseUrl(), {
        accessTokenFactory: () => authStorage.getToken() ?? "",
        skipNegotiation: false,
        transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling
    })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Information)
    .build();

/**
 * 안전하게 연결을 시작하고 보장하는 함수
 */
export const ensureConnection = async (): Promise<boolean> => {
    // 1. 이미 연결된 경우 바로 true 반환
    if (connection.state === HubConnectionState.Connected) {
        return true;
    }

    // 2. 연결 중이거나 재연결 중인 경우 잠시 대기
    if (connection.state === "Connecting" || connection.state === "Reconnecting") {
        let attempts = 0;
        // 🔴 'as string'을 붙여서 문자열 비교로 강제 전환
        while ((connection.state as string) !== "Connected" && attempts < 25) {
            await new Promise(resolve => setTimeout(resolve, 200));
            attempts++;
        }
        return (connection.state as string) === "Connected";
    }

    // 3. 연결이 끊겨 있는 경우 새로 시작
    try {
        console.log(`[SignalR] 연결 시도 중... 주소: ${getBaseUrl()}`);
        await connection.start();
        console.log("✅ [SignalR] 연결 성공");
        return true;
    } catch (err) {
        console.error("❌ [SignalR] 연결 에러:", err);
        return false;
    }
};