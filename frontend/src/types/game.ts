export interface Card {
  id: string;
  suit: string;
  rank: string;
}

export interface Player {
  playerId: string;
  name: string;
  hand: Card[];
  isReady: boolean;
}

export interface GameState {
  currentTurnPlayerId: string;
  isStarted: boolean;
}

export interface GameRoom {
  roomId: string;
  players: Player[];
  state: GameState;
}
