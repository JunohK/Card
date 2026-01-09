import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import { connection } from "../signalr/connection";

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

    const [connected, setConnected] = useState(false);
    const [rooms, setRooms] = useState<RoomSummary[]>([]);
    const [messages, setMessages] = useState<string[]>([]);
    const [input, setInput] = useState("");

    // 방만들기 UI
    const [showCreate, setShowCreate] = useState(false);
    const [title, setTitle] = useState("");
    const [password, setPassword] = useState("");

useEffect(() => {
    let mounted = true;

    const setup = async () => {
        // 1️⃣ 이벤트 등록
        connection.on("ReceiveMessage", (user: string, message: string) => {
            if (!mounted) return;
            setMessages(prev => [...prev, `${user} : ${message}`]);
        });
        connection.on("RoomList", (rooms: RoomSummary[]) => {
            if (!mounted) return;
            setRooms(rooms);
        });

        connection.on("RoomCreated", (roomId: string) => {
            navigate(`/room/${roomId}`);
        });
        connection.on("JoinRoomSuccess", (roomId: string) => {
            navigate(`/room/${roomId}`);
        });

        // 2️⃣ 연결 시도
        if (connection.state === "Disconnected") {
            try {
                await connection.start();
            } catch (err) {
                console.error("SignalR 연결 실패:", err);
                setConnected(false);
                return;
            }
        }

        // 3️⃣ 연결 완료 시 invoke 호출
        const waitForConnected = async () => {
            if (connection.state === "Connected") return;
            await new Promise<void>((resolve) => {
                const handler = () => {
                    connection.off("reconnecting", handler);
                    resolve();
                };
                connection.on("reconnecting", handler);
            });
        };
        await waitForConnected();

        if (!mounted) return;
        setConnected(true);

        try {
            await connection.invoke("RequestRoomList");
        } catch (err) {
            console.error("RequestRoomList 실패:", err);
        }

        // 4️⃣ 재연결 시 RoomList 재호출
        connection.onreconnected(async () => {
            if (!mounted) return;
            try { await connection.invoke("RequestRoomList"); }
            catch (err) { console.error(err); }
        });
    };

    setup();

    return () => {
        mounted = false;
        connection.off("ReceiveMessage");
        connection.off("RoomList");
        connection.off("RoomCreated");
        connection.off("JoinRoomSuccess");
    };
}, [navigate]);




    // 채팅
    const sendMessage = async () => {
        if(!input.trim()) return;

        try{
            await connection.invoke("SendChatMessage", input);
            setInput("");
        } catch (err) {
            console.error(err);
        }
    };

    // 방 생성
    const createRoom = async () => {
        if (!title.trim()) {
            alert("방 제목을 입력해주세요.");
            return;
        }

        try {
            await connection.invoke(
                "CreateRoom",
                title,
                password.trim() === "" ? null : password
            );

            // 성공 시 초기화
            setShowCreate(false);
            setTitle("");
            setPassword("");
        } catch (err: any) {
            console.error(err);
            alert(err?.message ?? "방 생성 실패");
        }
    };

    // 비밀번호 입력 UI
    const handleJoinClick = (room: RoomSummary) => {
        if(room.isLocked) {
            const pwd = prompt("비밀번호를 입력하세요");
            if(pwd === null) return; // 취소
            joinRoom(room.roomId,pwd);
        } else {
            joinRoom(room.roomId);
        }
    };

    // 방 입장
    const joinRoom = async (roomId: string, password?: string) => {
        try{
            // 비밀번호 필요 없는 경우 null 전달
            await connection.invoke("JoinRoom", roomId, password ?? null);
            navigate(`/room/${roomId}`);
        } catch (err: any) {
            console.error("JoinRoom 실패 : ", err);
            alert(err?.message ?? " 방 입장 실패");
        }
    };

    const handleLogout = () => {
        logout();
        navigate("/login");
    }

    return (
        <>
            <div className="min-h-screen bg-gray-100 p-6">
                <div className="max-w-3xl mx-auto bg-white rounded-xl shadow p-6">
                    <div className="flex justify-between items-center mb-4">
                        <h1 className="text-xl font-bold">로비</h1>
                        <button
                            onClick={handleLogout}
                            className="text-sm text-red-600"
                        >
                            로그아웃
                        </button>
                    </div>

                    <p className="text-sm mb-4">
                        서버 연결 상태 : {connected ? "Connected" : "Disconnected"}
                    </p>

                    {/* 방 영역 ~ */}
                    <div className="mb-6">
                        <div className="flex justify-between items-center mb-2">
                            <h2 className="font-semibold">방 목록</h2>
                            <button
                                onClick={() => setShowCreate(true)}
                                className="bg-blue-600 text-white px-3 py-1 rounded"
                            >
                                방 만들기
                            </button>
                        </div>

                    <ul className="space-y-2">
                        {rooms.map(room => (
                            <li
                                key={room.roomId}
                                className="border rounded p-3 flex justify-between items-center"
                            >
                                <div>
                                    <p className="font-medium flex items-center gap-1">
                                        {room.title}
                                        {room.isLocked && (
                                            <span title="비밀번호 방">🔒</span>
                                        )}
                                    </p>
                                    <p className="text-sm text-gray-500">
                                        인원 {room.playerCount}
                                    </p>
                                </div>

                                <button
                                    disabled={room.isStarted}
                                    onClick={() => handleJoinClick(room)}
                                    className="bg-green-600 text-white px-3 py-1 rounded disabled:opacity-50"
                                >
                                    입장
                                </button>
                            </li>
                        ))}
                    </ul>
                </div>
                {/* ~ 방 영역 */}

                    {/* 채팅 영역 ~ */}
                    <div className="border rounded p-4 h-64 overflow-y-auto mb-4">
                        {messages.map((m, i) => (
                            <div key={i} className="text-sm mb-1">{m}</div>
                        ))}
                    </div>

                    <div className="flex gap-2">
                        <input
                            className="flex-1 border rounded px-3 py-2"
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            placeholder="메시지 입력"
                        />
                        <button
                            onClick={sendMessage}
                            className="bg-blue-600 text-white px-4 rounded"
                        >
                            전송
                        </button>
                    </div>
                    {/* ~ 채팅 영역 */}
                </div>
            </div>

            {showCreate && (
                <div className="fixed inset-0 bg-black/40 flex items-center justify-center z-50">
                    <div className="bg-white rounded-xl shadow-lg w-96 p-6">
                    <h2 className="text-lg font-bold mb-4">방 만들기</h2>

                    {/* 방 제목 */}
                    <div className="mb-3">
                        <label className="block text-sm font-medium mb-1">
                            방 제목
                        </label>
                        <input
                            value={title}
                            onChange={e => setTitle(e.target.value)}
                            className="w-full border rounded px-3 py-2"
                        />
                    </div>

                    {/* 비밀번호 */}
                    <div className="mb-4">
                        <label className="block text-sm font-medium mb-1">
                            비밀번호 (선택)
                        </label>
                        <input
                            type="password"
                            value={password}
                            onChange={e => setPassword(e.target.value)}
                            className="w-full border rounded px-3 py-2"
                        />
                    </div>

                    {/* 버튼 */}
                    <div className="flex justify-end gap-2">
                        <button
                            onClick={() => setShowCreate(false)}
                            className="px-4 py-2 border rounded"
                        >
                            취소
                        </button>
                        <button
                            onClick={createRoom}
                            className="px-4 py-2 bg-blue-600 text-white rounded"
                        >
                            생성
                        </button>
                    </div>
                </div>
            </div>
            )}
        </>
    );
}

