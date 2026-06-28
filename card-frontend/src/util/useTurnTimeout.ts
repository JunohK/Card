import { useEffect, useRef, useState } from "react";
import { connection } from "../signalr/connection";

/**
 * 턴 타임아웃 카운트다운 훅 (모든 게임 모드 공용).
 * - currentTurnId가 바뀔 때마다 timeoutSec로 리셋하고 1초씩 감소.
 * - 0이 되면 ForceTurnTimeout(roomId, currentTurnId)를 1회 호출 (현재 턴당 1회).
 * - 모든 클라이언트가 호출해도 서버가 "현재 턴 + 마감시각"을 검증/중복처리하므로 안전.
 *   (현재 턴 플레이어가 접속이 끊겨도 다른 클라가 호출해 진행됨)
 * - enabled=false면 비활성(표시 없음). pending 응답 대기 등에서 끄는 용도.
 * @returns 남은 초 (비활성이면 null)
 */
export function useTurnTimeout(
    roomId: string | undefined,
    currentTurnId: string | null | undefined,
    enabled: boolean,
    timeoutSec: number
): number | null {
    const [secLeft, setSecLeft] = useState<number | null>(null);
    const firedRef = useRef<string | null>(null);

    useEffect(() => {
        if (!enabled || !roomId || !currentTurnId) {
            setSecLeft(null);
            return;
        }
        const turnKey = currentTurnId;
        let remaining = timeoutSec;
        setSecLeft(remaining);
        const id = setInterval(() => {
            remaining -= 1;
            setSecLeft(remaining);
            if (remaining <= 0) {
                clearInterval(id);
                if (firedRef.current !== turnKey) {
                    firedRef.current = turnKey;
                    connection.invoke("ForceTurnTimeout", roomId, currentTurnId).catch(() => { });
                }
            }
        }, 1000);
        return () => clearInterval(id);
    }, [roomId, currentTurnId, enabled, timeoutSec]);

    return secLeft;
}
