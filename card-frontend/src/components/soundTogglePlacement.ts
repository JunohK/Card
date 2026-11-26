// 소리 버튼 위치 — 기본은 우측 상단.
// 그 자리에 다른 UI(기권 버튼 등)가 있는 페이지는 setSoundTogglePlacement로 위치를 바꾸고, 떠날 때 null로 되돌린다.
export type SoundTogglePlacement = { top?: number | string; right?: number | string; bottom?: number | string; left?: number | string };

const DEFAULT_PLACEMENT: SoundTogglePlacement = { top: "0.75rem", right: "0.75rem" };
let current: SoundTogglePlacement = DEFAULT_PLACEMENT;
const listeners = new Set<(p: SoundTogglePlacement) => void>();

export function getSoundTogglePlacement(): SoundTogglePlacement {
    return current;
}

/** null이면 기본 위치(우측 상단)로 복귀 */
export function setSoundTogglePlacement(p: SoundTogglePlacement | null): void {
    current = p ?? DEFAULT_PLACEMENT;
    listeners.forEach(l => l(current));
}

export function subscribeSoundTogglePlacement(listener: (p: SoundTogglePlacement) => void): () => void {
    listeners.add(listener);
    return () => { listeners.delete(listener); };
}
