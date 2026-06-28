import { useEffect, useState } from "react";
import { isMuted, setMuted, subscribeMute } from "../sound/sound";

export default function SoundToggle() {
    const [muted, setMutedState] = useState<boolean>(isMuted());

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
                top: "0.75rem",
                right: "0.75rem",
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
