// 게임 결과 통계 — localStorage 기반 (서버 의존성 없음)
// 각 게임 모드(Cards/Omok/Bang)의 종료 시 push, LobbyPage에서 조회

export type GameMode = "Cards" | "Omok" | "Bang";

export interface GameRecord {
    id: string;
    mode: GameMode;
    endTime: number;       // Date.now()
    winnerName: string;    // "Sheriff팀", "흑돌 (이름)", "이름 (1등)" 등
    detail?: string;       // 부가 정보 (라운드 수, 종료 사유 등)
    durationSec?: number;  // 게임 진행 시간 (선택)
    isMyWin?: boolean;     // 본인이 승자인지 (모드별 승률 계산용)
}

const KEY = "gameHistory";
const MAX_RECORDS = 100;

export function loadHistory(): GameRecord[] {
    try {
        return JSON.parse(localStorage.getItem(KEY) || "[]");
    } catch {
        return [];
    }
}

export function pushHistory(record: Omit<GameRecord, "id" | "endTime">): void {
    const list = loadHistory();
    list.push({
        ...record,
        id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        endTime: Date.now()
    });
    if (list.length > MAX_RECORDS) {
        list.splice(0, list.length - MAX_RECORDS);
    }
    localStorage.setItem(KEY, JSON.stringify(list));
}

export function clearHistory(): void {
    localStorage.removeItem(KEY);
}

// 모드별 카운터 — { Cards: 5, Omok: 3, Bang: 12 } 형태
export function countsByMode(): Record<GameMode, number> {
    const list = loadHistory();
    const out: Record<GameMode, number> = { Cards: 0, Omok: 0, Bang: 0 };
    for (const r of list) out[r.mode]++;
    return out;
}

// 모드별 본인 승리 카운트 (isMyWin === true인 것만)
export function myWinsByMode(): Record<GameMode, number> {
    const list = loadHistory();
    const out: Record<GameMode, number> = { Cards: 0, Omok: 0, Bang: 0 };
    for (const r of list) if (r.isMyWin) out[r.mode]++;
    return out;
}

// 현재 연승 카운터 — 마지막 게임부터 거꾸로 본인 승리가 연속인 수
// (모드 무관, 전체 게임 기준)
export function currentStreak(): number {
    const list = loadHistory();
    let n = 0;
    for (let i = list.length - 1; i >= 0; i--) {
        if (list[i].isMyWin) n++;
        else break;
    }
    return n;
}

// 최장 연승 — 기록 전체 통틀어 isMyWin 연속 최대치
export function longestStreak(): number {
    const list = loadHistory();
    let max = 0, cur = 0;
    for (const r of list) {
        if (r.isMyWin) { cur++; if (cur > max) max = cur; }
        else cur = 0;
    }
    return max;
}

// 모드별 현재 연승 (그 모드의 마지막 게임부터 거꾸로 isMyWin 연속)
export function currentStreakByMode(): Record<GameMode, number> {
    const list = loadHistory();
    const out: Record<GameMode, number> = { Cards: 0, Omok: 0, Bang: 0 };
    for (const mode of ["Cards", "Omok", "Bang"] as GameMode[]) {
        const filtered = list.filter(r => r.mode === mode);
        let n = 0;
        for (let i = filtered.length - 1; i >= 0; i--) {
            if (filtered[i].isMyWin) n++;
            else break;
        }
        out[mode] = n;
    }
    return out;
}

// 모드별 최장 연승
export function longestStreakByMode(): Record<GameMode, number> {
    const list = loadHistory();
    const out: Record<GameMode, number> = { Cards: 0, Omok: 0, Bang: 0 };
    for (const mode of ["Cards", "Omok", "Bang"] as GameMode[]) {
        let max = 0, cur = 0;
        for (const r of list) {
            if (r.mode !== mode) continue;
            if (r.isMyWin) { cur++; if (cur > max) max = cur; }
            else cur = 0;
        }
        out[mode] = max;
    }
    return out;
}
