import { HubConnectionBuilder, LogLevel, HttpTransportType, HubConnectionState, HubConnection } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

// 1. 공통으로 사용할 주소 생성 함수 (수정됨)
export const getBaseUrl = (): string => {
    // 환경변수가 있다면 우선 사용
    const envUrl = import.meta.env.VITE_SIGNALR_URL;
    
    // 만약 envUrl에 /gamehub가 붙어있다면 제거하고 순수 도메인만 추출 (API 호출용)
    const pureUrl = envUrl ? envUrl.replace("/gamehub", "") : null;

    if (pureUrl) return pureUrl;

    if (typeof window !== "undefined") {
        const hostname = window.location.hostname;
        if (hostname === "localhost" || hostname === "127.0.0.1") {
            return "http://localhost:5101";
        }
        return window.location.origin;
    }
    return "http://localhost:5101";
};

// 2. SignalR용 허브 주소
const getHubUrl = () => `${getBaseUrl()}/gamehub`;

// HubConnection 설정
export const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(getHubUrl(), {
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
    if (connection.state === HubConnectionState.Connected) {
        return true;
    }

    if (connection.state === HubConnectionState.Connecting || connection.state === HubConnectionState.Reconnecting) {
        let attempts = 0;
        while ((connection.state as string) !== "Connected" && attempts < 25) {
            await new Promise(resolve => setTimeout(resolve, 200));
            attempts++;
        }
        return (connection.state as string) === "Connected";
    }

    try {
        const currentUrl = getHubUrl();
        console.log(`[SignalR] 연결 시도 중... 주소: ${currentUrl}`);
        await connection.start();
        console.log("✅ [SignalR] 연결 성공");
        return true;
    } catch (err) {
        console.error("❌ [SignalR] 연결 에러:", err);
        return false;
    }
};