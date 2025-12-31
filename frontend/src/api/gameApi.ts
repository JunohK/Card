// 타입스크립트 API

import { GameRoom } from "../types/game";

const BASE_URL = "http://localhost:5101/api/game";

export async function createRoom(playerName: string): Promise<GameRoom> {
  const res = await fetch(`${BASE_URL}/create`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ playerName }),
  });

  if (!res.ok) throw new Error("방 생성 실패");
  return res.json();
}

export async function joinRoom(
  roomId: string,
  playerName: string
): Promise<GameRoom> {
  const res = await fetch(`${BASE_URL}/join`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ roomId, playerName }),
  });

  if (!res.ok) throw new Error("방 입장 실패");
  return res.json();
}

export async function getRoom(roomId: string): Promise<GameRoom> {
  const res = await fetch(`${BASE_URL}/${roomId}`);
  if (!res.ok) throw new Error("방 조회 실패");
  return res.json();
}
