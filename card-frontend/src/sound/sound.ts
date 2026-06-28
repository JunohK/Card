// 효과음 시스템 — HTMLAudioElement 기반, 외부 의존성 없음.
// 파일이 public/sounds/에 없으면 조용히 무시 (catch).

export type SoundName =
    | "place-stone"   // 오목 돌 놓기
    | "play-card"     // 카드 내기
    | "draw-card"     // 카드 뽑기
    | "pung"          // 뻥
    | "win"           // 승리
    | "resign"        // 기권
    | "my-turn"       // 본인 차례 시작
    | "bang"          // 뱅 BANG 발사
    | "heal"          // 뱅 회복
    | "equip"         // 뱅 장착
    | "damage";       // 뱅 데미지/사망

const SOUND_FILES: Record<SoundName, string> = {
    "place-stone": "/sounds/place-stone.mp3",
    "play-card": "/sounds/play-card.mp3",
    "draw-card": "/sounds/draw-card.mp3",
    "pung": "/sounds/pung.mp3",
    "win": "/sounds/win.mp3",
    "resign": "/sounds/resign.mp3",
    "my-turn": "/sounds/my-turn.mp3",
    "bang": "/sounds/bang.mp3",
    "heal": "/sounds/heal.mp3",
    "equip": "/sounds/equip.mp3",
    "damage": "/sounds/damage.mp3"
};

const STORAGE_KEY = "soundMuted";
const TOGGLE_EVENT = "soundtoggle";

export function isMuted(): boolean {
    return localStorage.getItem(STORAGE_KEY) === "1";
}

export function setMuted(muted: boolean): void {
    localStorage.setItem(STORAGE_KEY, muted ? "1" : "0");
    window.dispatchEvent(new CustomEvent(TOGGLE_EVENT, { detail: { muted } }));
}

// 음소거 상태 변경 구독 (SoundToggle 컴포넌트가 사용)
export function subscribeMute(listener: (muted: boolean) => void): () => void {
    const handler = (e: Event) => {
        const detail = (e as CustomEvent<{ muted: boolean }>).detail;
        listener(detail.muted);
    };
    window.addEventListener(TOGGLE_EVENT, handler);
    return () => window.removeEventListener(TOGGLE_EVENT, handler);
}

// 효과음 재생. 음소거면 즉시 무시. 파일 없으면 조용히 catch.
export function playSound(name: SoundName, volume = 0.7): void {
    if (isMuted()) return;
    const src = SOUND_FILES[name];
    if (!src) return;
    try {
        const audio = new Audio(src);
        audio.volume = Math.max(0, Math.min(1, volume));
        // play()는 Promise를 반환. 파일 없거나 자동재생 정책 위반이면 reject — 그냥 삼킴
        audio.play().catch(() => { /* silent fallback */ });
    } catch {
        // Audio 생성 실패도 무시
    }
}
