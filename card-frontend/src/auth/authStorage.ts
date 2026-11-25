const TOKEN_KEY = "accessToken";

export const authStorage = {
    getToken(): string | null {
        return localStorage.getItem(TOKEN_KEY);
    },

    setToken(token: string) {
        localStorage.setItem(TOKEN_KEY, token);
    },

    clear() {
        localStorage.removeItem(TOKEN_KEY);
    }
};

// ─────────────────────────────────────────────────────────────
// 안정 플레이어 ID — 서버가 PlayerId를 ConnectionId 대신 닉네임(JWT Name 클레임)으로
// 발급하므로, 클라이언트도 JWT에서 닉네임을 읽어 동일한 키로 비교한다.
// (connection.connectionId는 재접속마다 바뀌어 신원 비교에 쓰면 안 됨)
// ─────────────────────────────────────────────────────────────
const NAME_CLAIM_SHORT = "unique_name"; // .NET JwtSecurityTokenHandler outbound 매핑 키
const NAME_CLAIM_URI = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name";

let cachedToken: string | null = null;
let cachedId: string | null = null;

export function getMyPlayerId(): string | null {
    const token = authStorage.getToken();
    if (!token) return null;
    if (token === cachedToken) return cachedId;

    cachedToken = token;
    cachedId = null;
    try {
        const b64 = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
        // atob는 latin1 — 한글 닉네임 보존을 위해 TextDecoder로 UTF-8 복원
        const json = new TextDecoder().decode(
            Uint8Array.from(atob(b64), c => c.charCodeAt(0))
        );
        const payload: Record<string, unknown> = JSON.parse(json);
        const name = payload[NAME_CLAIM_SHORT] ?? payload[NAME_CLAIM_URI];
        cachedId = typeof name === "string" ? name : null;
    } catch {
        cachedId = null;
    }
    return cachedId;
}