import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import { connection, ensureConnection } from "../signalr/connection";

type RoomSummary = {
    roomId: string;
    title: string;
    playerCount: number;
    isStarted: boolean;
    isLocked: boolean;
}

export default function LobbyPage() {
    const navigate = useNavigate();
    const { logout } = useAuth();

    const [connected, setConnected] = useState(connection.state === "Connected");
    const [rooms, setRooms] = useState<RoomSummary[]>([]);
    const [messages, setMessages] = useState<string[]>([]);
    const [input, setInput] = useState("");
    const [myName, setMyName] = useState("");

    const [showCreate, setShowCreate] = useState(false);
    const [title, setTitle] = useState("");
    const [password, setPassword] = useState("");

    useEffect(() => {
        let mounted = true;

        const setup = async () => {
            connection.on("ConnectedUser", (name: string) => mounted && setMyName(name));
            connection.on("ReceiveMessage", (user, message) => {
                if (mounted) setMessages(prev => [...prev, `${user} : ${message}`]);
            });
            connection.on("RoomList", (rooms) => mounted && setRooms(rooms));
            
            // [중요] 방 생성 성공 시 로직
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

            // [중요] 방 입장 성공 시 로직
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

    const createRoom = async () => {
        if (!title.trim() || connection.state !== "Connected") return;
        try {
            const pwd = password.trim() === "" ? null : password;
            // 방 ID를 받기 전 임시 저장
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
            // RoomPage에서 꺼내 쓸 수 있도록 저장
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
        <div className="min-h-screen bg-slate-50 p-4 md:p-8">
            <div className="max-w-4xl mx-auto grid grid-cols-1 lg:grid-cols-3 gap-6">
                <div className="lg:col-span-2 space-y-6">
                    <div className="bg-white rounded-2xl shadow-sm p-6 border border-slate-200">
                        <div className="flex justify-between items-center mb-6">
                            <div>
                                <h1 className="text-2xl font-bold text-slate-800">게임 로비</h1>
                                <p className="text-sm text-slate-500">반갑습니다, <span className="font-semibold text-blue-600">{myName || "사용자"}</span>님!</p>
                            </div>
                            <button onClick={() => { logout(); navigate("/login"); }} className="px-4 py-2 text-sm font-medium text-red-500 hover:bg-red-50 rounded-lg transition-colors">로그아웃</button>
                        </div>
                        <div className="flex justify-between items-center mb-4">
                            <h2 className="text-lg font-bold text-slate-700">참여 가능한 방</h2>
                            <button onClick={() => setShowCreate(true)} disabled={!connected} className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-xl font-bold shadow-md disabled:bg-slate-300">+ 방 만들기</button>
                        </div>
                        <div className="grid gap-3">
                            {rooms.length === 0 ? (
                                <div className="text-center py-12 text-slate-400 border-2 border-dashed border-slate-100 rounded-2xl">현재 개설된 방이 없습니다.</div>
                            ) : (
                                rooms.map(room => (
                                    <div key={room.roomId} className="flex justify-between items-center p-4 bg-slate-50 rounded-xl border border-slate-100">
                                        <div>
                                            <div className="flex items-center gap-2">
                                                <span className="font-bold text-slate-700">{room.title}</span>
                                                {room.isLocked && <span>🔒</span>}
                                            </div>
                                            <span className="text-xs text-slate-500">인원: {room.playerCount}명</span>
                                        </div>
                                        <button onClick={() => handleJoinClick(room)} disabled={!connected || room.isStarted} className="bg-white text-blue-600 border border-blue-200 px-4 py-1.5 rounded-lg text-sm font-bold">
                                            {room.isStarted ? "게임중" : "입장"}
                                        </button>
                                    </div>
                                ))
                            )}
                        </div>
                    </div>
                </div>
                <div className="bg-white rounded-2xl shadow-sm border border-slate-200 flex flex-col h-[600px]">
                    <div className="p-4 border-b border-slate-100 font-bold text-slate-700 flex items-center gap-2">
                        <span className={`w-2 h-2 rounded-full ${connected ? 'bg-green-500' : 'bg-red-500'}`}></span> 전체 채팅
                    </div>
                    <div className="flex-1 overflow-y-auto p-4 space-y-2">
                        {messages.map((m, i) => <div key={i} className="text-sm p-2 bg-slate-50 rounded-lg border border-slate-100">{m}</div>)}
                    </div>
                    <div className="p-4 bg-slate-50 rounded-b-2xl flex gap-2">
                        <input className="flex-1 border border-slate-200 rounded-lg px-3 py-2 text-sm outline-none" placeholder="메시지..." value={input} onChange={e => setInput(e.target.value)} onKeyDown={e => e.key === 'Enter' && sendMessage()} />
                        <button onClick={sendMessage} disabled={!connected} className="bg-blue-600 text-white px-3 py-2 rounded-lg text-sm font-bold">전송</button>
                    </div>
                </div>
            </div>

            {showCreate && (
                <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
                    <div className="absolute inset-0 bg-slate-900/40 backdrop-blur-sm" onClick={() => setShowCreate(false)}></div>
                    <div className="relative bg-white w-full max-w-md rounded-2xl shadow-2xl p-8">
                        <h2 className="text-2xl font-bold text-slate-800 mb-6">새로운 방 만들기</h2>
                        <div className="space-y-4">
                            <div>
                                <label className="block text-sm font-semibold text-slate-600 mb-1">방 제목</label>
                                <input className="w-full border border-slate-200 rounded-xl px-4 py-3 outline-none" value={title} onChange={e => setTitle(e.target.value)} autoFocus />
                            </div>
                            <div>
                                <label className="block text-sm font-semibold text-slate-600 mb-1">비밀번호 (선택)</label>
                                <input className="w-full border border-slate-200 rounded-xl px-4 py-3 outline-none" type="password" value={password} onChange={e => setPassword(e.target.value)} />
                            </div>
                        </div>
                        <div className="flex gap-3 mt-8">
                            <button onClick={() => setShowCreate(false)} className="flex-1 px-4 py-3 border border-slate-200 text-slate-500 font-bold rounded-xl">취소</button>
                            <button onClick={createRoom} className="flex-1 px-4 py-3 bg-blue-600 text-white font-bold rounded-xl">방 만들기</button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}