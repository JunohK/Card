import { useEffect, useState, useSyncExternalStore } from "react";
import { isMuted, setMuted, subscribeMute } from "../sound/sound";
import { getSoundTogglePlacement, subscribeSoundTogglePlacement } from "./soundTogglePlacement";

export default function SoundToggle() {
    const [muted, setMutedState] = useState<boolean>(isMuted());
    // 페이지가 지정한 위치를 따라감 (게임 화면: 우측 하단 라이브챗 왼쪽).
    // 게임 화면에서 새로고침하면 페이지의 effect가 이 컴포넌트의 구독보다 먼저 실행되는데,
    // useSyncExternalStore는 구독 직후 현재 값을 다시 읽으므로 그 순서 문제가 없다.
    const placement = useSyncExternalStore(subscribeSoundTogglePlacement, getSoundTogglePlacement);

    useEffect(() => {
        // 다른 컴포넌트/탭에서 음소거 변경 시 동기화
        return subscribeMute(setMutedState);
    }, []);

    const toggle = () => {
        const next = !muted;
        setMuted(next);
        setMutedState(next);
    };

    return (
        <button
            onClick={toggle}
            title={muted ? "음소거 해제" : "음소거"}
            aria-label={muted ? "음소거 해제" : "음소거"}
            style={{
                position: "fixed",
                ...placement,
                zIndex: 9999,
                width: "40px",
                height: "40px",
                borderRadius: "50%",
                border: "1px solid #334155",
                background: muted ? "#1e293b" : "#0f172a",
                color: muted ? "#94a3b8" : "#38bdf8",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                fontSize: "1.2rem",
                boxShadow: "0 2px 8px rgba(0,0,0,0.4)",
                transition: "all 0.15s ease"
            }}
        >
            {muted ? "🔇" : "🔊"}
        </button>
    );
}
