import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection, ensureConnection, setActiveRoom } from "../signalr/connection";
import { getMyPlayerId } from "../auth/authStorage";
import { useTurnTimeout } from "../util/useTurnTimeout";
import { playSound } from "../sound/sound";
import { pushHistory } from "../util/gameHistory";
import { hwatuCardDataUri } from "../util/hwatuCard";
import StatsModal from "../components/StatsModal";
import { setSoundTogglePlacement } from "../components/soundTogglePlacement";
import "../css/GamePage.css";

// 상대 좌석 — 본인(6시)을 포함한 전원을 테이블 타원 위에 균등 배치.
// 2명이면 12시, 3명이면 10시·2시, 4명이면 9·12·3시 … 최대 7명까지 같은 비율.
// k = 내 다음 턴부터 시계 방향 순서(1..n), totalPlayers = 본인 포함 인원
// 손패에서 2장 이상인 숫자마다 테두리 색 클래스를 배정 (숫자 오름차순, 6색 순환). 조커는 제외 — CSS에서 별도 금색
type RankedCard = { rank?: string; Rank?: string };
const buildRankGroups = (hand: RankedCard[], rankValue: (r: string) => number): Record<string, string> => {
    const counts: Record<string, number> = {};
    for (const c of hand) {
        const r = String(c.rank ?? c.Rank ?? "").toUpperCase();
        counts[r] = (counts[r] ?? 0) + 1;
    }
    const groups: Record<string, string> = {};
    Object.keys(counts)
        .filter(r => counts[r] >= 2 && r !== "JOKER" && r !== "JK")
        .sort((a, b) => rankValue(a) - rankValue(b))
        .forEach((r, i) => { groups[r] = `rank-group-${i % 6}`; });
    return groups;
};

const seatPosition = (k: number, totalPlayers: number) => {
    const theta = Math.PI + (2 * Math.PI * k) / totalPlayers; // 12시 기준 시계 방향 각도, 본인은 π(6시)
    return { top: `${50 - 35 * Math.cos(theta)}%`, left: `${50 + 40 * Math.sin(theta)}%` };
};

type MyProfile = {
    name: string;
    wins: number;
    totalGames: number;
    maxScore: number;
    minScore: number;
}

interface GameState {
    isNaturalBagajiEnabled: boolean;
    players: any[];
    currentTurnPlayerId: string;
}


