import { HubConnectionBuilder, LogLevel, HttpTransportType, HubConnectionState, HubConnection } from "@microsoft/signalr";
import { authStorage } from "../auth/authStorage";

// 1. 공통으로 사용할 주소 생성 함수 (수정됨)
export const getBaseUrl = (): string => {
    if (typeof window !== "undefined") {
        const hostname = window.location.hostname;

        // 1. 순수 로컬 환경 체크 (최우선)
        // 로컬에서 개발 중일 때는 무조건 로컬 백엔드 포트를 바라보게 합니다.
        if (hostname === "localhost" || hostname === "127.0.0.1") {
            return "http://localhost:5101";
        }

        // 2. 배포 환경(Vercel)인 경우 환경 변수 사용
        const envUrl = import.meta.env.VITE_SIGNALR_URL;
        if (envUrl) {
            // /gamehub가 포함된 경우 제거하여 API 호출용 순수 도메인 반환
            return envUrl.replace("/gamehub", "");
        }

        // 3. 기타 상황 (접속한 도메인 기준)
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