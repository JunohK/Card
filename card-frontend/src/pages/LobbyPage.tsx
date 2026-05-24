import { useEffect, useState, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import { connection, ensureConnection } from "../signalr/connection";
import "../css/LobbyPage.css";

type RoomSummary = {
    roomId: string;
    title: string;
    playerCount: number;
    isStarted: boolean;
    isLocked: boolean;
}

type MyProfile = {
    name: string;
    wins: number;
    totalGames: number;
    maxScore: number;
    minScore: number;
}

export default function LobbyPage() {
    const navigate = useNavigate();
    const { logout } = useAuth();

    // 스크롤을 제어하기 위한 Ref 생성
    const chatEndRef = useRef<HTMLDivElement>(null);

    const [connected, setConnected] = useState(connection.state === "Connected");
    const [rooms, setRooms] = useState<RoomSummary[]>([]);
    const [messages, setMessages] = useState<string[]>([]);
    const [input, setInput] = useState("");
    
    const [myProfile, setMyProfile] = useState<MyProfile>({ 
        name: "", 
        wins: 0, 
        totalGames: 0, 
        maxScore: 0, 
        minScore: 0 
    });

    const [showCreate, setShowCreate] = useState(false);
    const [showProfile, setShowProfile] = useState(false); 
    const [title, setTitle] = useState("");
    const [password, setPassword] = useState("");

    // 메시지 목록이 변경될 때마다 하단으로 스크롤 이동
    useEffect(() => {
        chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [messages]);

    useEffect(() => {
        let mounted = true;

        const setup = async () => {
            connection.on("ConnectedUser", (data: any) => {
                if (mounted) {
                    if (typeof data === "string") {
                        setMyProfile(prev => ({ ...prev, name: data }));
                    } else {
                        setMyProfile({
                            name: data.name || "",
                            wins: data.wins || 0,
                            totalGames: data.totalGames || 0,
                            maxScore: data.maxScore || 0,
                            minScore: data.minScore || 0
                        });
                    }
                }
            });

            connection.on("ReceiveMessage", (user, message) => {
                if (mounted) setMessages(prev => [...prev, `${user} : ${message}`]);
            });
            connection.on("RoomList", (rooms) => mounted && setRooms(rooms));
            
            connection.on("RoomCreated", (roomId) => {
                if (mounted) {
                    const pending = sessionStorage.getItem("pending_pwd");
                    if (pending) {
                        sessionStorage.setItem(`room_pwd_${roomId}`, pending);
                        sessionStorage.removeItem("pending_pwd");
                    }
                    navigate(`/room/${roomId}`);
                }
            });

            connection.on("JoinRoomSuccess", (roomId) => {
                if (mounted) navigate(`/room/${roomId}`);
            });

            connection.onclose(() => mounted && setConnected(false));
            const isConnected = await ensureConnection();
            if (mounted) {
                setConnected(isConnected);
                if (isConnected) await connection.invoke("EnterLobby");
            }
        };

        setup();

        return () => {
            mounted = false;
            connection.off("ConnectedUser");
            connection.off("ReceiveMessage");
            connection.off("RoomList");
            connection.off("RoomCreated");
            connection.off("JoinRoomSuccess");
        };
    }, [navigate]);

    const winRate = myProfile.totalGames > 0 
        ? ((myProfile.wins / myProfile.totalGames) * 100).toFixed(1) 
        : "0";

    const createRoom = async () => {
        if (!title.trim() || connection.state !== "Connected") return;
        try {
            const pwd = password.trim() === "" ? null : password;
            if (pwd) sessionStorage.setItem("pending_pwd", pwd);
            
            await connection.invoke("CreateRoom", title, pwd);
            setShowCreate(false);
            setTitle("");
            setPassword("");
        } catch (err: any) {
            alert(err?.message ?? "방 생성 실패");
            sessionStorage.removeItem("pending_pwd");
        }
    };

    const handleJoinClick = (room: RoomSummary) => {
        if (room.isLocked) {
            const pwd = prompt("비밀번호를 입력하세요");
            if (pwd !== null) joinRoom(room.roomId, pwd);
        } else {
            joinRoom(room.roomId);
        }
    };

    const joinRoom = async (roomId: string, password?: string) => {
        if (connection.state !== "Connected") return;
        try {
            const pwdToSend = password || "";
            sessionStorage.setItem(`room_pwd_${roomId}`, pwdToSend);
            await connection.invoke("JoinRoom", roomId, pwdToSend === "" ? null : pwdToSend);
        } catch (err: any) {
            alert(err?.message ?? "방 입장 실패");
            sessionStorage.removeItem(`room_pwd_${roomId}`);
        }
    };

    const sendMessage = async () => {
        if (!input.trim() || connection.state !== "Connected") return;
        await connection.invoke("SendChatMessage", input);
        setInput("");
    };

    return (
        <div className="lobby-container">
            <div className="lobby-wrapper">
                
                {/* 메인 섹션: 방 목록 */}
                <div className="lobby-card">
                    <header className="lobby-header">
                        <div className="lobby-title-text">
                            <h1>LOBBY</h1>
                            <button
                                className="profile-btn"
                                onClick={() => setShowProfile(true)}
                                title="내 프로필 보기"
                            >
                                <span className="profile-icon">👤</span>
                                Player: <span className="player-name">{myProfile.name || "Loading..."}</span>
                            </button>
                        </div>
                        <button className="logout-btn" onClick={() => { logout(); navigate("/login"); }}>
                            LOGOUT
                        </button>
                    </header>

                    <section className="room-list-header">
                        <h2 style={{ margin: 0, fontSize: '1.25rem' }}>방 목록</h2>
                        <button 
                            className="create-room-btn" 
                            onClick={() => setShowCreate(true)} 
                            disabled={!connected}
                        >
                            + NEW ROOM
                        </button>
                    </section>

                    <div className="room-grid">
                        {rooms.length === 0 ? (
                            <div style={{ gridColumn: '1/-1', textAlign: 'center', padding: '3rem', color: '#64748b', border: '2px dashed #334155', borderRadius: '1rem' }}>
                                생성된 방이 없습니다.
                            </div>
                        ) : (
                            rooms.map(room => (
                                <div key={room.roomId} className="room-item">
                                    <div>
                                        <div style={{ fontWeight: 'bold', fontSize: '1.1rem', marginBottom: '4px' }}>
                                            {room.title} {room.isLocked && "🔒"}
                                        </div>
                                        <div style={{ fontSize: '0.8rem', color: '#38bdf8', fontWeight: 'bold' }}>
                                            PLAYERS: {room.playerCount}/7
                                        </div>
                                    </div>
                                    <button 
                                        className="join-btn" 
                                        onClick={() => handleJoinClick(room)}
                                        disabled={!connected || room.isStarted}
                                    >
                                        {room.isStarted ? "IN GAME" : "JOIN"}
                                    </button>
                                </div>
                            ))
                        )}
                    </div>
                </div>

                {/* 사이드 섹션: 채팅 */}
                <div className="lobby-card chat-section">
                    <div className="chat-status">
                        <div className="status-dot" style={{ backgroundColor: connected ? '#22c55e' : '#ef4444' }}></div>
                        CHAT
                    </div>
                    
                    <div className="chat-messages">
                        {messages.map((m, i) => {
                            const splitIdx = m.indexOf(" : ");
                            const user = m.substring(0, splitIdx);
                            const msg = m.substring(splitIdx + 3);
                            const isMe = user === myProfile.name;
                            return (
                                <div key={i} className="chat-bubble" style={{ 
                                    alignSelf: isMe ? 'flex-end' : 'flex-start',
                                    backgroundColor: isMe ? '#2563eb' : '#334155',
                                    color: 'white',
                                    borderColor: isMe ? '#3b82f6' : '#475569'
                                }}>
                                    <div style={{ fontSize: '0.7rem', opacity: 0.8, marginBottom: '2px' }}>{user}</div>
                                    <div>{msg}</div>
                                </div>
                            );
                        })}
                        {/* 스크롤 하단을 가리키는 태그 */}
                        <div ref={chatEndRef} />
                    </div>

                    <div className="chat-input-area">
                        <input 
                            className="chat-input" 
                            placeholder="Type a message..." 
                            value={input} 
                            onChange={e => setInput(e.target.value)}
                            onKeyDown={e => e.key === 'Enter' && sendMessage()}
                        />
                        <button 
                            className="join-btn" 
                            style={{ padding: '0.5rem' }} 
                            onClick={sendMessage}
                            disabled={!connected}
                        >
                            SEND
                        </button>
                    </div>
                </div>
            </div>

            {/* 내 정보 프로필 모달 */}
            {showProfile && (
                <div className="modal-overlay" onClick={() => setShowProfile(false)}>
                    <div className="profile-modal" onClick={e => e.stopPropagation()}>
                        <div className="profile-modal-header">
                            <div className="profile-avatar">
                                {(myProfile.name || "?").charAt(0).toUpperCase()}
                            </div>
                            <h2 className="profile-name">{myProfile.name || "Unknown"}</h2>
                        </div>

                        <div className="profile-winrate-section">
                            <div className="profile-winrate-header">
                                <span className="profile-winrate-label">Win Rate</span>
                                <span className="profile-winrate-value">{winRate}%</span>
                            </div>
                            <div className="profile-winrate-bar">
                                <div
                                    className="profile-winrate-fill"
                                    style={{ width: `${Math.min(parseFloat(winRate), 100)}%` }}
                                />
                            </div>
                            <div className="profile-winrate-meta">
                                {myProfile.wins} Wins / {myProfile.totalGames} Games
                            </div>
                        </div>

                        <div className="profile-stats-grid">
                            <div className="profile-stat-card">
                                <div className="profile-stat-icon">🏆</div>
                                <div className="profile-stat-label">Max Score</div>
                                <div className="profile-stat-value high">{myProfile.maxScore}</div>
                            </div>
                            <div className="profile-stat-card">
                                <div className="profile-stat-icon">📉</div>
                                <div className="profile-stat-label">Min Score</div>
                                <div className="profile-stat-value low">
                                    {myProfile.minScore === 999 ? 0 : myProfile.minScore}
                                </div>
                            </div>
                        </div>

                        <button
                            className="logout-btn"
                            style={{ width: '100%', padding: '0.8rem' }}
                            onClick={() => setShowProfile(false)}
                        >
                            CLOSE
                        </button>
                    </div>
                </div>
            )}

            {/* 방 생성 모달 */}
            {showCreate && (
                <div className="modal-overlay">
                    <div className="modal-content">
                        <h2 style={{ color: '#38bdf8', marginTop: 0 }}>Create Room</h2>
                        <div style={{ marginBottom: '1rem' }}>
                            <label style={{ display: 'block', fontSize: '0.8rem', marginBottom: '0.5rem', color: '#94a3b8' }}>TITLE</label>
                            <input 
                                className="chat-input" 
                                style={{ width: '100%', boxSizing: 'border-box' }}
                                value={title}
                                onChange={e => setTitle(e.target.value)}
                            />
                        </div>
                        <div style={{ marginBottom: '2rem' }}>
                            <label style={{ display: 'block', fontSize: '0.8rem', marginBottom: '0.5rem', color: '#94a3b8' }}>PASSWORD (OPTIONAL)</label>
                            <input 
                                className="chat-input" 
                                style={{ width: '100%', boxSizing: 'border-box' }}
                                type="password"
                                value={password}
                                onChange={e => setPassword(e.target.value)}
                            />
                        </div>
                        <div style={{ display: 'flex', gap: '0.5rem' }}>
                            <button className="logout-btn" style={{ flex: 1 }} onClick={() => setShowCreate(false)}>CANCEL</button>
                            <button className="create-room-btn" style={{ flex: 1 }} onClick={createRoom}>CREATE</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}