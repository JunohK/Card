import { createContext, useContext, useEffect, useState } from "react";
import { connection } from "./connection";

// 방 목록 데이터 타입 정의 (기존 RoomSummaryDTO와 맞춰주세요)
type RoomSummary = {
    roomId: string;
    title: string;
    playerCount: number;
    isStarted: boolean;
    isLocked: boolean;
    playerNames: string[];
};

type SignalRContextType = {
    connected: boolean;
    roomList: RoomSummary[]; // 방 목록 상태 추가
};

const SignalRContext = createContext<SignalRContextType>({
    connected: false,
    roomList: [],
});

export const SignalRProvider = ({ children }: { children: React.ReactNode }) => {
    const [connected, setConnected] = useState(false);
    const [roomList, setRoomList] = useState<RoomSummary[]>([]); // 방 목록 state

    useEffect(() => {
        // [핵심] 서버의 "RoomList" 이벤트를 여기서 미리 등록합니다.
        // start() 하기 전에 등록하는 것이 좋습니다.
        connection.on("roomList", (rooms: RoomSummary[]) => {
            console.log("전역 방 목록 갱신:", rooms);
            setRoomList(rooms);
        });

        const start = async () => {
            if (connection.state === "Disconnected") {
                try {
                    await connection.start();
                    setConnected(true);
                    console.log("SignalR Connected");

                    // 연결 성공 직후, 로비 정보를 가져오기 위해 서버 메서드 호출 (선택 사항)
                    // connection.invoke("EnterLobby"); 
                } catch (e) {
                    console.error("SignalR start failed", e);
                }
            }
        };

        start();

        connection.onreconnected(() => setConnected(true));
        connection.onclose(() => setConnected(false));

        return () => {
            // 언마운트 시 핸들러 제거
            connection.off("RoomList");
        };
    }, []);

    return (
        // roomList를 value에 추가하여 하위 컴포넌트들이 쓸 수 있게 합니다.
        <SignalRContext.Provider value={{ connected, roomList }}>
            {children}
        </SignalRContext.Provider>
    );
};

export const useSignalR = () => useContext(SignalRContext);