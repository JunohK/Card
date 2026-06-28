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

// ─────────────────────────────────────────────────────────────
// 재접속 복구 — 네트워크가 끊겼다 자동 재연결(withAutomaticReconnect)되면
// ConnectionId가 새로 발급되어 기존 SignalR 그룹/플레이어 매핑이 끊긴다.
// 현재 입장한 방을 기억해 두고, 재연결 직후 JoinRoom을 다시 호출해
// (1) 그룹 재가입 (2) 서버의 ConnectionId 재매핑(RemapPlayerId)을 트리거한다.
// ─────────────────────────────────────────────────────────────
let activeRoomId: string | null = null;

/** 게임/방 페이지 진입 시 호출 — 재접속 시 자동 재입장할 방을 등록 (null이면 해제). */
export const setActiveRoom = (roomId: string | null): void => {
    activeRoomId = roomId;
};

connection.onreconnected(async () => {
    if (!activeRoomId) return;
    const pwd = sessionStorage.getItem(`room_pwd_${activeRoomId}`);
    try {
        console.log(`[SignalR] 재접속 — 방 ${activeRoomId} 자동 재입장`);
        await connection.invoke("JoinRoom", activeRoomId, pwd && pwd !== "" ? pwd : null);
    } catch (err) {
        console.error("❌ [SignalR] 재접속 후 재입장 실패:", err);
    }
});

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