export default function GamePage() {
    const { roomId } = useParams<{ roomId: string }>();
    const navigate = useNavigate();

    // 재접속(네트워크 끊김 자동 재연결) 시 자동 재입장할 방 등록
    useEffect(() => {
        setActiveRoom(roomId ?? null);
        return () => setActiveRoom(null);
    }, [roomId]);

    const [game, setGame] = useState<any>(null);
    const [showRoundResult, setShowRoundResult] = useState(false);
    const [showDiscardModal, setShowDiscardModal] = useState(false);
    const [showScoreHistory, setShowScoreHistory] = useState(false);
    const [lastDrawnCardKey, setLastDrawnCardKey] = useState<string | null>(null);
    const [showStats, setShowStats] = useState(false); // 결과창에서 방을 떠나지 않고 띄우는 게임 기록 모달
    const [input, setInput] = useState("");
    const [connected] = useState(connection.state === "Connected");
    const [messages, setMessages] = useState<string[]>([]);
    const [myProfile, setMyProfile] = useState<MyProfile>({
        name: "",
        wins: 0,
        totalGames: 0,
        maxScore: 0,
        minScore: 0
    });
    const [isChatMinimized, setIsChatMinimized] = useState(true); // false로 하면 최소화가 기본값
    const [hasNewMessage, setHasNewMessage] = useState(false);
    // 채팅 접힘 상태를 ref로도 유지 — 구독 effect가 isChatMinimized에 의존해 토글마다 재구독·재입장하지 않도록
    const isChatMinimizedRef = useRef(isChatMinimized);
    useEffect(() => { isChatMinimizedRef.current = isChatMinimized; }, [isChatMinimized]);
    const [, setWinnerHand] = useState<any[]>([]);
    const [, setWinnerName] = useState<string | null>(null);
    const [chatWidth, setChatWidth] = useState(260);
    const [chatHeight, setChatHeight] = useState(320);

    
    // // 메시지가 새로 추가되면 자동으로 채팅창을 펼침
    // useEffect(() => {
    //     if (messages.length > 0) {
    //         setIsChatMinimized(false);
    //     }
    // }, [messages]);
    const chatRef = useRef<HTMLDivElement>(null);
    const resizeDirection = useRef<null | "left" | "top" | "corner">(null);

    useEffect(() => {
        const handleMouseMove = (e: MouseEvent) => {
            if (!resizeDirection.current) return;

            if (resizeDirection.current === "left" || resizeDirection.current === "corner") {
                setChatWidth(prev => {
                    const newWidth = prev + (window.innerWidth - e.clientX - prev - 20);
                    return Math.max(200, Math.min(newWidth, 600));
                });
            }

            if (resizeDirection.current === "top" || resizeDirection.current === "corner") {
                setChatHeight(prev => {
                    const newHeight = prev + (window.innerHeight - e.clientY - prev - 20);
                    return Math.max(150, Math.min(newHeight, 700));
                });
            }
        };

        const handleMouseUp = () => {
            resizeDirection.current = null;
        };

        window.addEventListener("mousemove", handleMouseMove);
        window.addEventListener("mouseup", handleMouseUp);

        return () => {
            window.removeEventListener("mousemove", handleMouseMove);
            window.removeEventListener("mouseup", handleMouseUp);
        };
    }, []);

    useEffect(() => {
        // chatRef.current가 존재하는지(null이 아닌지) 체크 후 호출
        if (chatRef.current) {
            chatRef.current.scrollIntoView({ behavior: 'smooth' });
        }
    }, [messages]);

    useEffect(() => {
        connection.on("NaturalBagajiToggled", (isEnabled: boolean) => {
            // game 상태 업데이트
            setGame((prev: GameState) => ({
                ...prev,
                isNaturalBagajiEnabled: isEnabled
            }));
        });

        return () => {
            connection.off("NaturalBagajiToggled");
        };
    }, [connection]);

    useEffect(() => {
        localStorage.setItem("chatSize", JSON.stringify({ chatWidth, chatHeight }));
        }, [chatWidth, chatHeight]);

        useEffect(() => {
            const saved = localStorage.getItem("chatSize");
            if (saved) {
                const { chatWidth, chatHeight } = JSON.parse(saved);
                setChatWidth(chatWidth);
                setChatHeight(chatHeight);
            }
    }, []);

    // 소리 버튼을 우측 하단 라이브챗 왼쪽에 배치 — 우측 상단은 기권 버튼 자리.
    // 채팅 바는 bottom 20px / right 20px / 최소화 높이 45px, 버튼은 40px이라 세로 중앙 맞춤
    useEffect(() => {
        setSoundTogglePlacement({ bottom: 22, right: chatWidth + 30 });
        return () => setSoundTogglePlacement(null);
    }, [chatWidth]);

    // ✅ 에러 메시지 처리를 위한 상태 추가
    const [errorMsg, setErrorMsg] = useState<string | null>(null);

    const myId = getMyPlayerId();
    const prevHandRef = useRef<string[]>([]);
    const isSubscribed = useRef(false);
    const historyPushedRef = useRef(false);   // 게임당 1회만 전적 기록 (종료 후 RoomUpdated가 반복돼도 중복 방지)

    // 🟢 카드 고유 키 생성
    const getCardKey = (card: any) => {
        if (!card) return "";
        return `${card.rank || card.Rank}-${card.suit || card.Suit}-${card.id || card.Id || ""}`;
    };

    const getRankValue = (rank: string) => {
        if (!rank) return 0;
        const r = rank.toString().toUpperCase();
        if (r === "JOKER" || r === "JK") return 99;
        if (r === "A") return 1;
        if (r === "J") return 11;
        if (r === "Q") return 12;
        if (r === "K") return 13;
        return parseInt(r);
    };

    const getRankText = (rank: string) => {
        if (!rank) return "";
        const r = rank.toString().toUpperCase();
        return (r === "JOKER" || r === "JK") ? "JK" : r;
    };

    const isHwatuMode = (game?.cardMode ?? game?.CardMode) === 1;

    // 화투 모드일 때 카드 앞면 이미지(SVG data URI) 반환. 숫자·그림이 들어 있어 글자 스팬은 그리지 않는다.
    // 화투에는 무늬(♠♥♦♣) 개념이 없으므로 같은 달 4장은 같은 그림. Trump 모드·조커는 null → 기존 텍스트 카드
    const getCardImageSrc = (rank: string): string | null => {
        if (!isHwatuMode) return null;
        if (!rank) return null;

        const r = rank.toString().toUpperCase();
        if (r === "JOKER" || r === "JK") return null;

        const rankMap: Record<string, number> = { A: 1, J: 11, Q: 12 };
        const rankNum = rankMap[r] ?? parseInt(r, 10);
        if (isNaN(rankNum)) return null;

        return hwatuCardDataUri(rankNum);
    };

    const sortCards = (cards: any[]) => {
        if (!cards) return [];
        return [...cards].sort((a, b) =>
            getRankValue(a.rank || a.Rank) - getRankValue(b.rank || b.Rank)
        );
    };

    const sendMessage = async () => {
        if (!input.trim() || connection.state !== "Connected") return;
        await connection.invoke("SendRoomChat", roomId, input);
        setInput("");
    };

    const unsubscribeAll = () => {
        connection.off("RoomUpdated");
        connection.off("GameStarted");
        connection.off("ShowResultBoard");
        connection.off("GameTerminated");
        connection.off("ErrorMessage");
        connection.off("HideResultBoard");
        connection.off("ExitToRoom");
        isSubscribed.current = false;
        console.log("🚫 모든 게임 리스너 구독 해제됨");
    };

    useEffect(() => {
        const onUpdate = async (data: any) => {
            if (!data) return;

            const playersArr = data.players || data.Players || [];
            const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
            const currentHand = me?.hand || me?.Hand || [];
            
            const currentHandKeys = currentHand.map((c: any) => getCardKey(c));

            if (currentHandKeys.length > prevHandRef.current.length) {
                const newKey = currentHandKeys.find((key: string) => !prevHandRef.current.includes(key));
                if (newKey) {
                    setLastDrawnCardKey(newKey);
                    setTimeout(() => setLastDrawnCardKey(null), 1000);
                }
            }
            prevHandRef.current = currentHandKeys;

            const playersArr2 = data.players || data.Players || [];
            const me2 = playersArr2.find((p: any) => (p.playerId || p.PlayerId) === myId);

            setGame((prev: any) => ({
                ...data,
                isNaturalBagajiEnabled: me2?.allowNaturalBagaji ?? me2?.AllowNaturalBagaji ?? prev?.isNaturalBagajiEnabled ?? true
            }));

            
            const roundEnded = data.isRoundEnded || data.IsRoundEnded;
            const gameFinished = data.isFinished || data.IsFinished;

            if (gameFinished) {
                setShowRoundResult(false);
                // 전적 DB 반영은 서버가 게임 종료 시 1회 수행 (클라이언트 호출 없음 — 중복 집계 방지)
                // 게임 결과 통계 — localStorage (게임 전체 승자 = 총점 최저 기준). 종료 후 RoomUpdated가 반복돼도 1회만
                if (!historyPushedRef.current) {
                    historyPushedRef.current = true;
                    const gameWinnerIds: string[] = data.gameWinnerIds || data.GameWinnerIds || [];
                    const winnerName = data.gameWinnerName || data.GameWinnerName || data.winnerName || data.WinnerName || "승자 미확정";
                    const winType = data.lastWinType || data.LastWinType || "";
                    pushHistory({
                        mode: "Cards",
                        winnerName,
                        detail: winType,
                        isMyWin: gameWinnerIds.includes(myId ?? "")
                    });
                }
            } else if (roundEnded) {
                setShowRoundResult(true);
            }
        };

        const onHideResultBoard = () => setShowRoundResult(false);

        const onGameTerminated = (data: any) => {
            setGame((prev: any) => ({ ...prev, ...data, isFinished: true }));
        };

        const onExitToRoom = (targetRoomId: string) => {
            unsubscribeAll();
            navigate(`/room/${targetRoomId || roomId}`, { replace: true });
        };

        const onReceiveMessage = (user: string, message: string) => {
            setMessages(prev => [...prev, `${user} : ${message}`]);
            if (isChatMinimizedRef.current) {
                setHasNewMessage(true);
            }
        };

        const onConnectedUser = (data: any) => {
            if (typeof data === "string") {
                setMyProfile(prev => ({ ...prev, name: data }));
            } else {
                setMyProfile({
                    name: data.nickname || data.name || "",
                    wins: data.wins || 0,
                    totalGames: data.totalGames || 0,
                    maxScore: data.maxScore || 0,
                    minScore: data.minScore || 0
                });
            }
        };

        if (!isSubscribed.current) {
            connection.on("RoomUpdated", onUpdate);
            connection.on("ReceiveMessage", onReceiveMessage);
            connection.on("ConnectedUser", onConnectedUser);

            connection.on("GameStarted", (data) => {
                historyPushedRef.current = false;
                setGame((prev: any) => ({
                    ...prev,
                    winnerName: null,
                    winnerHand: [],
                    isBagaji: false,
                    showResult: false 
                }));
                onUpdate(data);
            });

            connection.on("ShowResultBoard", (data: any) => {
                console.log("SHOW RESULT BOARD", data.WinnerHand);
                setWinnerHand(data.WinnerHand || []);
                setWinnerName(data.WinnerName || null);
                setShowRoundResult(true);
                playSound("win");
            });

            connection.on("HideResultBoard", onHideResultBoard);
            connection.on("GameTerminated", onGameTerminated);
            connection.on("ExitToRoom", onExitToRoom);

            connection.on("ErrorMessage", (msg) => {
                setErrorMsg(msg);
                setTimeout(() => setErrorMsg(null), 3000); 
            });
            
            isSubscribed.current = true;
        }

        // 새로고침 복구: 연결 보장 → 방 재입장(그룹 재가입) → 현재 상태 로드
        const loadRoom = async () => {
            try {
                const ok = await ensureConnection();
                if (!ok) throw new Error("서버에 연결할 수 없습니다.");
                const savedPwd = sessionStorage.getItem(`room_pwd_${roomId}`) || "";
                await connection.invoke("JoinRoom", roomId, savedPwd === "" ? null : savedPwd);
                const data = await connection.invoke("GetRoom", roomId);
                if (data) {
                    setGame(data);
                    const playersArr = data.players || data.Players || [];
                    const me = playersArr.find((p: any) => (p.playerId || p.PlayerId) === myId);
                    prevHandRef.current = (me?.hand || me?.Hand || []).map((c: any) => getCardKey(c));
                }
            } catch (err) {
                console.error("방 상태 로드 실패:", err);
                setErrorMsg("방 정보를 불러오지 못했습니다. 로비로 돌아갑니다.");
                setTimeout(() => navigate('/lobby', { replace: true }), 1500);
            }
        };
        loadRoom();

        // 프로필 정보 로드
        connection.invoke("GetMyProfile").then(data => {
            if (data) setMyProfile(data);
        }).catch(err => console.error("프로필 로드 실패:", err));

        return () => {
            connection.off("ReceiveMessage", onReceiveMessage);
            connection.off("ConnectedUser", onConnectedUser);
            unsubscribeAll();
        };
    }, [roomId, myId, navigate]);

    const handleExit = () => {
        if (window.confirm("정말 기권하시겠습니까? 전체 게임이 종료되며 대기실로 이동합니다.")) {
            playSound("resign");
            connection.invoke("GiveUp", roomId).catch(err => console.error("기권 처리 중 오류:", err));
        }
    };

    const handleReturnToRoom = () => {
        unsubscribeAll();
        // 기권 종료도 일반 종료와 같이 대기실로 복귀 — 전원이 로비로 나가면 빈 방이 삭제되기 때문
        navigate(`/room/${roomId}`, { replace: true });
    };

    const handleNextRoundRequest = () => {
        connection.invoke("RequestNextRound", roomId).catch(err => alert("다음 라운드 시작 실패: " + err));
    };

    const handleReshuffle = () => {
        if (window.confirm("버려진 카드들을 다시 섞어서 덱으로 만드시겠습니까?")) {
            connection.invoke("ReshuffleDeck", roomId).catch(err => alert("셔플 실패: " + err));
        }
    };

    const openRules = () => {
        window.open('/rules', '_blank', 'width=600,height=800,noopener,noreferrer');
    };

    // 턴 타임아웃 — 현재 턴 플레이어에 대해 카운트다운 (라운드 진행 중일 때만)
    const turnSecLeft = useTurnTimeout(
        roomId,
        (game?.currentTurnPlayerId ?? game?.CurrentTurnPlayerId) ?? null,
        !!game && !!(game.isStarted ?? game.IsStarted) && !(game.isFinished ?? game.IsFinished) && !(game.isRoundEnded ?? game.IsRoundEnded),
        (game?.turnTimeoutSec ?? game?.TurnTimeoutSec) ?? 60
    );

    // 본인 차례 시작 알림 사운드 (false → true 전환)
    // 아래 로딩 중 early return보다 앞에 있어야 함 — 훅은 매 렌더 같은 순서로 호출돼야 함
    const currentTurnId = game?.currentTurnPlayerId || game?.CurrentTurnPlayerId;
    const isMyTurn = !!game && currentTurnId === myId;
    const prevIsMyTurnRef = useRef(false);
    useEffect(() => {
        if (isMyTurn && !prevIsMyTurnRef.current) {
            playSound("my-turn");
        }
        prevIsMyTurnRef.current = isMyTurn;
    }, [isMyTurn]);

    if (!game || (!game.players && !game.Players)) {
        return (
            <div className="game-container loading-state" style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', color: 'white', height: '100vh' }}>
                <h2 style={{ marginBottom: '20px' }}>데이터 동기화 중...</h2>
                <button onClick={() => navigate('/lobby')} style={{ padding: '12px 24px', backgroundColor: '#34495e', color: 'white', border: 'none', borderRadius: '8px', cursor: 'pointer', fontWeight: 'bold' }}>로비로 돌아가기</button>
            </div>
        );
    }

    const players = game.players || game.Players || [];
    const me = players.find((p: any) => (p.playerId || p.PlayerId) === myId);
    // 내 다음 턴 순서부터 나열 — 좌석을 시계 방향 턴 순서대로 앉히기 위해 (턴 순서 = players 배열 순서)
    const myIndex = players.indexOf(me); // -1이면 players에 내가 없는 경우(관전 등)
    const others = myIndex >= 0
        ? [...players.slice(myIndex + 1), ...players.slice(0, myIndex)]
        : players.filter((p: any) => (p.playerId || p.PlayerId) !== myId);
    const isHost = (game.hostPlayerId || game.HostPlayerId) === myId;
    const deckCount = game.deckCount ?? game.DeckCount ?? 0;
    const lastDiscarded = game.lastDiscardedCard || game.LastDiscardedCard;
    const discardPile = game.discardPile || game.DiscardPile || [];

    const myHand = me?.hand || me?.Hand || [];
    // 서버가 본인 뷰에만 실어 주는 족보 힌트 (6장: 지금 승리 가능 / 5장: 어떤 카드가 오면 승리)
    const winHint = me?.winHint ?? me?.WinHint ?? null;
    const rankGroups = buildRankGroups(myHand, getRankValue);
    const formatOuts = (outs: Array<{ rank: string; winType: string }>) => {
        const byType: Record<string, string[]> = {};
        for (const o of outs) {
            const label = o.rank === "Joker" ? "조커" : (isHwatuMode ? String(getRankValue(o.rank)) : getRankText(o.rank));
            if (!byType[o.winType]) byType[o.winType] = [];
            byType[o.winType].push(label);
        }
        return "이 카드가 오면 승리: " + Object.entries(byType).map(([type, ranks]) => `${ranks.join(", ")} → ${type}`).join(" · ");
    };
    // 뻥 대기 시간(상대가 버린 뒤 2초) — 뻥 버튼 활성 기준. 뽑기는 막지 않는다 (그 사이 뻥이 성립하면 서버가 뽑은 카드를 덱으로 되돌림)
    const pungWindowOpen = !!(game.isPungWindowOpen ?? game.IsPungWindowOpen);
    const canDraw = isMyTurn && (myHand.length === 2 || myHand.length === 5);
    const canDiscardOrWin = isMyTurn && (myHand.length === 3 || myHand.length === 6);

    // 최종 판단 로직
    const checkCanDeclareWin = () => {
        if (!isMyTurn) return false;

        // 'me' 대신 'myInfo'로 이름을 바꾸어 중복 선언 에러 방지
        const myInfo = players.find((p: any) => (p.playerId || p.PlayerId) === myId);
        const currentTurnCount = myInfo?.roundTurnCount || myInfo?.RoundTurnCount || 0;

        // 내가 2번째 턴 이상일 때만 버튼 활성화
        return currentTurnCount >= 2;
    };

    const canDeclareWin = checkCanDeclareWin();

    const checkCanPung = () => {
        // 0. 서버가 뻥 윈도우를 연 동안에만 가능 (뻥으로 버린 카드 등 윈도우 밖에서는 버튼이 켜지지 않도록)
        if (!pungWindowOpen) return false;

        // 1. 기본 조건: 내 손에 카드가 5장일 때만 가능
        const currentHandCount = myHand.length;
        if (currentHandCount !== 5) return false;

        // 2. 바닥에 버려진 카드가 있어야 함
        if (!lastDiscarded) return false; 

        // 3. 자가 뻥 방지: 내가 버린 카드는 내가 뻥 할 수 없음
        const lastActorId = game?.lastActorPlayerId || game?.LastActorPlayerId;
        if (lastActorId && String(lastActorId) === String(myId)) {
            return false; 
        }

        // 4. 내 턴 조건 완화 (턴이 넘어온 직후에도 뻥 버튼이 유지되도록)
        // 상대가 버린 직후 턴이 나에게 왔더라도, 뻥을 칠 수 있는 기회를 주기 위해 
        // 내 손패가 아직 5장(뽑기 전)이라면 뻥 체크를 허용합니다.
        if (isMyTurn && currentHandCount !== 5) return false; 

        // 5. 비교 대상(상대가 버린 카드)의 숫자 추출
        const discardedRank = (lastDiscarded.rank || lastDiscarded.Rank)?.toString().toUpperCase();
        // 조커는 뻥의 대상(버려진 카드)이 될 수 없음
        if (!discardedRank || discardedRank === "JK" || discardedRank === "JOKER") return false;

        // 6. 내 손패 확인 (동일 숫자 카드와 조커 분리 추출)
        const sameRankCards = myHand.filter((c: any) => {
            const myCardRank = (c.rank || c.Rank)?.toString().toUpperCase();
            return myCardRank === discardedRank;
        });

        const jokerCards = myHand.filter((c: any) => {
            const myCardRank = (c.rank || c.Rank)?.toString().toUpperCase();
            return myCardRank === "JK" || myCardRank === "JOKER";
        });

        // 7. 뻥 구성 우선순위 결정 (7, 7, 조커 상황 대응)
        let finalPungCards: any[] = [];
        
        // 숫자 카드를 우선순위로 먼저 채움 (최대 2장)
        finalPungCards.push(...sameRankCards.slice(0, 2));

        // 숫자 카드가 2장이 안 될 때만 조커를 사용함
        if (finalPungCards.length < 2) {
            const needed = 2 - finalPungCards.length;
            finalPungCards.push(...jokerCards.slice(0, needed));
        }

        // 최종 결과: 우선순위에 따라 구성된 카드가 2장이면 뻥 가능(true)
        return finalPungCards.length === 2;
    };

    const canPung = checkCanPung();

    /** 🛑 STOP 버튼 활성화 조건 (본인 포함 2장인 사람 2명 이상) */
    const checkCanStop = () => {
        const currentHandCount = myHand.length;
        
        // 1. 내 턴이고, 내가 카드를 한 장 뽑아서 3장인 상태여야 함 (뽑기 전 2장)
        const isActionPhase = isMyTurn && currentHandCount === 3;
        if (!isActionPhase) return false;

        // 2. 패가 2장인 사람 카운트 (본인 포함)
        const playersWithTwoCards = players.filter((p: any) => {
            const pid = p.playerId || p.PlayerId;
            // 상대 손패는 마스킹되므로 서버가 내려주는 handCount 사용
            const handCount = p.handCount ?? p.HandCount ?? p.hand?.length ?? p.Hand?.length ?? 0;

            if (pid === myId) {
                // 나는 현재 3장이지만 뽑기 전에는 2장이었으므로 조건 충족
                return true; 
            }
            
            // 다른 플레이어들은 현재 손에 든 카드가 정확히 2장이어야 함
            return handCount === 2;
        });

        // 본인(2장 상태에서 뽑은 자)을 포함하여 2장인 사람이 최소 2명 이상일 때
        return playersWithTwoCards.length >= 2;
    };

    /** 🛑 STOP 버튼 클릭 핸들러 */
    const handleStop = () => {
        if (!canStop) return;
        
        if (window.confirm("STOP을 선언하시겠습니까? 이번에 카드를 버리면 게임이 종료됩니다.")) {
            connection.invoke("DeclareStop", roomId)
                .catch(err => console.error("STOP 호출 실패:", err));
        }
    };

    const canStop = checkCanStop();

return (
        <div className={`game-container ${isMyTurn ? "my-turn-flash" : ""}`} style={{ position: 'relative', minHeight: '100vh', overflow: 'hidden' }}>
            {turnSecLeft != null && (
                <div style={{ position: 'fixed', top: 58, right: 12 /* 헤더(50px) 아래, 소리 버튼 밑 — 기권 버튼을 가리지 않도록 */, zIndex: 9999, background: turnSecLeft <= 10 ? '#dc2626' : 'rgba(30,41,59,0.9)', color: '#fff', padding: '4px 10px', borderRadius: 8, fontWeight: 'bold', fontSize: 13 }}>
                    ⏱ {turnSecLeft}s
                </div>
            )}
            <style>{`
                @keyframes fadeInModal {
                    0% { opacity: 0; }
                    100% { opacity: 1; }
                }

                .fade-in-2s {
                    animation: fadeInModal 2s ease-in-out forwards;
                }
                @keyframes blueGlow {
                    0% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; }
                    50% { box-shadow: 0 0 25px #3498db; border: 3px solid #5dade2; }
                    100% { box-shadow: 0 0 5px #3498db; border: 3px solid #3498db; }
                }
                @keyframes redPulse {
                    0% { box-shadow: 0 0 5px #e74c3c; }
                    50% { box-shadow: 0 0 30px #e74c3c; background-color: #ff5e4d; }
                    100% { box-shadow: 0 0 5px #e74c3c; }
                }
                /* ✨ 내 턴일 때 화면 테두리 금색 광채 애니메이션 */
                @keyframes goldGlow {
                    0% { box-shadow: inset 0 0 10px #f1c40f; }
                    50% { box-shadow: inset 0 0 40px #f39c12; }
                    100% { box-shadow: inset 0 0 10px #f1c40f; }
                }
                /* ✨ 뻥/알림 메시지 애니메이션 */
                @keyframes notifyPop {
                    0% { transform: translate(-50%, -60%); opacity: 0; }
                    10% { transform: translate(-50%, -50%); opacity: 1; }
                    90% { transform: translate(-50%, -50%); opacity: 1; }
                    100% { transform: translate(-50%, -40%); opacity: 0; }
                }

                .my-turn-flash { animation: goldGlow 1.5s infinite ease-in-out; }
                /* z-index를 올리지 않음 — 정렬된 손패 가운데에 들어온 새 카드가 오른쪽 카드의 숫자를 덮지 않도록 */
                .new-card-highlight { animation: blueGlow 0.8s ease-in-out infinite !important; }
                .pung-active { animation: redPulse 0.5s infinite !important; background-color: #e74c3c !important; color: white !important; border: 2px solid white !important; cursor: pointer !important; opacity: 1 !important; z-index: 1000; }
                .rule-btn-fixed { position: fixed; bottom: 20px; left: 20px; padding: 10px 20px; background: #f1c40f; color: #2c3e50; border: none; border-radius: 50px; font-weight: bold; cursor: pointer; z-index: 1000; box-shadow: 0 4px 10px rgba(0,0,0,0.3); }
                .reshuffle-badge { cursor: pointer; background: #2980b9; padding: 4px 8px; border-radius: 4px; font-size: 11px; margin-top: 8px; border: none; color: white; transition: 0.2s; }
                .reshuffle-badge:hover { background: #3498db; }
                .discard-modal-overlay { position: fixed; top: 0; left: 0; width: 100%; height: 100%; background: rgba(0,0,0,0.85); display: flex; justify-content: center; align-items: center; z-index: 9999; }
                .discard-modal-content { background: #2c3e50; width: 80%; max-width: 600px; max-height: 80vh; overflow-y: auto; padding: 20px; border-radius: 12px; border: 1px solid #34495e; }
                
                /* 알림 오버레이 스타일 */
                .turn-notify-overlay {
                    position: fixed; top: 35%; left: 50%; transform: translate(-50%, -50%);
                    background: rgba(0, 0, 0, 0.85); color: #f1c40f; padding: 20px 50px;
                    border-radius: 60px; z-index: 11000; font-weight: bold; font-size: 2.2rem;
                    border: 4px solid #f1c40f; pointer-events: none; animation: notifyPop 2.5s forwards;
                    box-shadow: 0 0 30px rgba(241, 196, 15, 0.4); text-shadow: 2px 2px 4px rgba(0,0,0,0.5);
                }
            `}</style>

            {/* 📢 뻥 성공 및 턴 시작 알림 (alertMsg 상태가 있을 때 노출) */}
            {/* {alertMsg && <div className="turn-notify-overlay">📢 {alertMsg}</div>} */}

            {/* 🔴 조그만 에러 알림 팝업 UI */}
            {errorMsg && (
                <div style={{
                    position: 'fixed', top: '15%', left: '50%', transform: 'translateX(-50%)',
                    backgroundColor: 'rgba(231, 76, 60, 0.95)', color: 'white', padding: '12px 25px',
                    borderRadius: '50px', zIndex: 10001, fontWeight: 'bold', boxShadow: '0 4px 15px rgba(0,0,0,0.4)',
                    fontSize: '1rem', border: '2px solid rgba(255,255,255,0.2)'
                }}>
                    ⚠️ {errorMsg}
                </div>
            )}

            <button className="rule-btn-fixed" onClick={openRules}>📜 게임 족보</button>

            <div className="game-header">
                <div className="header-left">
                    <span className="set-info">ROUND {game.currentRound || game.CurrentRound}</span>
                    <span className="room-info">ROOM: {roomId}</span>
                </div>
                <div
                    className="player-scores"
                    onClick={() => setShowScoreHistory(true)}
                    style={{ cursor: 'pointer' }}
                    title="라운드별 점수 히스토리 보기"
                >
                    {players.map((p: any) => (
                        <span key={p.playerId || p.PlayerId} className={`score-item ${(p.playerId || p.PlayerId) === myId ? "my-score" : ""} ${currentTurnId === (p.playerId || p.PlayerId) ? "turn-highlight" : ""}`}>
                            {p.name || p.Name}: {p.totalScore ?? p.TotalScore ?? 0}점
                        </span>
                    ))}
                </div>
                <button className="exit-btn" onClick={handleExit}>기권</button>
            </div>

            <div className="game-table-area">
                <div className="table-oval" style={{ position: 'relative' }}>
                    <div style={{ position: 'absolute', top: '50%', left: '50%', transform: 'translate(-50%, -50%)', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '20px', zIndex: 10 }}>
                        {/* 덱과 버린 카드 레이아웃 */}
                        <div style={{ display: 'flex', gap: '15px', alignItems: 'center' }}>
                            <div className={`card-ui deck ${canDraw ? 'can-action' : ''}`} 
                                style={{
                                    width: '100px',
                                    height: '140px',
                                    cursor: canDraw ? 'pointer' : 'default'
                                }}
                                onClick={() => {
                                    if (canDraw) {
                                        playSound("draw-card");
                                        connection.invoke("DrawCard", roomId);
                                    }
                                }}>
                                <span className="label">DECK</span>
                                <div className="count">{deckCount}</div>
                                {isHost && deckCount === 0 && <button className="reshuffle-badge" onClick={(e) => { e.stopPropagation(); handleReshuffle(); }}>🔄 셔플</button>}
                            </div>
                            
                            {(() => {
                                const discardImageSrc = lastDiscarded
                                    ? getCardImageSrc(lastDiscarded.rank || lastDiscarded.Rank)
                                    : null;
                                return (
                                    <div className={`card-ui discard ${(lastDiscarded?.color || lastDiscarded?.Color) === 'Red' ? 'red' : 'black'}`}
                                        onClick={() => setShowDiscardModal(true)}
                                        style={{
                                            width: '100px',
                                            height: '140px',
                                            cursor: 'pointer',
                                            display: 'flex',
                                            flexDirection: 'column',
                                            justifyContent: 'center',
                                            alignItems: 'center',
                                            border: '2px solid #333',
                                            borderRadius: '10px',
                                            backgroundImage: discardImageSrc ? `url(${discardImageSrc})` : undefined,
                                            backgroundSize: 'cover',
                                            backgroundPosition: 'center',
                                            backgroundRepeat: 'no-repeat'
                                        }}
                                    >
                                        {lastDiscarded ? (discardImageSrc ? null : (
                                            <>
                                                <span className="rank">{getRankText(lastDiscarded.rank || lastDiscarded.Rank)}</span>
                                                <span className="suit">{(lastDiscarded.suit || lastDiscarded.Suit) === "Joker" ? "🃏" : (lastDiscarded.suit || lastDiscarded.Suit)}</span>
                                            </>
                                        )) : <span className="empty-label">DROP</span>}
                                    </div>
                                );
                            })()}
                        </div>

                        {/* 액션 버튼 영역 (뻥과 STOP을 가로로 배치) */}
                        <div style={{ display: 'flex', flexDirection: 'row', gap: '15px', alignItems: 'center' }}>
                            {/* 🔥 뻥 버튼 */}
                            <button 
                                className={`interrupt-btn ${canPung ? 'pung-active' : ''}`} 
                                onClick={() => { if (canPung) { playSound("pung"); connection.invoke("InterruptDiscard", roomId); } }}
                                disabled={!canPung}
                                style={{ 
                                    padding: '12px 25px', 
                                    borderRadius: '10px', 
                                    fontWeight: 'bold', 
                                    fontSize: '1.1rem', 
                                    transition: '0.3s',
                                    minWidth: '100px'
                                }}
                            >
                                {canPung ? "🔥 뻥!!" : "뻥"}
                            </button>

                            {/* STOP 버튼 추가 (뻥 버튼 오른쪽에 배치됨) */}
                            <button 
                                className={`interrupt-btn ${canStop ? 'stop-active' : ''}`} 
                                onClick={handleStop}
                                disabled={!canStop}
                                style={{ 
                                    padding: '12px 25px', 
                                    borderRadius: '10px', 
                                    fontWeight: 'bold', 
                                    fontSize: '1.1rem',
                                    opacity: canStop ? 1 : 0.5, 
                                    cursor: canStop ? 'pointer' : 'default',
                                    backgroundColor: canStop ? '#f1c40f' : '#7f8c8d', 
                                    color: canStop ? '#2c3e50' : 'white', 
                                    border: 'none',
                                    minWidth: '100px',
                                    transition: '0.3s'
                                }}
                            >
                                {canStop ? "🛑 STOP" : "STOP"}
                            </button>
                            <button
                                className="win-btn highlight"
                                onClick={() => {
                                    connection.invoke("ToggleNaturalBagaji", roomId);
                                }}
                            >
                                자연바가지 {game.isNaturalBagajiEnabled ? "ON" : "OFF"}
                            </button>
                        </div>
                    </div>

                    {others.map((player: any, idx: number) => {
                        // 🟢 바가지 체크 — 상대 손패는 마스킹되어 내려오므로 서버가 계산한 값 사용
                        const isBagaji = player.isBagaji ?? player.IsBagaji ?? false;
                        const seat = seatPosition(idx + 1, others.length + 1);

                        return (
                            <div key={player.playerId || player.PlayerId} className={`player-box ${currentTurnId === (player.playerId || player.PlayerId) ? 'active-turn' : ''}`} style={{ position: 'absolute', top: seat.top, left: seat.left, transform: 'translate(-50%, -50%)' }}>
                                <div className="player-name">{player.name || player.Name}</div>
                                <div className="opponent-card-back">{player.handCount ?? player.HandCount ?? (player.hand || player.Hand)?.length ?? 0}</div>
                                
                                {/* 🔴 바가지 표시 추가 */}
                                {isBagaji && (
                                    <div className="bagaji-label" style={{ 
                                        marginTop: '5px', 
                                        color: '#e74c3c', 
                                        fontWeight: 'bold', 
                                        fontSize: '0.9rem',
                                        textShadow: '0 0 5px rgba(255,255,255,0.5)',
                                        textAlign: 'center',
                                        animation: 'pulse 1s infinite'
                                    }}>
                                        🔥 바가지
                                    </div>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>

            <div className="hand-area">
                <div className="turn-status-container" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: '15px 0' }}>
                    <span className={`status-text ${isMyTurn || canPung ? "active-text" : ""}`} style={{ fontSize: '1.2rem', fontWeight: 'bold', marginRight: '15px', color: canPung ? '#e74c3c' : 'inherit' }}>
                        {isMyTurn ? (canDraw ? "▲ 카드를 뽑으세요" : "▼ 버릴 카드를 선택하세요") : (canPung ? "🔥 지금 바로 '뻥'이 가능합니다!" : "상대방의 턴입니다...")}
                    </span>
                    {/* 🏆 승리 선언 버튼: 내 턴 + 선언 가능할 때만 */}
                    {isMyTurn && canDeclareWin && (
                        <button
                            className="win-btn highlight"
                            onClick={() => connection.invoke("DeclareWin", roomId)}
                        >
                            🏆 승리 선언
                        </button>
                    )}
                </div>

                {winHint && (
                    <div className={`win-hint ${winHint.isWinning ? "winning" : ""}`}>
                        {winHint.isWinning
                            ? `🏆 지금 승리 선언 가능 — ${winHint.winType} (${winHint.score}점)${canDeclareWin ? "" : " · 2번째 턴부터 선언 가능"}`
                            : `💡 ${formatOuts(winHint.outs ?? [])}`}
                    </div>
                )}

                <div className="cards-in-hand" style={{ display: 'flex', gap: '10px', justifyContent: 'center', flexWrap: 'nowrap', paddingBottom: '20px' }}>
                    {sortCards(myHand).map((card: any) => {
                        const rankText = getRankText(card.rank || card.Rank);
                        const suitText = (card.suit === "Joker" || card.Suit === "Joker") ? "🃏" : (card.suit || card.Suit);
                        const cardKey = getCardKey(card);
                        const isNew = cardKey === lastDrawnCardKey;
                        const imageSrc = getCardImageSrc(card.rank || card.Rank);
                        const rankKey = String(card.rank ?? card.Rank ?? "").toUpperCase();
                        const groupClass = (rankKey === "JOKER" || rankKey === "JK") ? "rank-group-joker" : (rankGroups[rankKey] ?? "");

                        return (
                            <div key={cardKey}
                                className={`card-ui my-card ${(card.color || card.Color) === 'Red' ? 'red' : 'black'} ${isNew ? "new-card-highlight" : ""} ${groupClass}`}
                                style={{
                                    width: '90px', height: '130px',
                                    background: rankText === "JK" ? "#f1c40f" : "white",
                                    backgroundImage: imageSrc ? `url(${imageSrc})` : undefined,
                                    backgroundSize: 'cover',
                                    backgroundPosition: 'center',
                                    backgroundRepeat: 'no-repeat',
                                    cursor: canDiscardOrWin ? 'pointer' : 'default',
                                    borderRadius: '10px', display: 'flex', flexDirection: 'column',
                                    justifyContent: 'space-between', padding: '10px',
                                    boxShadow: '0 4px 8px rgba(0,0,0,0.3)', transition: 'transform 0.1s'
                                }}
                                onClick={() => { if (canDiscardOrWin) { playSound("play-card"); connection.invoke("PlayCard", roomId, card); } }}>
                                {/* 화투 모드(imageSrc)는 SVG 안에 숫자·무늬가 들어 있으므로 글자를 겹쳐 그리지 않음 */}
                                {!imageSrc && (
                                    <>
                                        <div className="card-corners">
                                            <span className="rank">{rankText}</span>
                                            <span className="rank">{rankText}</span>
                                        </div>
                                        <span className="suit" style={{ fontSize: '2.2rem', textAlign: 'center' }}>{suitText}</span>
                                        <div className="card-corners bottom">
                                            <span className="rank">{rankText}</span>
                                            <span className="rank">{rankText}</span>
                                        </div>
                                    </>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>

            {/* 1. 라운드 결과창: showRoundResult가 true이고 게임이 완전히 끝나지 않았을 때 표시 */}
            {showRoundResult && !game.isFinished && (
                <div className="discard-modal-overlay fade-in-2s">
                    <div className="discard-modal-content" style={{ textAlign: 'center' }}>
                        <h2 style={{ color: '#f1c40f', marginBottom: '20px' }}>ROUND RESULT</h2>
                        
                        {/* 🏆 우승자 패 표시 영역 (로그 포함) */}
                        <div style={{ marginBottom: '25px', padding: '15px', background: 'rgba(255,255,255,0.05)', borderRadius: '12px' }}>
                            <p style={{ color: '#aaa', fontSize: '0.9rem', marginBottom: '10px' }}>우승자 카드 구성</p>
                            <div style={{ 
                                display: 'flex', 
                                gap: '8px', 
                                justifyContent: 'center', 
                                flexWrap: 'wrap', 
                                minHeight: '70px' 
                            }}> 
                                {(() => {
                                    const winnerCards = game.WinnerHand || game.winnerHand;

                                    if (!winnerCards || winnerCards.length === 0) {
                                        return (
                                            <div style={{ color: '#e74c3c' }}>
                                                <p>승리 카드를 불러올 수 없습니다.</p>
                                            </div>
                                        );
                                    }

                                    return sortCards(winnerCards).map((card: any, i: number) => {
                                        // 카드 개별 데이터 확인용 로그
                                        if (i === 0) console.log("4. Sample Card Object:", card);

                                        const imageSrc = getCardImageSrc(card.Rank || card.rank);

                                        return (
                                            <div key={i} style={{
                                                width: '50px', height: '70px', background: 'white', borderRadius: '5px',
                                                backgroundImage: imageSrc ? `url(${imageSrc})` : undefined,
                                                backgroundSize: 'cover',
                                                backgroundPosition: 'center',
                                                backgroundRepeat: 'no-repeat',
                                                display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center',
                                                color: (card.Color || card.color) === 'Red' ? '#e74c3c' : '#2c3e50',
                                                border: '1px solid #ddd', fontSize: '0.8rem',
                                                boxShadow: '0 2px 4px rgba(0,0,0,0.2)'
                                            }}>
                                                {!imageSrc && (
                                                    <>
                                                        <span style={{ fontWeight: 'bold' }}>{getRankText(card.Rank || card.rank)}</span>
                                                        <span style={{ fontSize: '1.2rem' }}>
                                                            {(card.Suit || card.suit) === "Joker" ? "🃏" : (card.Suit || card.suit)}
                                                        </span>
                                                    </>
                                                )}
                                            </div>
                                        );
                                    });
                                })()}
                            </div>
                        </div>

                        <div style={{ marginBottom: '20px', padding: '12px', background: 'rgba(241, 196, 15, 0.1)', borderRadius: '8px', border: '1px solid #f1c40f' }}>
                            <span style={{ color: '#f1c40f', fontWeight: 'bold' }}>판정 결과: </span>
                            <span style={{ color: '#ffffff', fontSize: '1.2rem', fontWeight: 'bold', marginLeft: '8px' }}>
                                {game.LastWinType || game.lastWinType || "족보 확인 중..."}
                            </span>
                        </div>

                        <div style={{ margin: '20px 0', color: 'white' }}>
                            <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '1px solid #555' }}>
                                        <th style={{ padding: '10px' }}>플레이어</th>
                                        <th style={{ padding: '10px' }}>획득 점수</th>
                                        <th style={{ padding: '10px' }}>누적 점수</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {(game.Players || game.players || []).map((p: any) => {
                                        const currentScore = p.Score !== undefined ? p.Score : (p.score ?? 0);
                                        return (
                                            <tr key={p.PlayerId || p.playerId} style={{ borderBottom: '1px solid #444' }}>
                                                <td style={{ padding: '10px' }}>{p.Name || p.name}</td>
                                                <td style={{ 
                                                    padding: '10px', 
                                                    color: currentScore <= 0 ? '#2ecc71' : '#e74c3c',
                                                    fontWeight: 'bold'
                                                }}>
                                                    {currentScore > 0 ? `+${currentScore}` : currentScore}
                                                </td>
                                                <td style={{ padding: '10px' }}>
                                                    {p.TotalScore !== undefined ? p.TotalScore : (p.totalScore ?? 0)} 점
                                                </td>
                                            </tr>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                        {isHost ? (
                            <button onClick={handleNextRoundRequest} style={{ padding: '15px 30px', background: '#27ae60', color: 'white', border: 'none', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}>
                                다음 라운드 시작
                            </button>
                        ) : (
                            <p style={{ color: '#bdc3c7' }}>방장이 다음 라운드를 준비 중입니다...</p>
                        )}
                    </div>
                </div>
            )}

            {/* 2. 최종 결과창: game.isFinished가 true일 때만 표시 (1라운드 게임인 경우 바로 이 창이 뜸) */}
            {game.isFinished && (
                <div className="discard-modal-overlay fade-in-2s" style={{ backgroundColor: 'rgba(0, 0, 0, 0.9)', zIndex: 2000 }}>
                    <div className="discard-modal-content" style={{ textAlign: 'center', border: '2px solid #f1c40f', padding: '40px' }}>
                        <h1 style={{ color: '#f1c40f', fontSize: '2.5rem', marginBottom: '10px' }}>
                            {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "기권" : "게임 종료"}
                        </h1>
                        
                        <div style={{ marginBottom: '30px', padding: '20px', background: 'rgba(241, 196, 15, 0.1)', borderRadius: '12px' }}>
                            <span style={{ color: '#bdc3c7', display: 'block', marginBottom: '5px' }}>
                                {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "기권 승리자" : "최종 우승자"}
                            </span>
                            <span style={{ color: '#ffffff', fontSize: '2rem', fontWeight: 'bold' }}>
                                👑 {game.gameWinnerName || game.GameWinnerName || game.winnerName || game.WinnerName || "-"}
                            </span>

                            {/* 🏆 마지막 라운드 승자의 카드 — 그 사람이 게임 승자일 때만 노출 (다른 사람 패를 우승자 것처럼 보이지 않도록) */}
                            {(game.gameWinnerIds ?? game.GameWinnerIds ?? []).includes(game.winnerPlayerId ?? game.WinnerPlayerId) && (
                            <div style={{ display: 'flex', gap: '10px', justifyContent: 'center', marginTop: '20px' }}>
                                {sortCards(game.winnerHand || []).map((card: any, i: number) => {
                                    const imageSrc = getCardImageSrc(card.rank || card.Rank);
                                    return (
                                        <div key={i} style={{
                                            width: '60px', height: '85px', background: 'white', borderRadius: '6px',
                                            backgroundImage: imageSrc ? `url(${imageSrc})` : undefined,
                                            backgroundSize: 'cover',
                                            backgroundPosition: 'center',
                                            backgroundRepeat: 'no-repeat',
                                            display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center',
                                            color: (card.color || card.Color) === 'Red' ? '#e74c3c' : '#2c3e50',
                                            boxShadow: '0 0 15px rgba(241, 196, 15, 0.5)'
                                        }}>
                                            <span style={{ fontWeight: 'bold', fontSize: '1rem' }}>{getRankText(card.rank || card.Rank)}</span>
                                            <span style={{ fontSize: '1.8rem' }}>{(card.suit || card.Suit) === "Joker" ? "🃏" : (card.suit || card.Suit)}</span>
                                        </div>
                                    );
                                })}
                            </div>
                            )}

                            <div style={{ color: '#f1c40f', marginTop: '10px' }}>
                                판정 족보: {(game.winnerName || game.WinnerName || "").includes("(기권)") ? "상대방 기권" : (game.lastWinType || game.LastWinType || "게임 종료")}
                            </div>
                        </div>

                        <div style={{ margin: '20px 0', maxHeight: '300px', overflowY: 'auto' }}>
                            <table style={{ width: '100%', color: 'white', borderCollapse: 'collapse' }}>
                                <thead>
                                    <tr style={{ borderBottom: '2px solid #f1c40f', height: '40px' }}>
                                        <th>순위</th>
                                        <th>플레이어</th>
                                        <th>최종 총점</th>
                                    </tr>
                                </thead>
                                <tbody>
                                    {[...(game.players || [])]
                                        .sort((a, b) => (a.totalScore ?? a.TotalScore ?? 0) - (b.totalScore ?? b.TotalScore ?? 0))
                                        .map((p, index) => (
                                            <tr key={p.playerId || p.PlayerId} style={{ height: '45px', borderBottom: '1px solid #333' }}>
                                                <td>{index + 1}위</td>
                                                <td>{p.name || p.Name}</td>
                                                <td>{p.totalScore ?? p.TotalScore} 점</td>
                                            </tr>
                                        ))}
                                </tbody>
                            </table>
                        </div>

                        <div style={{ display: 'flex', gap: '10px', justifyContent: 'center' }}>
                            <button
                                onClick={handleReturnToRoom}
                                style={{ padding: '15px 50px', background: '#f1c40f', color: '#000', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}
                            >
                                확인
                            </button>
                            <button
                                onClick={() => setShowStats(true)}
                                style={{ padding: '15px 25px', background: '#7c3aed', color: 'white', borderRadius: '8px', fontWeight: 'bold', cursor: 'pointer' }}
                            >
                                📊 통계
                            </button>
                        </div>
                    </div>
                </div>
            )}
            
            {showStats && <StatsModal myName={myProfile.name} onClose={() => setShowStats(false)} />}

            {showScoreHistory && (() => {
                const history = game.roundScoreHistory ?? game.RoundScoreHistory ?? [];
                const playerOrder = players.map((p: any) => ({
                    id: p.playerId || p.PlayerId,
                    name: p.name || p.Name
                }));
                return (
                    <div className="discard-modal-overlay" onClick={() => setShowScoreHistory(false)}>
                        <div className="discard-modal-content" onClick={e => e.stopPropagation()} style={{ maxWidth: '720px', maxHeight: '85vh', overflowY: 'auto' }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '20px' }}>
                                <h2 style={{ color: '#f1c40f', margin: 0 }}>📊 라운드별 점수 히스토리</h2>
                                <button onClick={() => setShowScoreHistory(false)} style={{ background: '#e74c3c', color: 'white', border: 'none', padding: '8px 15px', borderRadius: '4px', cursor: 'pointer' }}>닫기</button>
                            </div>

                            {history.length === 0 ? (
                                <p style={{ color: '#bdc3c7', textAlign: 'center', padding: '30px' }}>아직 종료된 라운드가 없습니다.</p>
                            ) : (
                                <table style={{ width: '100%', color: 'white', borderCollapse: 'collapse' }}>
                                    <thead>
                                        <tr style={{ borderBottom: '2px solid #f1c40f' }}>
                                            <th style={{ padding: '10px', textAlign: 'left' }}>R</th>
                                            <th style={{ padding: '10px', textAlign: 'left' }}>승자 / 족보</th>
                                            {playerOrder.map((po: any) => (
                                                <th key={po.id} style={{ padding: '10px', textAlign: 'center' }}>{po.name}</th>
                                            ))}
                                        </tr>
                                    </thead>
                                    <tbody>
                                        {history.map((entry: any, idx: number) => {
                                            const round = entry.round ?? entry.Round;
                                            const winnerName = entry.winnerName ?? entry.WinnerName ?? '-';
                                            const winType = entry.winType ?? entry.WinType ?? '';
                                            const scores = entry.scores ?? entry.Scores ?? [];

                                            return (
                                                <tr key={idx} style={{ borderBottom: '1px solid #333' }}>
                                                    <td style={{ padding: '10px', fontWeight: 'bold', color: '#f1c40f' }}>R{round}</td>
                                                    <td style={{ padding: '10px', fontSize: '0.9rem' }}>
                                                        <div style={{ fontWeight: 'bold' }}>{winnerName}</div>
                                                        {winType && <div style={{ color: '#94a3b8', fontSize: '0.8rem' }}>{winType}</div>}
                                                    </td>
                                                    {playerOrder.map((po: any) => {
                                                        const ps = scores.find((s: any) => (s.playerId ?? s.PlayerId) === po.id);
                                                        const roundScore = ps?.score ?? ps?.Score ?? 0;
                                                        const totalScore = ps?.totalScore ?? ps?.TotalScore ?? 0;
                                                        const scoreColor = roundScore < 0 ? '#22c55e' : (roundScore > 0 ? '#ef4444' : '#94a3b8');
                                                        return (
                                                            <td key={po.id} style={{ padding: '10px', textAlign: 'center' }}>
                                                                <div style={{ color: scoreColor, fontWeight: 'bold' }}>
                                                                    {roundScore > 0 ? '+' : ''}{roundScore}
                                                                </div>
                                                                <div style={{ color: '#bdc3c7', fontSize: '0.75rem' }}>({totalScore})</div>
                                                            </td>
                                                        );
                                                    })}
                                                </tr>
                                            );
                                        })}
                                    </tbody>
                                </table>
                            )}

                            <div style={{ marginTop: '15px', fontSize: '0.8rem', color: '#94a3b8', textAlign: 'right' }}>
                                각 셀: <span style={{ color: '#22c55e' }}>녹색=감점(좋음)</span> · <span style={{ color: '#ef4444' }}>빨강=가점(나쁨)</span> · (괄호=누적)
                            </div>
                        </div>
                    </div>
                );
            })()}

            {showDiscardModal && (
                <div className="discard-modal-overlay" onClick={() => setShowDiscardModal(false)}>
                    <div className="discard-modal-content" onClick={e => e.stopPropagation()}>
                        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
                            <h2 style={{ color: 'white' }}>버려진 카드 기록 ({discardPile.length})</h2>
                            <button onClick={() => setShowDiscardModal(false)} style={{ background: '#e74c3c', color: 'white', border: 'none', padding: '8px 15px', borderRadius: '4px' }}>닫기</button>
                        </div>
                        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(65px, 1fr))', gap: '10px' }}>
                            {sortCards(discardPile).map((card: any, idx: number) => {
                                const rank = card.rank ?? card.Rank;
                                const suit = card.suit ?? card.Suit;
                                const isJoker = suit === "Joker";
                                const numberOnly = isHwatuMode && !isJoker;   // 화투 모드: 그림·무늬 없이 달 숫자만
                                return (
                                    <div key={idx} style={{
                                        background: 'white',
                                        color: numberOnly ? '#2c3e50' : ((card.color ?? card.Color) === 'Red' ? 'red' : 'black'),
                                        borderRadius: '8px',
                                        padding: '8px',
                                        textAlign: 'center',
                                        width: '54px', height: '78px', boxSizing: 'border-box',
                                        display: 'flex', flexDirection: 'column', justifyContent: 'center'
                                    }}>
                                        {numberOnly ? (
                                            <div style={{ fontWeight: 'bold', fontSize: '22px' }}>{getRankValue(rank)}</div>
                                        ) : (
                                            <>
                                                <div style={{ fontWeight: 'bold', fontSize: '12px' }}>{getRankText(rank)}</div>
                                                <div style={{ fontSize: '20px' }}>{isJoker ? "🃏" : suit}</div>
                                            </>
                                        )}
                                    </div>
                                );
                            })}
                        </div>
                    </div>
                </div>
            )}
            <div
                className="game-mini-chat"
                style={{
                    position: 'fixed',
                    bottom: '20px',
                    right: '20px',
                    width: `${chatWidth}px`,
                    height: isChatMinimized ? '45px' : `${chatHeight}px`,
                    backgroundColor: 'rgba(15, 23, 42, 0.95)',
                    borderRadius: '12px',
                    display: 'flex',
                    flexDirection: 'column',
                    border: '1px solid #334155',
                    zIndex: 10002,
                    fontSize: '0.85rem',
                    boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
                    overflow: 'hidden',
                    transition: 'height 0.3s ease'
                }}
            >

                {/* ---------------- 리사이즈 핸들 (추가된 부분) ---------------- */}

                {/* 좌측 핸들 */}
                {!isChatMinimized && (
                    <div
                        onMouseDown={() => resizeDirection.current = "left"}
                        style={{
                            position: "absolute",
                            left: 0,
                            top: 0,
                            bottom: 0,
                            width: "8px",
                            cursor: "ew-resize",
                            zIndex: 10003
                        }}
                    />
                )}

                {/* 상단 핸들 */}
                {!isChatMinimized && (
                    <div
                        onMouseDown={() => resizeDirection.current = "top"}
                        style={{
                            position: "absolute",
                            top: 0,
                            left: 0,
                            right: 0,
                            height: "8px",
                            cursor: "ns-resize",
                            zIndex: 10003
                        }}
                    />
                )}

                {/* 좌상단 코너 핸들 */}
                {!isChatMinimized && (
                    <div
                        onMouseDown={() => resizeDirection.current = "corner"}
                        style={{
                            position: "absolute",
                            top: 0,
                            left: 0,
                            width: "14px",
                            height: "14px",
                            cursor: "nwse-resize",
                            zIndex: 10004
                        }}
                    />
                )}

                {/* ------------------------------------------------------------- */}

                {/* 기존 헤더 */}
                <div
                    onClick={() => {
                        setIsChatMinimized(!isChatMinimized);
                        setHasNewMessage(false);
                    }}
                    style={{
                        padding: '10px 12px',
                        background: (hasNewMessage && isChatMinimized) ? '#eab308' : '#1e293b',
                        borderBottom: isChatMinimized ? 'none' : '1px solid #334155',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        cursor: 'pointer',
                        userSelect: 'none',
                        transition: 'background-color 0.3s ease'
                    }}
                >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <div
                            style={{
                                width: '8px',
                                height: '8px',
                                borderRadius: '50%',
                                backgroundColor: connected ? '#22c55e' : '#ef4444'
                            }}
                        />
                        <span
                            style={{
                                fontWeight: 'bold',
                                color: (hasNewMessage && isChatMinimized) ? '#0f172a' : '#cbd5e1',
                                fontSize: '0.75rem',
                                letterSpacing: '0.05em'
                            }}
                        >
                            {(hasNewMessage && isChatMinimized) ? 'NEW MESSAGE!' : 'LIVE CHAT'}
                        </span>
                    </div>

                    <span
                        style={{
                            color: (hasNewMessage && isChatMinimized) ? '#0f172a' : '#94a3b8',
                            fontSize: '0.7rem'
                        }}
                    >
                        {isChatMinimized ? '▲' : '▼'}
                    </span>
                </div>

                {!isChatMinimized && (
                    <div
                        className="chat-messages"
                        style={{
                            flex: 1,
                            overflowY: 'auto',
                            padding: '12px',
                            display: 'flex',
                            flexDirection: 'column',
                            gap: '8px'
                        }}
                    >
                        {messages.map((m, i) => {
                            const splitIdx = m.indexOf(" : ");
                            if (splitIdx === -1) return null;

                            const user = m.substring(0, splitIdx).trim();
                            const msg = m.substring(splitIdx + 3);
                            const isMe = myProfile.name && user === myProfile.name.trim();

                            return (
                                <div
                                    key={i}
                                    style={{
                                        alignSelf: isMe ? 'flex-end' : 'flex-start',
                                        display: 'flex',
                                        flexDirection: 'column',
                                        alignItems: isMe ? 'flex-end' : 'flex-start',
                                        maxWidth: '85%'
                                    }}
                                >
                                    {!isMe && (
                                        <div
                                            style={{
                                                fontSize: '0.65rem',
                                                color: '#94a3b8',
                                                marginBottom: '2px'
                                            }}
                                        >
                                            {user}
                                        </div>
                                    )}
                                    <div
                                        style={{
                                            backgroundColor: isMe ? '#2563eb' : '#334155',
                                            color: 'white',
                                            padding: '6px 12px',
                                            borderRadius: isMe
                                                ? '12px 12px 2px 12px'
                                                : '12px 12px 12px 2px',
                                            fontSize: '0.8rem'
                                        }}
                                    >
                                        {msg}
                                    </div>
                                </div>
                            );
                        })}
                        <div ref={chatRef} />
                    </div>
                )}

                {!isChatMinimized && (
                    <div
                        style={{
                            padding: '10px',
                            background: '#0f172a',
                            borderTop: '1px solid #334155',
                            display: 'flex',
                            gap: '6px'
                        }}
                    >
                        <input
                            className="chat-input"
                            style={{
                                flex: 1,
                                backgroundColor: '#1e293b',
                                border: '1px solid #475569',
                                color: 'white',
                                borderRadius: '6px',
                                padding: '6px 10px',
                                fontSize: '0.8rem',
                                outline: 'none'
                            }}
                            placeholder="메시지 입력..."
                            value={input}
                            onChange={e => setInput(e.target.value)}
                            onKeyDown={e => e.key === 'Enter' && sendMessage()}
                        />
                        <button
                            onClick={sendMessage}
                            disabled={!connected}
                            style={{
                                backgroundColor: '#3b82f6',
                                color: 'white',
                                border: 'none',
                                borderRadius: '6px',
                                padding: '6px 12px',
                                fontSize: '0.75rem',
                                fontWeight: 'bold',
                                cursor: 'pointer',
                                opacity: connected ? 1 : 0.5
                            }}
                        >
                            전송
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
}