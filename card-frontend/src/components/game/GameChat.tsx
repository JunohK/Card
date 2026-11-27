// 게임 화면 우측 하단 라이브챗 — 방 채팅(SendRoomChat / ReceiveMessage), 접기·펼치기, 좌·상단 드래그로 크기 조절.
// 크기는 localStorage(chatSize)에 저장. 소리 버튼은 채팅 바 왼쪽에 붙인다.
import { useEffect, useRef, useState } from "react";
import { connection } from "../../signalr/connection";
import { setSoundTogglePlacement } from "../soundTogglePlacement";
import "../../css/GameChat.css";

type ChatSize = { chatWidth: number; chatHeight: number };
type ChatMessage = { user: string; text: string };
type ResizeDirection = "left" | "top" | "corner";

const CHAT_SIZE_KEY = "chatSize";
const DEFAULT_SIZE: ChatSize = { chatWidth: 260, chatHeight: 320 };

const readSavedSize = (): ChatSize => {
    const saved = localStorage.getItem(CHAT_SIZE_KEY);
    return saved ? (JSON.parse(saved) as ChatSize) : DEFAULT_SIZE;
};

type GameChatProps = {
    roomId: string | undefined;
    myName: string;
};

export default function GameChat({ roomId, myName }: GameChatProps) {
    const [messages, setMessages] = useState<ChatMessage[]>([]);
    const [input, setInput] = useState("");
    const [connected] = useState(connection.state === "Connected");
    const [isMinimized, setIsMinimized] = useState(true);
    const [hasNewMessage, setHasNewMessage] = useState(false);
    // 저장된 크기로 시작 — 첫 렌더부터 저장값을 써야 기본값이 저장값을 덮어쓰지 않는다
    const [chatWidth, setChatWidth] = useState(() => readSavedSize().chatWidth);
    const [chatHeight, setChatHeight] = useState(() => readSavedSize().chatHeight);

    const bottomRef = useRef<HTMLDivElement>(null);
    const resizeDirection = useRef<ResizeDirection | null>(null);
    // 접힘 상태를 ref로도 유지 — 수신 구독이 접기/펼치기마다 다시 걸리지 않도록
    const isMinimizedRef = useRef(isMinimized);
    useEffect(() => { isMinimizedRef.current = isMinimized; }, [isMinimized]);

    useEffect(() => {
        const onReceiveMessage = (user: string, text: string) => {
            setMessages(prev => [...prev, { user, text }]);
            if (isMinimizedRef.current) setHasNewMessage(true);
        };
        connection.on("ReceiveMessage", onReceiveMessage);
        return () => connection.off("ReceiveMessage", onReceiveMessage);
    }, []);

    // 드래그 크기 조절 — 핸들에서 누른 방향으로 창 밖 마우스 위치까지 따라감
    useEffect(() => {
        const handleMouseMove = (e: MouseEvent) => {
            if (!resizeDirection.current) return;
            if (resizeDirection.current === "left" || resizeDirection.current === "corner") {
                setChatWidth(prev => Math.max(200, Math.min(prev + (window.innerWidth - e.clientX - prev - 20), 600)));
            }
            if (resizeDirection.current === "top" || resizeDirection.current === "corner") {
                setChatHeight(prev => Math.max(150, Math.min(prev + (window.innerHeight - e.clientY - prev - 20), 700)));
            }
        };
        const handleMouseUp = () => { resizeDirection.current = null; };

        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);
        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, []);

    useEffect(() => {
        localStorage.setItem(CHAT_SIZE_KEY, JSON.stringify({ chatWidth, chatHeight }));
    }, [chatWidth, chatHeight]);

    useEffect(() => {
        bottomRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [messages]);

    // 소리 버튼을 채팅 바 왼쪽에 배치 — 우측 상단은 기권 버튼 자리.
    // 채팅 바는 bottom 20px / right 20px / 접힌 높이 45px, 버튼은 40px이라 세로 중앙 맞춤
    useEffect(() => {
        setSoundTogglePlacement({ bottom: 22, right: chatWidth + 30 });
        return () => setSoundTogglePlacement(null);
    }, [chatWidth]);

    const sendMessage = async () => {
        if (!input.trim() || connection.state !== "Connected") return;
        await connection.invoke("SendRoomChat", roomId, input);
        setInput("");
    };

    const toggleMinimized = () => {
        setIsMinimized(!isMinimized);
        setHasNewMessage(false);
    };

    const showAlert = hasNewMessage && isMinimized;

    return (
        <div className="game-chat" style={{ width: `${chatWidth}px`, height: isMinimized ? "45px" : `${chatHeight}px` }}>
            {!isMinimized && (
                <>
                    <div className="game-chat-handle left" onMouseDown={() => { resizeDirection.current = "left"; }} />
                    <div className="game-chat-handle top" onMouseDown={() => { resizeDirection.current = "top"; }} />
                    <div className="game-chat-handle corner" onMouseDown={() => { resizeDirection.current = "corner"; }} />
                </>
            )}

            <div className={`game-chat-header ${showAlert ? "alert" : ""} ${isMinimized ? "minimized" : ""}`} onClick={toggleMinimized}>
                <div className="game-chat-title-row">
                    <div className={`game-chat-dot ${connected ? "online" : ""}`} />
                    <span className="game-chat-title">{showAlert ? "NEW MESSAGE!" : "LIVE CHAT"}</span>
                </div>
                <span className="game-chat-arrow">{isMinimized ? "▲" : "▼"}</span>
            </div>

            {!isMinimized && (
                <div className="game-chat-messages">
                    {messages.map((m, i) => {
                        const isMe = !!myName && m.user.trim() === myName.trim();
                        return (
                            <div key={i} className={`game-chat-msg ${isMe ? "mine" : ""}`}>
                                {!isMe && <div className="game-chat-sender">{m.user}</div>}
                                <div className="game-chat-bubble">{m.text}</div>
                            </div>
                        );
                    })}
                    <div ref={bottomRef} />
                </div>
            )}

            {!isMinimized && (
                <div className="game-chat-input-row">
                    <input
                        className="game-chat-input"
                        placeholder="메시지 입력..."
                        value={input}
                        onChange={e => setInput(e.target.value)}
                        onKeyDown={e => e.key === "Enter" && sendMessage()}
                    />
                    <button className="game-chat-send" onClick={sendMessage} disabled={!connected}>전송</button>
                </div>
            )}
        </div>
    );
}
