import { useEffect, useState, useRef } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { useAuth } from "../auth/authContext";
import { connection, ensureConnection } from "../signalr/connection";
import { loadHistory, clearHistory, countsByMode, myWinsByMode, currentStreak, longestStreak, currentStreakByMode, longestStreakByMode, type GameRecord } from "../util/gameHistory";
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
    const [searchParams, setSearchParams] = useSearchParams();
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
    const [showStats, setShowStats] = useState(false);
    const [stats, setStats] = useState<GameRecord[]>([]);
    const [statsFilter, setStatsFilter] = useState<'all' | 'Cards' | 'Omok' | 'Bang'>('all');
    const [statsTab, setStatsTab] = useState<'mine' | 'leaderboard'>('mine');
    const [leaderboard, setLeaderboard] = useState<Array<{ nickname: string; wins: number; totalGames: number; maxScore: number; winRate: number }>>([]);

    // 리더보드 fetch (탭 전환 시)
    useEffect(() => {
        if (!showStats || statsTab !== 'leaderboard') return;
        const apiBase = import.meta.env.VITE_API_BASE_URL ?? '';
        fetch(`${apiBase}/api/leaderboard/top?limit=20`)
            .then(r => r.ok ? r.json() : [])
            .then((data) => setLeaderboard(Array.isArray(data) ? data : []))
            .catch(() => setLeaderboard([]));
    }, [showStats, statsTab]);

    // URL ?showStats=1로 진입 시 자동 통계 모달 + 파라미터 제거
    useEffect(() => {
        if (searchParams.get("showStats") === "1") {
            setStats(loadHistory());
            setShowStats(true);
            const next = new URLSearchParams(searchParams);
            next.delete("showStats");
            setSearchParams(next, { replace: true });
        }
    }, [searchParams, setSearchParams]);
    const [title, setTitle] = useState("");
    const [password, setPassword] = useState("");
    const [gameMode, setGameMode] = useState<number>(0); // 0=Cards, 1=Omok

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

            await connection.invoke("CreateRoom", title, pwd, gameMode);
            setShowCreate(false);
            setTitle("");
            setPassword("");
            setGameMode(0);
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
                            <h1>로비</h1>
                            <button
                                className="profile-btn"
                                onClick={() => setShowProfile(true)}
                                title="내 프로필 보기"
                            >
                                <span className="profile-icon">👤</span>
                                플레이어: <span className="player-name">{myProfile.name || "로딩 중..."}</span>
                            </button>
                        </div>
                        <div style={{ display: 'flex', gap: '0.5rem' }}>
                            <button
                                className="logout-btn"
                                style={{ background: '#7c3aed' }}
                                onClick={() => { setStats(loadHistory()); setShowStats(true); }}
                                title="게임 기록 보기"
                            >
                                📊 통계
                            </button>
                            <button className="logout-btn" onClick={() => { logout(); navigate("/login"); }}>
                                로그아웃
                            </button>
                        </div>
                    </header>

                    <section className="room-list-header">
                        <h2 style={{ margin: 0, fontSize: '1.25rem' }}>방 목록</h2>
                        <button
                            className="create-room-btn"
                            onClick={() => setShowCreate(true)}
                            disabled={!connected}
                        >
                            + 새 방 만들기
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
                                            인원: {room.playerCount}/7
                                        </div>
                                    </div>
                                    <button
                                        className="join-btn"
                                        onClick={() => handleJoinClick(room)}
                                        disabled={!connected || room.isStarted}
                                    >
                                        {room.isStarted ? "게임 중" : "입장"}
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
                        채팅
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
                            <h2 className="profile-name">{myProfile.name || "알 수 없음"}</h2>
                        </div>

                        <div className="profile-winrate-section">
                            <div className="profile-winrate-header">
                                <span className="profile-winrate-label">승률</span>
                                <span className="profile-winrate-value">{winRate}%</span>
                            </div>
                            <div className="profile-winrate-bar">
                                <div
                                    className="profile-winrate-fill"
                                    style={{ width: `${Math.min(parseFloat(winRate), 100)}%` }}
                                />
                            </div>
                            <div className="profile-winrate-meta">
                                {myProfile.wins}승 / {myProfile.totalGames}전
                            </div>
                        </div>

                        <div className="profile-stats-grid">
                            <div className="profile-stat-card">
                                <div className="profile-stat-icon">🏆</div>
                                <div className="profile-stat-label">최고 점수</div>
                                <div className="profile-stat-value high">{myProfile.maxScore}</div>
                            </div>
                            <div className="profile-stat-card">
                                <div className="profile-stat-icon">📉</div>
                                <div className="profile-stat-label">최저 점수</div>
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
                            닫기
                        </button>
                    </div>
                </div>
            )}

            {/* 게임 기록 통계 모달 */}
            {showStats && (() => {
                const counts = countsByMode();
                const wins = myWinsByMode();
                const streak = currentStreak();
                const longest = longestStreak();
                const streakMode = currentStreakByMode();
                const longestMode = longestStreakByMode();
                const rate = (mode: 'Cards' | 'Omok' | 'Bang') =>
                    counts[mode] === 0 ? '-' : `${Math.round((wins[mode] / counts[mode]) * 100)}%`;
                return (
                    <div className="modal-overlay" onClick={() => setShowStats(false)}>
                        <div className="modal-content" onClick={e => e.stopPropagation()} style={{ maxWidth: '560px', maxHeight: '80vh', overflowY: 'auto' }}>
                            <h2 style={{ color: '#a78bfa', marginTop: 0 }}>📊 게임 기록</h2>
                            {/* 탭 — 내 기록 / 리더보드 */}
                            <div style={{ display: 'flex', gap: '0.4rem', marginBottom: '0.75rem' }}>
                                <button
                                    onClick={() => setStatsTab('mine')}
                                    style={{
                                        flex: 1, padding: '0.4rem', borderRadius: '0.3rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.85rem',
                                        background: statsTab === 'mine' ? '#7c3aed' : '#0f172a',
                                        border: statsTab === 'mine' ? '1px solid #a78bfa' : '1px solid #334155',
                                        color: statsTab === 'mine' ? 'white' : '#94a3b8'
                                    }}
                                >🏅 내 기록</button>
                                <button
                                    onClick={() => setStatsTab('leaderboard')}
                                    style={{
                                        flex: 1, padding: '0.4rem', borderRadius: '0.3rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.85rem',
                                        background: statsTab === 'leaderboard' ? '#7c3aed' : '#0f172a',
                                        border: statsTab === 'leaderboard' ? '1px solid #a78bfa' : '1px solid #334155',
                                        color: statsTab === 'leaderboard' ? 'white' : '#94a3b8'
                                    }}
                                >🏆 리더보드</button>
                            </div>

                            {statsTab === 'leaderboard' && (
                                <div>
                                    <div style={{ fontSize: '0.85rem', color: '#94a3b8', marginBottom: '0.5rem' }}>
                                        전체 사용자 순위 (승수 내림차순, 상위 20명) — 서버 DB 기반
                                    </div>
                                    {leaderboard.length === 0 ? (
                                        <div style={{ color: '#64748b', padding: '2rem', textAlign: 'center', border: '2px dashed #334155', borderRadius: '0.5rem' }}>
                                            데이터 없음 (백엔드 미연결 또는 통계 없음)
                                        </div>
                                    ) : (
                                        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.3rem' }}>
                                            {leaderboard.map((u, idx) => {
                                                const me = u.nickname === myProfile.name;
                                                const medal = idx === 0 ? '🥇' : idx === 1 ? '🥈' : idx === 2 ? '🥉' : `${idx + 1}.`;
                                                return (
                                                    <div key={u.nickname} style={{
                                                        background: me ? '#14532d' : '#0f172a',
                                                        border: me ? '1px solid #22c55e' : 'none',
                                                        padding: '0.5rem 0.75rem',
                                                        borderRadius: '0.4rem',
                                                        display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.85rem'
                                                    }}>
                                                        <span><b>{medal} {u.nickname}</b> {me && <span style={{ color: '#86efac' }}>(나)</span>}</span>
                                                        <span style={{ color: '#94a3b8', fontSize: '0.75rem' }}>
                                                            {u.wins}승 / {u.totalGames}전 · {Math.round(u.winRate * 100)}% · 최고 {u.maxScore}
                                                        </span>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    )}
                                    <button
                                        onClick={() => setShowStats(false)}
                                        className="logout-btn"
                                        style={{ width: '100%', marginTop: '1rem' }}
                                    >닫기</button>
                                </div>
                            )}

                            {statsTab === 'mine' && (<div>
                            <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '0.75rem' }}>
                                <div style={{ flex: 1, background: streak > 0 ? '#14532d' : '#0f172a', padding: '0.5rem 0.75rem', borderRadius: '0.4rem', textAlign: 'center', border: streak > 0 ? '1px solid #22c55e' : 'none' }}>
                                    <div style={{ fontSize: '0.7rem', color: '#94a3b8' }}>🔥 현재 연승</div>
                                    <div style={{ fontSize: '1.3rem', fontWeight: 'bold', color: streak > 0 ? '#86efac' : '#64748b' }}>{streak}</div>
                                </div>
                                <div style={{ flex: 1, background: '#0f172a', padding: '0.5rem 0.75rem', borderRadius: '0.4rem', textAlign: 'center' }}>
                                    <div style={{ fontSize: '0.7rem', color: '#94a3b8' }}>🏆 최장 연승</div>
                                    <div style={{ fontSize: '1.3rem', fontWeight: 'bold', color: '#fbbf24' }}>{longest}</div>
                                </div>
                            </div>
                            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '0.5rem', marginBottom: '1rem' }}>
                                <div style={{ background: '#0f172a', padding: '0.75rem', borderRadius: '0.5rem', textAlign: 'center' }}>
                                    <div style={{ fontSize: '0.75rem', color: '#94a3b8' }}>🃏 카드</div>
                                    <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: '#38bdf8' }}>{counts.Cards}</div>
                                    <div style={{ fontSize: '0.7rem', color: '#22c55e' }}>승률 {rate('Cards')} ({wins.Cards}승)</div>
                                    <div style={{ fontSize: '0.65rem', color: '#94a3b8' }}>🔥 {streakMode.Cards} / 🏆 {longestMode.Cards}</div>
                                </div>
                                <div style={{ background: '#0f172a', padding: '0.75rem', borderRadius: '0.5rem', textAlign: 'center' }}>
                                    <div style={{ fontSize: '0.75rem', color: '#94a3b8' }}>⚫ 오목</div>
                                    <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: '#fbbf24' }}>{counts.Omok}</div>
                                    <div style={{ fontSize: '0.7rem', color: '#22c55e' }}>승률 {rate('Omok')} ({wins.Omok}승)</div>
                                    <div style={{ fontSize: '0.65rem', color: '#94a3b8' }}>🔥 {streakMode.Omok} / 🏆 {longestMode.Omok}</div>
                                </div>
                                <div style={{ background: '#0f172a', padding: '0.75rem', borderRadius: '0.5rem', textAlign: 'center' }}>
                                    <div style={{ fontSize: '0.75rem', color: '#94a3b8' }}>🤠 뱅</div>
                                    <div style={{ fontSize: '1.5rem', fontWeight: 'bold', color: '#f87171' }}>{counts.Bang}</div>
                                    <div style={{ fontSize: '0.7rem', color: '#22c55e' }}>승률 {rate('Bang')} ({wins.Bang}승)</div>
                                    <div style={{ fontSize: '0.65rem', color: '#94a3b8' }}>🔥 {streakMode.Bang} / 🏆 {longestMode.Bang}</div>
                                </div>
                            </div>
                            {/* 모드 필터 */}
                            <div style={{ display: 'flex', gap: '0.3rem', marginBottom: '0.6rem' }}>
                                {([
                                    { key: 'all', label: '전체', emoji: '📋' },
                                    { key: 'Cards', label: '카드', emoji: '🃏' },
                                    { key: 'Omok', label: '오목', emoji: '⚫' },
                                    { key: 'Bang', label: '뱅', emoji: '🤠' }
                                ] as const).map(f => (
                                    <button
                                        key={f.key}
                                        onClick={() => setStatsFilter(f.key)}
                                        style={{
                                            flex: 1,
                                            padding: '0.35rem 0.5rem',
                                            background: statsFilter === f.key ? '#1e3a5f' : '#0f172a',
                                            border: statsFilter === f.key ? '1px solid #38bdf8' : '1px solid #334155',
                                            color: statsFilter === f.key ? '#38bdf8' : '#94a3b8',
                                            borderRadius: '0.3rem',
                                            cursor: 'pointer',
                                            fontSize: '0.75rem',
                                            fontWeight: 'bold'
                                        }}
                                    >
                                        {f.emoji} {f.label}
                                    </button>
                                ))}
                            </div>
                            {(() => {
                                const filtered = statsFilter === 'all' ? stats : stats.filter(r => r.mode === statsFilter);
                                return (
                                    <>
                                        <div style={{ fontSize: '0.85rem', color: '#94a3b8', marginBottom: '0.5rem' }}>
                                            {statsFilter === 'all' ? '전체' : statsFilter} 기록 ({filtered.length}개, 최대 100개 저장)
                                        </div>
                                        {filtered.length === 0 ? (
                                            <div style={{ color: '#64748b', padding: '2rem', textAlign: 'center', border: '2px dashed #334155', borderRadius: '0.5rem' }}>
                                                기록이 없습니다.
                                            </div>
                                        ) : (
                                            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem' }}>
                                                {[...filtered].reverse().map(r => {
                                                    const isBotWin = r.winnerName.includes('🤖') || r.winnerName.toLowerCase().includes('bot');
                                                    const BOT_ICON = "https://cdn.jsdelivr.net/gh/game-icons/icons@master/delapouite/robot-helmet.svg";
                                        const d = new Date(r.endTime);
                                        const modeEmoji = r.mode === 'Cards' ? '🃏' : r.mode === 'Omok' ? '⚫' : '🤠';
                                        return (
                                            <div key={r.id} style={{ background: r.isMyWin ? '#14532d' : '#0f172a', padding: '0.6rem 0.8rem', borderRadius: '0.4rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center', fontSize: '0.85rem', border: r.isMyWin ? '1px solid #22c55e' : 'none' }}>
                                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.3rem' }}>
                                                    {r.isMyWin && '🏅 '}{modeEmoji}
                                                    {isBotWin && (
                                                        <img src={BOT_ICON} alt="봇" title="봇 승리" style={{ width: '16px', height: '16px', filter: 'invert(0.7) sepia(0.5) saturate(2) hue-rotate(240deg)' }} />
                                                    )}
                                                    <b>{r.winnerName}</b>
                                                    {r.detail && <span style={{ color: '#64748b' }}>· {r.detail}</span>}
                                                </span>
                                                <span style={{ color: '#64748b', fontSize: '0.75rem' }}>{d.toLocaleDateString()} {d.toLocaleTimeString().slice(0, 5)}</span>
                                            </div>
                                        );
                                    })}
                                            </div>
                                        )}
                                    </>
                                );
                            })()}
                            <div style={{ display: 'flex', gap: '0.5rem', marginTop: '1rem' }}>
                                <button
                                    className="logout-btn"
                                    style={{ flex: 1, background: '#7f1d1d' }}
                                    onClick={() => { if (confirm('전체 기록을 삭제할까요?')) { clearHistory(); setStats([]); } }}
                                >🗑 전체 삭제</button>
                                <button className="logout-btn" style={{ flex: 1 }} onClick={() => setShowStats(false)}>닫기</button>
                            </div>
                            </div>)}
                        </div>
                    </div>
                );
            })()}

            {/* 방 생성 모달 */}
            {showCreate && (
                <div className="modal-overlay">
                    <div className="modal-content">
                        <h2 style={{ color: '#38bdf8', marginTop: 0 }}>방 만들기</h2>
                        <div style={{ marginBottom: '1rem' }}>
                            <label style={{ display: 'block', fontSize: '0.8rem', marginBottom: '0.5rem', color: '#94a3b8' }}>방 제목</label>
                            <input 
                                className="chat-input" 
                                style={{ width: '100%', boxSizing: 'border-box' }}
                                value={title}
                                onChange={e => setTitle(e.target.value)}
                            />
                        </div>
                        <div style={{ marginBottom: '1rem' }}>
                            <label style={{ display: 'block', fontSize: '0.8rem', marginBottom: '0.5rem', color: '#94a3b8' }}>비밀번호 (선택)</label>
                            <input
                                className="chat-input"
                                style={{ width: '100%', boxSizing: 'border-box' }}
                                type="password"
                                value={password}
                                onChange={e => setPassword(e.target.value)}
                            />
                        </div>
                        <div style={{ marginBottom: '2rem' }}>
                            <label style={{ display: 'block', fontSize: '0.8rem', marginBottom: '0.5rem', color: '#94a3b8' }}>게임 모드</label>
                            <div style={{ display: 'flex', gap: '0.5rem' }}>
                                {[
                                    { value: 0, label: '🃏 카드' },
                                    { value: 1, label: '⚫ 오목 (2명)' },
                                    { value: 2, label: '🤠 뱅 (4~7명)' }
                                ].map(m => (
                                    <button
                                        key={m.value}
                                        type="button"
                                        onClick={() => setGameMode(m.value)}
                                        style={{
                                            flex: 1,
                                            padding: '0.6rem',
                                            borderRadius: '0.5rem',
                                            border: gameMode === m.value ? '2px solid #38bdf8' : '1px solid #334155',
                                            background: gameMode === m.value ? '#1e3a5f' : '#0f172a',
                                            color: gameMode === m.value ? '#38bdf8' : '#94a3b8',
                                            fontWeight: 'bold',
                                            cursor: 'pointer',
                                            fontSize: '0.9rem'
                                        }}
                                    >
                                        {m.label}
                                    </button>
                                ))}
                            </div>
                        </div>
                        <div style={{ display: 'flex', gap: '0.5rem' }}>
                            <button className="logout-btn" style={{ flex: 1 }} onClick={() => setShowCreate(false)}>취소</button>
                            <button className="create-room-btn" style={{ flex: 1 }} onClick={createRoom}>만들기</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}