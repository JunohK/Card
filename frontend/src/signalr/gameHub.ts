import * as signalR from "@microsoft/signalr";

// signalR 허브 연결 생성
export const connection = new signalR.HubConnectionBuilder()
    .withUrl("https://localhost:5101/gameHub") // 서버 허브 URL
    .withAutomaticReconnect()                  // 연결 끊기면 자동 재접속
    .build();

// 서버에서 플레이어 리스트 갱신 이벤트 수신
connection.on("UpdatePlayerList",(players: string[]) => {
    const ul = document.getElementById("playerList"); // HTML 플레이어 리스트 <ul> 선택
    if(!ul) return;

    ul.innerHTML = ""; // 기존 리스트 초기화

    // 플레이어 리스트를 <li>로 추가
    players.forEach(player => {
        const li = document.createElement("li");
        li.textContent = player;
        ul.appendChild(li);
    })
})

// signalR 연결 시작
export async function startConnection() {
    try{
        await connection.start();
        console.log("SignalR 연결 성공");
    } catch (err) {
        console.error(err);
        // 연결 실패 시 5초 후 재시도
        setTimeout(startConnection, 5000);
    }
}

// 룸 입장 요청
export async function joinRoom(roomId: string, playerName: string){
    await connection.invoke("JoinRoom", roomId, playerName);
}

// 룸 퇴장 요청
export async function leaveRoom(roomId: string, playerName: string){
    await connection.invoke("LeaveRoom", roomId, playerName);
}

connection.on("PlayerJoined",(playerName: string) => {
    console.log(`${playerName}님이 입장했습니다.`);
});

connection.on("PlayerLeft",(playerName: string) => {
    console.log(`${playerName}님이 퇴장하였습니다.`);
})