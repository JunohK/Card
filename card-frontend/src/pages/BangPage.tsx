import { useEffect, useState, useRef } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { connection, ensureConnection, setActiveRoom } from "../signalr/connection";
import { useTurnTimeout } from "../util/useTurnTimeout";
import { playSound } from "../sound/sound";
import { pushHistory } from "../util/gameHistory";

// MVP placeholder — 게임 진행 UI는 다음 phase에서 풍성하게 만듦.
// 지금은 동작 검증용 최소 화면: 손패 + 플레이어 목록 + 액션 버튼 + 로그.

type Role = 0 | 1 | 2 | 3;
type CardType = number;

type PendingType = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11; // ...Pedro/Jesse/Kit/LuckyDukeReveal
interface BangPending {
    type: PendingType;
    targetPlayerId?: string;
    sourcePlayerId?: string;
    cardId?: number;
    pickToHand?: boolean;
    responderQueue?: string[];
    revealedCards?: BangCard[];
    context?: string;
}

interface BangCard { id: number; type: CardType; suit?: number; rank?: number; }

// 카드 suit/rank 표시 유틸
const SUIT_SYMBOLS = ['♥', '♦', '♣', '♠'];
const SUIT_COLORS_ON_DARK = ['#fca5a5', '#fca5a5', '#cbd5e1', '#cbd5e1'];
// 로그 메시지에 카드명이 포함되면 그 자리에 SVG 아이콘 인라인 삽입
// 메시지 텍스트를 토큰으로 쪼개서 카드명 매칭하면 <img>로 교체
function renderLogWithIcons(msg: string): React.ReactNode[] {
    // CARD_NAMES의 값을 길이 내림차순으로 정렬 (긴 이름 우선 매칭)
    const names = Object.entries(CARD_NAMES)
        .map(([id, name]) => ({ id: Number(id), name }))
        .sort((a, b) => b.name.length - a.name.length);
    const parts: React.ReactNode[] = [];
    let remain = msg;
    let key = 0;
    while (remain.length > 0) {
        let hit: { id: number; name: string; idx: number } | null = null;
        for (const c of names) {
            const i = remain.indexOf(c.name);
            if (i >= 0 && (hit == null || i < hit.idx)) {
                hit = { id: c.id, name: c.name, idx: i };
                if (i === 0) break; // 맨 앞 매칭이면 더 찾을 필요 X
            }
        }
        if (!hit) {
            parts.push(<span key={key++}>{remain}</span>);
            break;
        }
        if (hit.idx > 0) parts.push(<span key={key++}>{remain.substring(0, hit.idx)}</span>);
        parts.push(
            <img key={key++}
                src={CARD_IMAGES[hit.id]}
                alt={hit.name}
                onError={(e) => { e.currentTarget.style.display = 'none'; }}
                style={{ width: '14px', height: '14px', verticalAlign: 'middle', filter: 'invert(0.9) sepia(0.3) saturate(2)' }}
            />
        );
        parts.push(<span key={key++}>{hit.name}</span>);
        remain = remain.substring(hit.idx + hit.name.length);
    }
    return parts;
}

function rankLabel(rank?: number): string {
    if (!rank) return '';
    if (rank === 1) return 'A';
    if (rank === 11) return 'J';
    if (rank === 12) return 'Q';
    if (rank === 13) return 'K';
    return String(rank);
}
interface BangPlayer {
    playerId: string;
    name: string;
    role: Role;
    roleRevealed: boolean;
    character: number;
    health: number;
    maxHealth: number;
    hand: BangCard[];
    seatIndex: number;
    weapon?: BangCard | null;
    equipment: BangCard[];
    isBot?: boolean;
}
interface BangState {
    players: BangPlayer[];
    currentTurnPlayerId: string;
    bangsPlayedThisTurn: number;
    isFinished: boolean;
    winnerRole?: Role;
    winnerPlayerIds: string[];
    endReason?: string;
    deck: BangCard[];
    discardPile: BangCard[];
    pending?: BangPending;
}
interface BangRoom {
    roomId: string;
    hostPlayerId?: string;
    bang?: BangState;
    bangResponseTimerSec?: number;
    bangUseDodgeCity?: boolean;
}

const CARD_NAMES: Record<number, string> = {
    0: '뱅!',
    1: '빗나감',
    2: '맥주',
    3: '역마차',
    4: '잡화점',
    5: '웰스파고',
    6: '술집',
    7: '캣 발루',
    8: '패닉',
    9: '인디언',
    10: '개틀링',
    11: '결투',
    12: '볼케이닉(∞)',
    13: '스코필드(2)',
    14: '레밍턴(3)',
    15: '카빈(4)',
    16: '윈체스터(5)',
    17: '머스탱',
    18: '망원경',
    19: '술통',
    20: '다이너마이트',
    21: '감옥',
    22: '주먹질',
    23: '회피',
    24: '위스키',
    25: '데킬라',
    26: '스프링필드',
    27: '버팔로 라이플',
    28: '난투',
    29: '래그타임',
    30: '캉캉',
    31: '철판',
    32: '페퍼박스(3)',
    33: '코네스토가(1)',
    34: '리볼버 카빈(4)',
    35: '텐갤런 모자',
    36: '성경'
};

// 카드 이미지 — game-icons.net (CC BY 3.0, Delapouite / Lorc)
// jsDelivr CDN 사용. 라이선스: https://creativecommons.org/licenses/by/3.0/
const ICON_BASE = "https://cdn.jsdelivr.net/gh/game-icons/icons@master";
const CARD_IMAGES: Record<number, string> = {
    0: `${ICON_BASE}/delapouite/revolver.svg`,        // 뱅!
    1: `${ICON_BASE}/delapouite/avoidance.svg`,       // 빗나감
    2: `${ICON_BASE}/lorc/beer-stein.svg`,            // 맥주
    3: `${ICON_BASE}/delapouite/old-wagon.svg`,       // 역마차
    4: `${ICON_BASE}/delapouite/shop.svg`,            // 잡화점
    5: `${ICON_BASE}/lorc/cash.svg`,                  // 웰스파고
    6: `${ICON_BASE}/delapouite/saloon-doors.svg`,    // 술집
    7: `${ICON_BASE}/lorc/cat.svg`,                   // 캣 발루
    8: `${ICON_BASE}/lorc/screaming.svg`,             // 패닉
    9: `${ICON_BASE}/lorc/feather-wound.svg`,         // 인디언
    10: `${ICON_BASE}/delapouite/machine-gun-magazine.svg`, // 개틀링
    11: `${ICON_BASE}/lorc/crossed-pistols.svg`,      // 결투
    12: `${ICON_BASE}/delapouite/revolver.svg`,       // 볼케이닉
    13: `${ICON_BASE}/delapouite/revolver.svg`,       // 스코필드
    14: `${ICON_BASE}/delapouite/revolver.svg`,       // 레밍턴
    15: `${ICON_BASE}/delapouite/revolver.svg`,       // 카빈
    16: `${ICON_BASE}/delapouite/revolver.svg`,       // 윈체스터
    17: `${ICON_BASE}/delapouite/horse-head.svg`,     // 머스탱
    18: `${ICON_BASE}/delapouite/telescope.svg`,      // 망원경
    19: `${ICON_BASE}/delapouite/barrel.svg`,         // 술통
    20: `${ICON_BASE}/delapouite/dynamite.svg`,       // 다이너마이트
    21: `${ICON_BASE}/lorc/imprisoned.svg`,           // 감옥
    22: `${ICON_BASE}/lorc/punch-blast.svg`,          // 주먹질
    23: `${ICON_BASE}/delapouite/avoidance.svg`,      // 회피
    24: `${ICON_BASE}/lorc/martini.svg`,              // 위스키
    25: `${ICON_BASE}/delapouite/sombrero.svg`,       // 데킬라
    26: `${ICON_BASE}/delapouite/revolver.svg`,       // 스프링필드
    27: `${ICON_BASE}/delapouite/buffalo-head.svg`,   // 버팔로 라이플
    28: `${ICON_BASE}/lorc/punch.svg`,                // 난투
    29: `${ICON_BASE}/delapouite/musical-notes.svg`,  // 래그타임
    30: `${ICON_BASE}/delapouite/musical-notes.svg`,  // 캉캉
    31: `${ICON_BASE}/delapouite/metal-plate.svg`,    // 철판
    32: `${ICON_BASE}/delapouite/revolver.svg`,       // 페퍼박스
    33: `${ICON_BASE}/delapouite/revolver.svg`,       // 코네스토가
    34: `${ICON_BASE}/delapouite/revolver.svg`,       // 리볼버 카빈
    35: `${ICON_BASE}/delapouite/western-hat.svg`,    // 텐갤런 모자
    36: `${ICON_BASE}/delapouite/bookshelf.svg`       // 성경
};

// 카드별 대표 이모지 — 시각적 식별용 (이미지 로드 실패 fallback)
const CARD_ICONS: Record<number, string> = {
    0: '💥',   // 뱅!
    1: '🛡',   // 빗나감
    2: '🍺',   // 맥주
    3: '🐴',   // 역마차
    4: '🏪',   // 잡화점
    5: '💰',   // 웰스파고
    6: '🍻',   // 술집
    7: '😼',   // 캣 발루
    8: '😱',   // 패닉
    9: '🪶',   // 인디언
    10: '🔫',  // 개틀링
    11: '⚔️',  // 결투
    12: '🌋',  // 볼케이닉
    13: '🔫',  // 스코필드
    14: '🔫',  // 레밍턴
    15: '🔫',  // 카빈
    16: '🔫',  // 윈체스터
    17: '🐎',  // 머스탱
    18: '🔭',  // 망원경
    19: '🛢',  // 술통
    20: '💣',  // 다이너마이트
    21: '⛓',   // 감옥
    22: '👊',  // 주먹질
    23: '💨',  // 회피
    24: '🥃',  // 위스키
    25: '🍹',  // 데킬라
    26: '🎯',  // 스프링필드
    27: '🦬',  // 버팔로 라이플
    28: '🥊',  // 난투
    29: '🎼',  // 래그타임
    30: '💃',  // 캉캉
    31: '🛡',  // 철판
    32: '🔫',  // 페퍼박스
    33: '🔫',  // 코네스토가
    34: '🔫',  // 리볼버 카빈
    35: '🤠',  // 텐갤런 모자
    36: '📖'   // 성경
};

const CARD_ABILITIES: Record<number, string> = {
    0: '뱅! — 거리 내 대상 1명에게 1 데미지 (빗나감으로 회피)',
    1: '빗나감 — 뱅! 무효화',
    2: '맥주 — HP +1 (마지막 2명 이하일 땐 사용 불가)',
    3: '역마차 — 카드 2장 뽑기',
    4: '잡화점 — 살아있는 인원수만큼 카드 펴기, 시계방향으로 각자 1장 선택',
    5: '웰스파고 — 카드 3장 뽑기',
    6: '술집 — 살아있는 모두 HP +1',
    7: '캣 발루 — 거리 무관, 대상의 카드(손/장비/무기) 1장 즉시 버림',
    8: '패닉 — 거리 1, 대상의 카드(손/장비/무기) 1장 가져옴',
    9: '인디언 — 본인 외 모두가 뱅!으로 응답 안 하면 1 데미지',
    10: '개틀링 — 본인 외 모두가 빗나감으로 응답 안 하면 1 데미지',
    11: '결투 — 대상과 뱅! 번갈아 던지기. 못 내는 쪽 1 데미지',
    12: '볼케이닉 — 무기 사거리 1, 턴당 뱅! 무제한',
    13: '스코필드 — 무기 사거리 2',
    14: '레밍턴 — 무기 사거리 3',
    15: '카빈 — 무기 사거리 4',
    16: '윈체스터 — 무기 사거리 5',
    17: '머스탱 — 장비. 본인이 받을 때 거리 +1',
    18: '망원경 — 장비. 본인이 보낼 때 거리 -1',
    19: '술통 — 장비. 뱅! 받을 때 펴기 ♥이면 자동 회피',
    20: '다이너마이트 — 장비. 본인 턴 시작 시 펴기. ♠ 2~9이면 3 데미지 + 버림, 아니면 다음 사람으로',
    21: '감옥 — 대상에게 장착. 그 사람 턴 시작 시 펴기 ♥이면 탈옥, 아니면 턴 스킵',
    22: '주먹질 (확장) — 뱅! 효과, 거리 1 강제. 뱅! 카운트 공유',
    23: '회피 (확장) — 빗나감 효과 + 응답 시 카드 1장 보너스 드로우',
    24: '위스키 (확장) — HP +2 회복. 사용 시 손패 1장 추가 버림',
    25: '데킬라 (확장) — 대상 1명 HP +1 회복 (본인 포함). 사용 시 손패 1장 추가 버림',
    26: '스프링필드 (확장) — 손 1장 추가 버림 + 뱅! 효과 (사거리 무한)',
    27: '버팔로 라이플 (확장) — 뱅! 효과 (사거리 무한, 뱅! 카운트와 별개)',
    28: '난투 (확장) — 손 1장 추가 버림 + 다른 모두 손패 1장씩 강제 버림',
    29: '래그타임 (확장) — 손 1장 추가 버림 + 대상 손에서 1장 가져옴 (거리 무한)',
    30: '캉캉 (확장) — 대상의 카드(손/장비/무기) 1장 즉시 버림 (거리 무한)',
    31: '철판 (확장) — 장비. 뱅! 받기 전 펴기 ♠이면 회피',
    32: '페퍼박스 (확장) — 무기 사거리 3',
    33: '코네스토가 (확장) — 무기 사거리 1',
    34: '리볼버 카빈 (확장) — 무기 사거리 4',
    35: '텐갤런 모자 (확장) — 장비. 뱅! 받을 때 펴기 ♠이면 회피',
    36: '성경 (확장) — 장비. 본인 턴 시작 시 펴기 ♣이면 카드 1장 추가'
};

const CHARACTER_NAMES: Record<number, string> = {
    0: '바트 캐시디', 1: '폴 리그렛', 2: '로즈 둘란', 3: '윌리 더 키드',
    4: '블랙 잭', 5: '칼라미티 자넷', 6: '엘 그링고', 7: '제시 존스',
    8: '주르도네', 9: '킷 칼슨', 10: '럭키 듀크', 11: '페드로 라미레즈',
    12: '시드 케첨', 13: '슬랩 더 킬러', 14: '수지 라파예트', 15: '벌처 샘',
    16: '아파치 키드', 17: '벨 스타', 18: '빌 노페이스', 19: '척 웬감',
    20: '닥 홀리데이', 21: '엘레나 푸엔테', 22: '그렉 디거', 23: '허브 헌터'
};

// 캐릭터별 SVG 아이콘 — game-icons.net (CC BY 3.0)
const CHARACTER_IMAGES: Record<number, string> = {
    0: `${ICON_BASE}/delapouite/barbarian.svg`,        // 바트 캐시디
    1: `${ICON_BASE}/delapouite/avoidance.svg`,        // 폴 리그렛
    2: `${ICON_BASE}/delapouite/hunter-eyes.svg`,      // 로즈 둘란
    3: `${ICON_BASE}/delapouite/cowboy-boot.svg`,      // 윌리 더 키드
    4: `${ICON_BASE}/lorc/ace.svg`,                    // 블랙 잭
    5: `${ICON_BASE}/delapouite/female.svg`,           // 칼라미티 자넷
    6: `${ICON_BASE}/delapouite/sombrero.svg`,         // 엘 그링고
    7: `${ICON_BASE}/delapouite/cowboy-holster.svg`,   // 제시 존스
    8: `${ICON_BASE}/delapouite/barrel.svg`,           // 주르도네
    9: `${ICON_BASE}/lorc/spyglass.svg`,               // 킷 칼슨
    10: `${ICON_BASE}/delapouite/duck.svg`,            // 럭키 듀크
    11: `${ICON_BASE}/delapouite/wanted-reward.svg`,   // 페드로 라미레즈
    12: `${ICON_BASE}/delapouite/medicines.svg`,       // 시드 케첨
    13: `${ICON_BASE}/lorc/desert-skull.svg`,          // 슬랩 더 킬러
    14: `${ICON_BASE}/delapouite/woman-elf-face.svg`,  // 수지 라파예트
    15: `${ICON_BASE}/delapouite/eagle-head.svg`,      // 벌처 샘
    16: `${ICON_BASE}/lorc/feather-wound.svg`,         // 아파치 키드
    17: `${ICON_BASE}/lorc/witch-flight.svg`,          // 벨 스타
    18: `${ICON_BASE}/delapouite/robber-mask.svg`,     // 빌 노페이스
    19: `${ICON_BASE}/delapouite/dice-target.svg`,     // 척 웬감
    20: `${ICON_BASE}/delapouite/nurse-male.svg`,      // 닥 홀리데이
    21: `${ICON_BASE}/delapouite/nurse-female.svg`,    // 엘레나 푸엔테
    22: `${ICON_BASE}/delapouite/miner.svg`,           // 그렉 디거
    23: `${ICON_BASE}/lorc/hood.svg`                   // 허브 헌터
};

const CHARACTER_ABILITIES: Record<number, string> = {
    0: '데미지 1당 카드 1장 자동 드로우 (바트 캐시디)',
    1: '본인이 받을 때 거리 +1 (폴 리그렛)',
    2: '본인이 보낼 때 거리 -1 (로즈 둘란)',
    3: '뱅! 무제한 사용 (윌리 더 키드)',
    4: '드로우 페이즈 2번째 카드 ♥/♦이면 카드 1장 추가 (블랙 잭)',
    5: '뱅! ↔ 빗나감 호환 + 회피/주먹질 호환 (칼라미티 자넷)',
    6: '데미지 받을 때 공격자 손패 1장 강제 버림 (엘 그링고)',
    7: '드로우 페이즈에 다른 사람 손에서 1장 가져오기 가능 (제시 존스)',
    8: '술통 능력 기본 보유 — ♥ 펴면 뱅! 회피 (주르도네)',
    9: '드로우 페이즈에 3장 펴서 2장 선택 (킷 칼슨)',
    10: '펴기(draw!) 시 2장 펴서 유리한 것 선택 (럭키 듀크) — 수동',
    11: '드로우 페이즈에 버린 카드 top 1장 + 덱 1장 가능 (페드로 라미레즈)',
    12: '손패 2장 버려서 HP +1 (시드 케첨) — 언제든',
    13: '뱅! 시 빗나감 2장 필요 (슬랩 더 킬러)',
    14: '손패 0장이면 즉시 1장 자동 드로우 (수지 라파예트)',
    15: '다른 사람 사망 시 그 사람 손패 모두 가져옴 (벌처 샘)',
    16: '♦ 카드 효과 무시 — 뱅!/캣 발루/패닉/결투/인디언/개틀링/주먹질/감옥 (아파치 키드)',
    17: '본인 턴 동안 다른 사람 장착 카드 효과 무효 (벨 스타)',
    18: '드로우 페이즈: 1장 + (잃은 HP)장 추가 (빌 노페이스)',
    19: '본인 턴에 HP -1, 카드 +2 (척 웬감) — HP 1이면 불가',
    20: '본인 턴에 손 2장 버려서 뱅! 효과 (닥 홀리데이) — 턴당 1회',
    21: '어떤 카드든 빗나감으로 사용 가능 (엘레나 푸엔테)',
    22: '다른 사람 사망 시 HP +2 (그렉 디거)',
    23: '다른 사람 사망 시 카드 +2 (허브 헌터)'
};

const ROLE_NAMES: Record<Role, string> = {
    0: '🌟 보안관',
    1: '🤝 부보안관',
    2: '🤠 무법자',
    3: '🥷 배신자'
};

function pick<T = any>(obj: any, ...keys: string[]): T | undefined {
    if (!obj) return undefined;
    for (const k of keys) if (obj[k] !== undefined) return obj[k];
    return undefined;
}

export default function BangPage() {
    const { roomId } = useParams<{ roomId: string }>();

    // 재접속(네트워크 끊김 자동 재연결) 시 자동 재입장할 방 등록
    useEffect(() => {
        setActiveRoom(roomId ?? null);
        return () => setActiveRoom(null);
    }, [roomId]);
    const navigate = useNavigate();
    const [room, setRoom] = useState<BangRoom | null>(null);
    const [errorMsg, setErrorMsg] = useState<string | null>(null);
    const [logLines, setLogLines] = useState<string[]>([]);
    const [selectedCardId, setSelectedCardId] = useState<number | null>(null);
    const [selectedTargetId, setSelectedTargetId] = useState<string | null>(null);
    const [discardSelection, setDiscardSelection] = useState<number[]>([]);
    const [revealToast, setRevealToast] = useState<{ id: number; text: string } | null>(null);
    const [responseTimer, setResponseTimer] = useState<number>(30);
    const [showDiscardModal, setShowDiscardModal] = useState(false);
    const [discardFilter, setDiscardFilter] = useState<string>("");
    const [handSort, setHandSort] = useState<'default' | 'type' | 'suit' | 'rank'>(() => {
        const v = localStorage.getItem("bangHandSort");
        return (v === 'type' || v === 'suit' || v === 'rank') ? v : 'default';
    });
    const [autoDodge, setAutoDodge] = useState<boolean>(() => localStorage.getItem("bangAutoDodge") === "1");
    const [abilityModalCharacter, setAbilityModalCharacter] = useState<number | null>(null);
    const [playingCard, setPlayingCard] = useState<{ id: number; type: number } | null>(null);
    const [chatMessages, setChatMessages] = useState<string[]>([]);
    const [chatInput, setChatInput] = useState("");
    const logEndRef = useRef<HTMLDivElement>(null);

    useEffect(() => {
        let mounted = true;
        const setup = async () => { await ensureConnection(); };

        const normalize = (raw: any): BangRoom => {
            const b = pick<any>(raw, 'bang', 'Bang');
            const bang: BangState | undefined = b ? {
                players: (pick<any[]>(b, 'players', 'Players') ?? []).map((p: any) => ({
                    playerId: pick(p, 'playerId', 'PlayerId') ?? '',
                    name: pick(p, 'name', 'Name') ?? '',
                    role: pick(p, 'role', 'Role') ?? 2,
                    roleRevealed: pick(p, 'roleRevealed', 'RoleRevealed') ?? false,
                    character: pick(p, 'character', 'Character') ?? 0,
                    health: pick(p, 'health', 'Health') ?? 0,
                    maxHealth: pick(p, 'maxHealth', 'MaxHealth') ?? 4,
                    hand: (pick<any[]>(p, 'hand', 'Hand') ?? []).map((c: any) => ({
                        id: pick(c, 'id', 'Id') ?? 0,
                        type: pick(c, 'type', 'Type') ?? 0,
                        suit: pick(c, 'suit', 'Suit'),
                        rank: pick(c, 'rank', 'Rank')
                    })),
                    seatIndex: pick(p, 'seatIndex', 'SeatIndex') ?? 0,
                    weapon: (() => {
                        const w = pick<any>(p, 'weapon', 'Weapon');
                        if (!w) return null;
                        return {
                            id: pick(w, 'id', 'Id') ?? 0,
                            type: pick(w, 'type', 'Type') ?? 0,
                            suit: pick(w, 'suit', 'Suit'),
                            rank: pick(w, 'rank', 'Rank')
                        };
                    })(),
                    equipment: (pick<any[]>(p, 'equipment', 'Equipment') ?? []).map((c: any) => ({
                        id: pick(c, 'id', 'Id') ?? 0,
                        type: pick(c, 'type', 'Type') ?? 0,
                        suit: pick(c, 'suit', 'Suit'),
                        rank: pick(c, 'rank', 'Rank')
                    })),
                    isBot: pick<boolean>(p, 'isBot', 'IsBot') ?? false
                })),
                currentTurnPlayerId: pick(b, 'currentTurnPlayerId', 'CurrentTurnPlayerId') ?? '',
                bangsPlayedThisTurn: pick(b, 'bangsPlayedThisTurn', 'BangsPlayedThisTurn') ?? 0,
                isFinished: pick(b, 'isFinished', 'IsFinished') ?? false,
                winnerRole: pick(b, 'winnerRole', 'WinnerRole'),
                winnerPlayerIds: pick(b, 'winnerPlayerIds', 'WinnerPlayerIds') ?? [],
                endReason: pick(b, 'endReason', 'EndReason'),
                deck: pick(b, 'deck', 'Deck') ?? [],
                discardPile: pick(b, 'discardPile', 'DiscardPile') ?? [],
                pending: (() => {
                    const p = pick<any>(b, 'pending', 'Pending');
                    if (!p) return undefined;
                    return {
                        type: pick(p, 'type', 'Type') ?? 0,
                        targetPlayerId: pick(p, 'targetPlayerId', 'TargetPlayerId'),
                        sourcePlayerId: pick(p, 'sourcePlayerId', 'SourcePlayerId'),
                        cardId: pick(p, 'cardId', 'CardId'),
                        pickToHand: pick(p, 'pickToHand', 'PickToHand') ?? false,
                        responderQueue: pick(p, 'responderQueue', 'ResponderQueue') ?? [],
                        revealedCards: (pick<any[]>(p, 'revealedCards', 'RevealedCards') ?? []).map((c: any) => ({
                            id: pick(c, 'id', 'Id') ?? 0,
                            type: pick(c, 'type', 'Type') ?? 0,
                            suit: pick(c, 'suit', 'Suit'),
                            rank: pick(c, 'rank', 'Rank')
                        })),
                        context: pick(p, 'context', 'Context')
                    };
                })()
            } : undefined;
            return {
                roomId: pick(raw, 'roomId', 'RoomId') ?? '',
                hostPlayerId: pick(raw, 'hostPlayerId', 'HostPlayerId'),
                bangResponseTimerSec: pick(raw, 'bangResponseTimerSec', 'BangResponseTimerSec'),
                bangUseDodgeCity: pick(raw, 'bangUseDodgeCity', 'BangUseDodgeCity'),
                bang
            };
        };

        const onRoomUpdated = (raw: any) => {
            if (!mounted) return;
            setRoom(normalize(raw));
        };
        const onGameStarted = onRoomUpdated;
        const onError = (msg: string) => {
            if (!mounted) return;
            setErrorMsg(msg);
            setTimeout(() => mounted && setErrorMsg(null), 3500);
        };
        const onLog = (msg: string) => {
            if (!mounted) return;
            setLogLines(prev => [...prev, msg]);

            // 사운드 트리거 — 우선순위 순으로 분기
            if (/BANG!|Punch/.test(msg)) playSound("bang");
            else if (/💀|사망|폭발|피격|데미지/.test(msg)) playSound("damage");
            else if (/Beer|Saloon|Whisky|Tequila|회복|HP \+/.test(msg)) playSound("heal");
            else if (/장착|장비|🛢|🛡|🔫/.test(msg)) playSound("equip");

            // 펴기 결과 토스트 — Barrel/Dynamite/Lucky Duke 등 ♠♥♦♣ 검출
            const revealMatch = msg.match(/[🛢💥🎲][^♠♥♦♣]*[♠♥♦♣][^—!\n]*/);
            if (revealMatch) {
                setRevealToast({ id: Date.now(), text: revealMatch[0].trim() });
            }
        };
        const onGameEnded = (data: any) => {
            const reason = pick<string>(data, 'reason', 'Reason') ?? '게임 종료';
            const winnerRole = pick<number>(data, 'winnerRole', 'WinnerRole');
            const winnerNames = pick<string[]>(data, 'winnerNames', 'WinnerNames') ?? [];
            const winnerPlayerIds = pick<string[]>(data, 'winnerPlayerIds', 'WinnerPlayerIds') ?? [];
            if (mounted) {
                setLogLines(prev => [...prev, `🏆 ${reason}`]);
                playSound("win");
                const roleLabel = winnerRole === 0 || winnerRole === 1 ? '보안관팀'
                    : winnerRole === 2 ? '무법자'
                    : winnerRole === 3 ? '배신자'
                    : '승자';
                pushHistory({
                    mode: "Bang",
                    winnerName: winnerNames.length > 0 ? `${roleLabel}: ${winnerNames.join(', ')}` : roleLabel,
                    detail: reason,
                    isMyWin: connection.connectionId ? winnerPlayerIds.includes(connection.connectionId) : false
                });
            }
        };

        const onChat = (user: string, msg: string) => {
            if (mounted) setChatMessages(prev => [...prev, `${user} : ${msg}`]);
        };

        connection.on("RoomUpdated", onRoomUpdated);
        connection.on("GameStarted", onGameStarted);
        connection.on("ErrorMessage", onError);
        connection.on("BangLog", onLog);
        connection.on("BangGameEnded", onGameEnded);
        connection.on("ReceiveMessage", onChat);
        setup();

        return () => {
            mounted = false;
            connection.off("RoomUpdated", onRoomUpdated);
            connection.off("GameStarted", onGameStarted);
            connection.off("ErrorMessage", onError);
            connection.off("BangLog", onLog);
            connection.off("BangGameEnded", onGameEnded);
            connection.off("ReceiveMessage", onChat);
        };
    }, [roomId]);

    useEffect(() => {
        logEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [logLines]);

    // 펴기 결과 토스트 자동 숨김 (3초)
    useEffect(() => {
        if (!revealToast) return;
        const id = setTimeout(() => setRevealToast(null), 3000);
        return () => clearTimeout(id);
    }, [revealToast]);

    // 카드 사용 애니메이션 자동 클리어 (0.6초)
    useEffect(() => {
        if (!playingCard) return;
        const id = setTimeout(() => setPlayingCard(null), 600);
        return () => clearTimeout(id);
    }, [playingCard]);

    // 본인 턴 시작 시 사운드 + toast 알림 + 손패 영역 강조
    const prevTurnRef = useRef<string | null>(null);
    const [myTurnFlashKey, setMyTurnFlashKey] = useState<number>(0);
    useEffect(() => {
        const current = room?.bang?.currentTurnPlayerId;
        if (!current) return;
        if (prevTurnRef.current === current) return;
        prevTurnRef.current = current;
        if (current === connection.connectionId) {
            playSound("my-turn");
            setRevealToast({ id: Date.now(), text: '🎯 당신의 차례!' });
            setMyTurnFlashKey(Date.now());
        }
    }, [room?.bang?.currentTurnPlayerId]);

    // 게임 시작 시 본인 역할 + 캐릭터 안내 (한 번만)
    const startNoticeShownRef = useRef(false);
    useEffect(() => {
        if (startNoticeShownRef.current) return;
        const me = room?.bang?.players.find(p => p.playerId === connection.connectionId);
        if (!me || !room?.bang) return;
        startNoticeShownRef.current = true;
        const roleLabel = ['🌟 보안관', '🤝 부보안관', '🤠 무법자', '🥷 배신자'][me.role];
        setRevealToast({ id: Date.now(), text: `${roleLabel} · 🎭 ${CHARACTER_NAMES[me.character]}` });
    }, [room?.bang]);

    // pending 식별 키 (응답 효과들의 deps용) — 선언이 사용보다 앞서야 TDZ 오류 방지
    const pendingKey = room?.bang?.pending
        ? `${room.bang.pending.type}-${room.bang.pending.cardId}-${room.bang.pending.sourcePlayerId}-${room.bang.pending.responderQueue?.[0] ?? ''}`
        : null;

    // 자동 회피 옵션 — pending이 BANG/Gatling이고 본인이 대상이고 빗나감 보유 시 즉시 응답
    useEffect(() => {
        if (!autoDodge) return;
        if (!isMyResponder) return;
        const p = room?.bang?.pending;
        if (!p) return;
        const myPlayer = room?.bang?.players.find(pl => pl.playerId === connection.connectionId);
        if (!myPlayer) return;
        const hasMissed = myPlayer.hand.some(c => c.type === 1 || c.type === 23); // 빗나감 또는 회피
        if (!hasMissed) return;
        if (p.type === 1) {
            connection.invoke("RespondBang", roomId, true);
        } else if (p.type === 6) {
            connection.invoke("RespondGatling", roomId, true);
        }
    }, [pendingKey, autoDodge]);

    // 본인이 응답 대상이 되는 순간 — 사운드 + 토스트
    useEffect(() => {
        if (!isMyResponder) return;
        const p = room?.bang?.pending;
        if (!p) return;
        // 위급한 응답만 사운드 (BANG/Indians/Gatling/Duel)
        if ([1, 5, 6, 7].includes(p.type)) {
            playSound("bang");
            const labels: Record<number, string> = {
                1: '🔫 뱅! — 응답하세요!',
                5: '🪶 인디언! — 응답하세요!',
                6: '🔫 개틀링! — 응답하세요!',
                7: '⚔ 결투! — 응답하세요!'
            };
            setRevealToast({ id: Date.now(), text: labels[p.type] ?? '⚠️ 응답 필요' });
        }
    }, [pendingKey]);

    // 본인 사망 알림 + 데미지 시 화면 흔들기
    const prevHealthRef = useRef<number | null>(null);
    const [shakeKey, setShakeKey] = useState<number>(0);
    useEffect(() => {
        const myPlayer = room?.bang?.players.find(p => p.playerId === connection.connectionId);
        if (!myPlayer) return;
        const prev = prevHealthRef.current;
        prevHealthRef.current = myPlayer.health;
        if (prev !== null && prev > 0 && myPlayer.health === 0) {
            setRevealToast({ id: Date.now(), text: '💀 당신이 사망했습니다' });
            playSound("damage");
            setShakeKey(Date.now()); // 사망 시 흔들기
        } else if (prev !== null && myPlayer.health > prev) {
            setRevealToast({ id: Date.now(), text: `❤ HP +${myPlayer.health - prev}` });
        } else if (prev !== null && myPlayer.health < prev && myPlayer.health > 0) {
            setRevealToast({ id: Date.now(), text: `💥 HP -${prev - myPlayer.health}` });
            playSound("damage");
            setShakeKey(Date.now()); // HP 감소 시 흔들기
        }
    }, [room?.bang?.players]);

    // 응답 타이머 — pending 바뀔 때마다 30초로 리셋 후 카운트다운
    const timerLimit = room?.bangResponseTimerSec ?? 30;
    const [redFlashKey, setRedFlashKey] = useState<number>(0);

    // 본인 응답자 여부 판단 (카운트다운 비프/플래시용)
    const isMyResponder = (() => {
        const p = room?.bang?.pending;
        if (!p) return false;
        if ((p.type === 1 || p.type === 7 || p.type === 11) && p.targetPlayerId === connection.connectionId) return true;
        if ((p.type === 2 || p.type === 5 || p.type === 6) && p.responderQueue?.[0] === connection.connectionId) return true;
        if ((p.type === 4 || p.type === 8 || p.type === 9 || p.type === 10) && p.sourcePlayerId === connection.connectionId) return true;
        return false;
    })();

    // 5초 이하 카운트다운 비프 + 0초 시 빨간 플래시
    const prevTimerRef = useRef<number>(timerLimit);
    useEffect(() => {
        if (!isMyResponder) { prevTimerRef.current = responseTimer; return; }
        if (responseTimer < prevTimerRef.current) {
            if (responseTimer > 0 && responseTimer <= 5) {
                playSound("damage"); // 비프 대용
            } else if (responseTimer === 0) {
                playSound("damage");
                setRedFlashKey(Date.now());
            }
        }
        prevTimerRef.current = responseTimer;
    }, [responseTimer, isMyResponder]);
    useEffect(() => {
        if (!pendingKey) { setResponseTimer(timerLimit); return; }
        setResponseTimer(timerLimit);
        const tickId = setInterval(() => {
            setResponseTimer(t => (t > 0 ? t - 1 : 0));
        }, 1000);
        return () => clearInterval(tickId);
    }, [pendingKey, timerLimit]);

    // 타이머 0 도달 시 본인이 응답 대상이면 자동 데미지 수용 (방치 방지)
    const autoFiredRef = useRef<string | null>(null);
    useEffect(() => {
        if (responseTimer !== 0) return;
        const pending = room?.bang?.pending;
        if (!pending || !pendingKey) return;
        if (autoFiredRef.current === pendingKey) return;
        const myConnId = connection.connectionId;
        if (!myConnId) return;

        const isResponder = (() => {
            if (pending.type === 1 || pending.type === 7) return pending.targetPlayerId === myConnId;
            if (pending.type === 5 || pending.type === 6) return pending.responderQueue?.[0] === myConnId;
            if (pending.type === 2) return pending.responderQueue?.[0] === myConnId;  // General Store
            if (pending.type === 4) return pending.sourcePlayerId === myConnId;        // Cat Balou/Panic/CanCan
            if (pending.type === 8 || pending.type === 9 || pending.type === 10)       // Pedro/Jesse/Kit
                return pending.sourcePlayerId === myConnId;
            if (pending.type === 11) return pending.targetPlayerId === myConnId;       // Lucky Duke
            return false;
        })();
        if (!isResponder) return;

        autoFiredRef.current = pendingKey;

        // 응답형(BANG/Indians/Gatling/Duel) — 데미지 수용 자동
        if ([1, 5, 6, 7].includes(pending.type)) {
            setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — 자동 데미지 수용`]);
            if (pending.type === 1) connection.invoke("RespondBang", roomId, false);
            else if (pending.type === 7) connection.invoke("RespondDuel", roomId, false);
            else if (pending.type === 5) connection.invoke("RespondIndians", roomId, false);
            else if (pending.type === 6) connection.invoke("RespondGatling", roomId, false);
            return;
        }
        // 선택형 — 첫 옵션 자동 선택
        if (pending.type === 2) {
            // General Store — 큐 첫 사람이 첫 카드 선택
            const first = pending.revealedCards?.[0];
            if (first) {
                setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — General Store 자동 첫 카드 선택`]);
                connection.invoke("PickGeneralStoreCard", roomId, first.id);
            }
            return;
        }
        if (pending.type === 4) {
            // Cat Balou/Panic/CanCan — 대상 손패 첫 카드 자동 선택
            const target = room?.bang?.players.find(p => p.playerId === pending.targetPlayerId);
            const first = target?.hand?.[0];
            if (first) {
                setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — 대상 첫 손패 자동 선택`]);
                connection.invoke("PickBangCardFromTarget", roomId, first.id);
            }
            return;
        }
        if (pending.type === 8) {
            // Pedro Ramirez — 덱에서 자동 드로우
            setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — Pedro Ramirez 덱에서 자동`]);
            connection.invoke("ChoosePedroRamirez", roomId, true);
            return;
        }
        if (pending.type === 9) {
            // Jesse Jones — 덱에서 자동 드로우
            setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — Jesse Jones 덱에서 자동`]);
            connection.invoke("ChooseJesseJones", roomId, true, null);
            return;
        }
        if (pending.type === 10) {
            // Kit Carlson — 펴진 3장 중 첫 카드를 덱으로 되돌림 (나머지 2장 손에)
            const first = pending.revealedCards?.[0];
            if (first) {
                setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — Kit Carlson 첫 카드를 덱으로`]);
                connection.invoke("ChooseKitCarlson", roomId, first.id);
            }
            return;
        }
        if (pending.type === 11) {
            // Lucky Duke — 첫 카드 자동 선택
            const first = pending.revealedCards?.[0];
            if (first) {
                setLogLines(prev => [...prev, `⏱ 응답 시간 초과 — Lucky Duke 첫 카드 자동 선택`]);
                connection.invoke("ChooseLuckyDuke", roomId, first.id);
            }
            return;
        }
    }, [responseTimer, pendingKey, room?.bang?.pending, room?.bang?.players, roomId]);

    // 턴 타임아웃 — 응답 대기(pending) 중이 아니면 현재 턴에 대해 카운트다운
    const turnSecLeft = useTurnTimeout(
        roomId,
        room?.bang?.currentTurnPlayerId ?? null,
        !!room?.bang && !room.bang.isFinished && !room.bang.pending,
        (room as any)?.turnTimeoutSec ?? 60
    );

    if (!room?.bang) {
        return (
            <div style={{ minHeight: '100vh', background: '#0f172a', color: '#f1f5f9', padding: '2rem', textAlign: 'center' }}>
                뱅 게임 정보를 불러오는 중... (room: {roomId})
            </div>
        );
    }

    const bang = room.bang;
    const myId = connection.connectionId;
    const isHost = room.hostPlayerId === myId;
    const me = bang.players.find(p => p.playerId === myId);
    // 시계방향 정렬 — 본인 seatIndex 기준 다음 좌석부터
    const sortedOthers = (() => {
        const arr = bang.players.filter(p => p.playerId !== myId);
        if (!me) return arr;
        const N = bang.players.length;
        return arr.slice().sort((a, b) =>
            ((a.seatIndex - me.seatIndex + N) % N) - ((b.seatIndex - me.seatIndex + N) % N)
        );
    })();
    const others = sortedOthers;
    const isMyTurn = bang.currentTurnPlayerId === myId;
    const currentTurnPlayer = bang.players.find(p => p.playerId === bang.currentTurnPlayerId);

    // 좌석 원형 배치 — 본인을 하단 중앙으로 보고 다른 사람들을 위쪽 호에 분포
    const seatStyle = (i: number, n: number): React.CSSProperties => {
        if (n === 0) return {};
        // n=1 → 정중앙 위, n>=2 → 좌상(-160) ~ 우상(-20) 호에 분포
        const startAngle = n === 1 ? -90 : -160;
        const totalArc = n === 1 ? 0 : 140;
        const step = n === 1 ? 0 : totalArc / (n - 1);
        const angle = startAngle + i * step;
        const rad = (angle * Math.PI) / 180;
        const rx = 340, ry = 110;
        return {
            position: 'absolute',
            left: `calc(50% + ${rx * Math.cos(rad)}px)`,
            top: `calc(100% + ${ry * Math.sin(rad)}px)`,
            transform: 'translate(-50%, -50%)',
            minWidth: '180px',
            maxWidth: '220px'
        };
    };

    const playCard = () => {
        if (!selectedCardId) { setErrorMsg('카드를 선택하세요.'); return; }
        const card = me?.hand.find(c => c.id === selectedCardId);
        if (!card) return;
        // 대상이 필요한 카드: BANG, Cat Balou, Panic, Duel, Jail, Punch, Tequila, Springfield, BuffaloRifle, RagTime, CanCan
        const needsTarget = [0, 7, 8, 11, 21, 22, 25, 26, 27, 29, 30].includes(card.type);
        if (needsTarget && !selectedTargetId) { setErrorMsg('대상을 선택하세요.'); return; }
        setPlayingCard({ id: card.id, type: card.type });
        connection.invoke("PlayBangCard", roomId, selectedCardId, needsTarget ? selectedTargetId : null);
        setSelectedCardId(null);
        setSelectedTargetId(null);
    };

    const endTurn = () => {
        const excess = (me?.hand.length ?? 0) - (me?.health ?? 0);
        if (excess > 0 && discardSelection.length !== excess) {
            setErrorMsg(`${excess}장을 버릴 카드로 선택하세요.`);
            return;
        }
        connection.invoke("EndBangTurn", roomId, excess > 0 ? discardSelection : null);
        setDiscardSelection([]);
    };

    const toggleDiscard = (cardId: number) => {
        setDiscardSelection(prev =>
            prev.includes(cardId) ? prev.filter(id => id !== cardId) : [...prev, cardId]
        );
    };

    return (
        <div
            key={`root-${shakeKey}`}
            style={{
                minHeight: '100vh', width: '100%', background: '#0f172a', color: '#f1f5f9',
                padding: '1rem', boxSizing: 'border-box',
                animation: shakeKey ? 'screen-shake 0.5s ease-out' : 'none'
            }}
        >
            {turnSecLeft != null && (
                <div style={{ position: 'fixed', top: 8, right: 8, zIndex: 9999, background: turnSecLeft <= 10 ? '#dc2626' : 'rgba(30,41,59,0.9)', color: '#fff', padding: '4px 10px', borderRadius: 8, fontWeight: 'bold', fontSize: 13 }}>
                    ⏱ {turnSecLeft}s
                </div>
            )}
            {/* 헤더 */}
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem', maxWidth: '1200px', margin: '0 auto 1rem' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', flexWrap: 'wrap' }}>
                    <span style={{ color: '#f59e0b', fontWeight: 'bold', fontSize: '1.1rem' }}>🤠 뱅</span>
                    <span style={{ color: '#64748b', fontSize: '0.7rem', fontFamily: 'monospace' }}>#{roomId?.substring(0, 6)}</span>
                    {/* 옵션 인디케이터 */}
                    <div style={{ display: 'inline-flex', gap: '0.3rem', alignItems: 'center' }}>
                        <span style={{
                            padding: '0.15rem 0.4rem',
                            background: room.bangUseDodgeCity ? '#14532d' : '#334155',
                            color: room.bangUseDodgeCity ? '#86efac' : '#94a3b8',
                            borderRadius: '0.25rem',
                            fontSize: '0.65rem',
                            fontWeight: 'bold'
                        }} title="Dodge City 확장팩">
                            🌵 확장
                        </span>
                        <span style={{
                            padding: '0.15rem 0.4rem',
                            background: '#0f172a',
                            color: '#94a3b8',
                            border: '1px solid #334155',
                            borderRadius: '0.25rem',
                            fontSize: '0.65rem'
                        }} title="응답 타이머">
                            ⏱ {timerLimit}s
                        </span>
                        <span style={{
                            padding: '0.15rem 0.4rem',
                            background: '#0f172a',
                            color: '#94a3b8',
                            border: '1px solid #334155',
                            borderRadius: '0.25rem',
                            fontSize: '0.65rem'
                        }} title="플레이어 수">
                            👥 {bang.players.filter(p => p.health > 0).length}/{bang.players.length}
                        </span>
                    </div>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    {currentTurnPlayer && (
                        <span style={{ color: isMyTurn ? '#22c55e' : '#94a3b8', fontWeight: 'bold', display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}>
                            {currentTurnPlayer.isBot && (
                                <span style={{ display: 'inline-flex', alignItems: 'center', gap: '0.3rem', animation: 'bot-thinking 1.2s ease-in-out infinite' }}>
                                    <img src={CHARACTER_IMAGES[currentTurnPlayer.character]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                </span>
                            )}
                            {isMyTurn
                                ? '▶ 당신의 차례'
                                : currentTurnPlayer.isBot
                                    ? `🤖 ${currentTurnPlayer.name} 사고 중...`
                                    : `상대 차례: ${currentTurnPlayer.name}`}
                        </span>
                    )}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                    <label style={{ fontSize: '0.75rem', color: '#64748b' }}>⏱ 응답{!isHost && '(방장)'}</label>
                    <select
                        value={timerLimit}
                        disabled={!isHost}
                        onChange={e => {
                            const v = parseInt(e.target.value, 10);
                            connection.invoke("UpdateBangResponseTimer", roomId, v);
                        }}
                        style={{ background: '#0f172a', color: isHost ? '#f1f5f9' : '#64748b', border: '1px solid #475569', borderRadius: '0.3rem', padding: '0.3rem 0.5rem', fontSize: '0.8rem', cursor: isHost ? 'pointer' : 'not-allowed' }}
                        title={isHost ? "응답 타이머 한도 — 만료 시 자동 처리" : "방장만 변경 가능"}
                    >
                        <option value={10}>10초</option>
                        <option value={20}>20초</option>
                        <option value={30}>30초</option>
                        <option value={60}>60초</option>
                    </select>
                    <button onClick={() => navigate(`/room/${roomId}`)} style={{ padding: '0.5rem 1rem', background: 'transparent', border: '1px solid #ef4444', color: '#ef4444', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}>
                        나가기
                    </button>
                </div>
            </div>

            {errorMsg && (
                <div style={{ position: 'fixed', top: '4rem', left: '50%', transform: 'translateX(-50%)', background: 'rgba(231, 76, 60, 0.95)', color: 'white', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', zIndex: 1000 }}>
                    ⚠️ {errorMsg}
                </div>
            )}

            {/* 본인 사망 시 관전 오버레이 — 게임 종료 전까지 */}
            {me && me.health === 0 && !bang.isFinished && (
                <div style={{
                    position: 'fixed', inset: 0,
                    background: 'rgba(0,0,0,0.45)',
                    pointerEvents: 'none',
                    zIndex: 1800,
                    display: 'flex', flexDirection: 'column',
                    alignItems: 'center', justifyContent: 'flex-start',
                    paddingTop: '5rem'
                }}>
                    <div style={{
                        background: 'rgba(15,23,42,0.9)',
                        border: '2px solid #475569',
                        borderRadius: '0.5rem',
                        padding: '0.75rem 1.5rem',
                        color: '#cbd5e1',
                        fontWeight: 'bold',
                        boxShadow: '0 4px 16px rgba(0,0,0,0.5)'
                    }}>
                        💀 사망 — 관전 중
                    </div>
                </div>
            )}

            {/* 응답 타이머 0초 도달 시 전체 화면 빨간 플래시 (0.5초) */}
            {redFlashKey > 0 && (
                <div
                    key={`flash-${redFlashKey}`}
                    style={{
                        position: 'fixed', inset: 0,
                        pointerEvents: 'none',
                        zIndex: 3000,
                        animation: 'red-flash 0.5s ease-out forwards'
                    }}
                />
            )}

            {/* 카드 사용 애니메이션 — 화면 중앙으로 fly */}
            {playingCard && (
                <div
                    key={playingCard.id}
                    style={{
                        position: 'fixed',
                        left: '50%',
                        bottom: '20%',
                        width: '100px',
                        height: '140px',
                        background: 'linear-gradient(135deg, #1e293b, #0f172a)',
                        border: '3px solid #fbbf24',
                        borderRadius: '0.6rem',
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        zIndex: 2500,
                        pointerEvents: 'none',
                        boxShadow: '0 0 24px rgba(251,191,36,0.7)',
                        animation: 'card-play-fly 0.6s ease-out forwards'
                    }}
                >
                    <img src={CARD_IMAGES[playingCard.type]} alt="" style={{ width: '54px', height: '54px', filter: 'invert(0.9) sepia(0.5) saturate(2) hue-rotate(15deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                    <div style={{ color: '#fbbf24', fontWeight: 'bold', fontSize: '0.85rem', marginTop: '0.4rem' }}>
                        {CARD_NAMES[playingCard.type]}
                    </div>
                </div>
            )}

            {/* 펴기 결과 토스트 (Barrel/Dynamite/Lucky Duke 등) — 3초 자동 숨김 */}
            {revealToast && (
                <div
                    key={revealToast.id}
                    style={{
                        position: 'fixed', top: '8rem', left: '50%', transform: 'translateX(-50%)',
                        background: 'linear-gradient(135deg, #fbbf24, #f59e0b)', color: '#0f172a',
                        padding: '1rem 2rem', borderRadius: '0.6rem', fontSize: '1.4rem', fontWeight: 'bold',
                        boxShadow: '0 6px 20px rgba(0,0,0,0.5)', zIndex: 1000,
                        animation: 'reveal-pop 0.25s ease-out'
                    }}
                >
                    {revealToast.text}
                </div>
            )}

            {/* 버린 카드 미리보기 모달 — 최근 30장 (시간 역순) */}
            {showDiscardModal && (
                <div className="modal-overlay" onClick={() => setShowDiscardModal(false)} style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                    <div onClick={e => e.stopPropagation()} style={{ background: '#1e293b', padding: '1.5rem', borderRadius: '0.75rem', border: '2px solid #475569', maxWidth: '720px', maxHeight: '80vh', overflowY: 'auto' }}>
                        <h2 style={{ color: '#94a3b8', marginTop: 0, marginBottom: '0.5rem' }}>🗑 버린 카드 ({bang.discardPile.length}장)</h2>
                        <div style={{ color: '#64748b', fontSize: '0.8rem', marginBottom: '0.5rem' }}>
                            최근 30장 (가장 최근이 좌측)
                        </div>
                        <input
                            value={discardFilter}
                            onChange={e => setDiscardFilter(e.target.value)}
                            placeholder="🔍 카드 이름 검색 (예: 뱅, 맥주, 회피)"
                            style={{
                                width: '100%', padding: '0.4rem 0.6rem', marginBottom: '0.75rem',
                                background: '#0f172a', border: '1px solid #475569',
                                borderRadius: '0.3rem', color: '#f1f5f9', fontSize: '0.85rem',
                                boxSizing: 'border-box'
                            }}
                        />
                        <div style={{ display: 'flex', gap: '0.4rem', flexWrap: 'wrap' }}>
                            {(() => {
                                const list = [...bang.discardPile].reverse().slice(0, 30);
                                const filtered = discardFilter.trim()
                                    ? list.filter(c => (CARD_NAMES[c.type] ?? '').includes(discardFilter.trim()))
                                    : list;
                                if (filtered.length === 0) {
                                    return <div style={{ color: '#64748b', padding: '1rem' }}>일치하는 카드가 없습니다.</div>;
                                }
                                return filtered.map(c => {
                                const suit = c.suit;
                                const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#94a3b8';
                                return (
                                    <div key={c.id} style={{
                                        position: 'relative',
                                        width: '76px', height: '102px',
                                        padding: '0.3rem',
                                        background: '#0f172a',
                                        border: '1px solid #334155',
                                        borderRadius: '0.4rem',
                                        display: 'flex',
                                        flexDirection: 'column',
                                        alignItems: 'center',
                                        justifyContent: 'center'
                                    }}>
                                        <div style={{ position: 'absolute', top: '0.2rem', left: '0.3rem', color: suitColor, fontSize: '0.65rem', fontWeight: 'bold', lineHeight: 1 }}>
                                            {rankLabel(c.rank)}<div>{suitSym}</div>
                                        </div>
                                        <img
                                            src={CARD_IMAGES[c.type]}
                                            alt={CARD_NAMES[c.type]}
                                            onError={(e) => {
                                                const img = e.currentTarget;
                                                img.style.display = 'none';
                                                const fb = img.nextElementSibling as HTMLElement | null;
                                                if (fb) fb.style.display = 'block';
                                            }}
                                            style={{ width: '30px', height: '30px', filter: 'invert(0.95)', marginBottom: '0.1rem' }}
                                        />
                                        <div style={{ fontSize: '1.3rem', lineHeight: 1, marginBottom: '0.1rem', display: 'none' }}>{CARD_ICONS[c.type] ?? '🃏'}</div>
                                        <div style={{ fontSize: '0.65rem', fontWeight: 'bold', textAlign: 'center', color: '#cbd5e1' }}>
                                            {CARD_NAMES[c.type]}
                                        </div>
                                    </div>
                                );
                            });
                            })()}
                        </div>
                        <button
                            onClick={() => setShowDiscardModal(false)}
                            style={{ marginTop: '1rem', padding: '0.5rem 1.5rem', background: '#334155', color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold', width: '100%' }}
                        >
                            닫기
                        </button>
                    </div>
                </div>
            )}

            {/* 캐릭터 능력 설명 모달 — 본인 + 상대방 공용 */}
            {abilityModalCharacter !== null && (() => {
                const charIdx = abilityModalCharacter;
                const ownPlayer = bang.players.find(p => p.character === charIdx);
                const maxHp = ownPlayer?.maxHealth;
                return (
                    <div className="modal-overlay" onClick={() => setAbilityModalCharacter(null)} style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div onClick={e => e.stopPropagation()} style={{ background: '#1e293b', padding: '1.5rem', borderRadius: '0.75rem', border: '2px solid #a78bfa', maxWidth: '480px', textAlign: 'center' }}>
                            <img
                                src={CHARACTER_IMAGES[charIdx]}
                                alt={CHARACTER_NAMES[charIdx]}
                                onError={(e) => { e.currentTarget.style.display = 'none'; }}
                                style={{ width: '100px', height: '100px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)', marginBottom: '0.5rem' }}
                            />
                            <h2 style={{ color: '#a78bfa', marginTop: 0 }}>🎭 {CHARACTER_NAMES[charIdx]}</h2>
                            {maxHp !== undefined && (
                                <div style={{ color: '#ef4444', fontSize: '0.9rem', marginBottom: '1rem' }}>❤ 최대 체력 {maxHp}</div>
                            )}
                            <div style={{ background: '#0f172a', padding: '1rem', borderRadius: '0.5rem', color: '#cbd5e1', fontSize: '0.95rem', lineHeight: 1.5, marginBottom: '1rem' }}>
                                {CHARACTER_ABILITIES[charIdx] ?? '능력 정보가 없습니다.'}
                            </div>
                            <button onClick={() => setAbilityModalCharacter(null)} style={{ padding: '0.5rem 1.5rem', background: '#a78bfa', color: '#0f172a', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold', width: '100%' }}>
                                닫기
                            </button>
                        </div>
                    </div>
                );
            })()}

            {/* 본인이 응답자일 때 큰 카운트다운 — 화면 상단 중앙 (4단계 색상) */}
            {bang.pending && (() => {
                const p = bang.pending;
                const isMyResp =
                    ((p.type === 1 || p.type === 7 || p.type === 11) && p.targetPlayerId === myId) ||
                    ((p.type === 2 || p.type === 5 || p.type === 6) && p.responderQueue?.[0] === myId) ||
                    ((p.type === 4 || p.type === 8 || p.type === 9 || p.type === 10) && p.sourcePlayerId === myId);
                if (!isMyResp) return null;
                const pct = Math.max(0, (responseTimer / timerLimit) * 100);
                // 4단계 색상: 안전(>20) / 경고(10~20) / 주의(5~10) / 위험(≤5)
                let bg: string, border: string, accent: string, shadow: string, label: string, pulse = false;
                if (responseTimer <= 5) {
                    bg = '#7f1d1d'; border = '#ef4444'; accent = '#fecaca'; shadow = '0 0 32px rgba(239,68,68,0.9)'; label = '🚨 위급'; pulse = true;
                } else if (responseTimer <= 10) {
                    bg = '#9a3412'; border = '#f97316'; accent = '#fed7aa'; shadow = '0 0 20px rgba(249,115,22,0.6)'; label = '⚠ 주의';
                } else if (responseTimer <= 20) {
                    bg = '#854d0e'; border = '#facc15'; accent = '#fef3c7'; shadow = '0 0 16px rgba(250,204,21,0.5)'; label = '⏱ 응답 필요';
                } else {
                    bg = 'linear-gradient(135deg, #1e3a5f, #082f4a)'; border = '#38bdf8'; accent = '#bae6fd'; shadow = '0 0 14px rgba(56,189,248,0.4)'; label = '⏱ 응답 필요';
                }
                // 원형 progress 계산
                const radius = 32;
                const circumference = 2 * Math.PI * radius;
                const dashOffset = circumference * (1 - pct / 100);
                return (
                    <div style={{
                        position: 'fixed', top: '7.5rem', left: '50%', transform: 'translateX(-50%)',
                        zIndex: 2100,
                        background: bg,
                        border: `3px solid ${border}`,
                        borderRadius: '0.6rem',
                        padding: '0.75rem 1.5rem',
                        boxShadow: shadow,
                        minWidth: '300px',
                        textAlign: 'center',
                        animation: pulse ? 'urgent-pulse 0.7s ease-in-out infinite' : 'none'
                    }}>
                        <div style={{ fontSize: '1rem', color: accent, fontWeight: 'bold', marginBottom: '0.3rem' }}>
                            {label}
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '0.8rem', marginTop: '0.3rem' }}>
                            <svg width="76" height="76" style={{ transform: 'rotate(-90deg)' }}>
                                <circle cx="38" cy="38" r={radius} stroke="rgba(0,0,0,0.4)" strokeWidth="6" fill="none" />
                                <circle
                                    cx="38" cy="38" r={radius}
                                    stroke={border}
                                    strokeWidth="6"
                                    fill="none"
                                    strokeLinecap="round"
                                    strokeDasharray={circumference}
                                    strokeDashoffset={dashOffset}
                                    style={{ transition: 'stroke-dashoffset 1s linear' }}
                                />
                            </svg>
                            <div style={{ fontSize: '2.4rem', color: accent, fontWeight: 'bold', lineHeight: 1, fontVariantNumeric: 'tabular-nums', minWidth: '60px' }}>
                                {responseTimer}<span style={{ fontSize: '1rem', marginLeft: '0.2rem' }}>s</span>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* 응답 타이머 (작음) — 본인 외 사람도 표시. progress bar 포함 */}
            {bang.pending && (
                <div
                    style={{
                        position: 'fixed', top: '4.5rem', right: '2rem',
                        background: responseTimer <= 10 ? '#7f1d1d' : '#1e293b',
                        border: `2px solid ${responseTimer <= 10 ? '#ef4444' : '#475569'}`,
                        color: 'white', padding: '0.5rem 1rem', borderRadius: '0.5rem',
                        fontWeight: 'bold', fontSize: '0.95rem', zIndex: 999,
                        boxShadow: responseTimer <= 10 ? '0 0 12px rgba(239,68,68,0.6)' : 'none',
                        minWidth: '140px'
                    }}
                >
                    <div>⏱ 응답 대기 {responseTimer}s</div>
                    <div style={{
                        width: '100%',
                        height: '4px',
                        background: '#0f172a',
                        borderRadius: '2px',
                        marginTop: '0.4rem',
                        overflow: 'hidden'
                    }}>
                        <div style={{
                            width: `${Math.max(0, (responseTimer / timerLimit) * 100)}%`,
                            height: '100%',
                            background: responseTimer <= 10 ? '#ef4444' : '#38bdf8',
                            transition: 'width 1s linear'
                        }} />
                    </div>
                </div>
            )}

            <div style={{ display: 'grid', gridTemplateColumns: '1fr 280px', gap: '1.5rem', maxWidth: '1200px', margin: '0 auto' }}>
                {/* 메인 영역 */}
                <div>
                    {/* 다른 플레이어들 — 원형 호 배치 */}
                    <div style={{ position: 'relative', height: '260px', marginBottom: '1rem' }}>
                        {others.map((p, idx) => {
                            const isTurn = p.playerId === bang.currentTurnPlayerId;
                            const isSelected = p.playerId === selectedTargetId;
                            // 응답 큐에서의 위치 (Indians/Gatling/General Store)
                            const queueIdx = bang.pending?.responderQueue?.indexOf(p.playerId) ?? -1;
                            const inQueue = queueIdx >= 0;
                            const isNextResponder = queueIdx === 0;
                            // 응답 대상 (BANG/Duel/LuckyDuke)
                            const isPendingTarget = bang.pending && [1, 7, 11].includes(bang.pending.type)
                                && bang.pending.targetPlayerId === p.playerId;
                            return (
                                <div
                                    key={p.playerId}
                                    onClick={() => p.health > 0 && setSelectedTargetId(isSelected ? null : p.playerId)}
                                    style={{
                                        ...seatStyle(idx, others.length),
                                        padding: '0.75rem',
                                        background: p.health === 0 ? '#1a1a1a' : isSelected ? '#1e3a5f' : '#1e293b',
                                        border: `2px solid ${
                                            isNextResponder || isPendingTarget ? '#fbbf24'
                                            : isTurn ? '#22c55e'
                                            : isSelected ? '#38bdf8'
                                            : '#334155'}`,
                                        borderRadius: '0.5rem',
                                        opacity: p.health === 0 ? 0.4 : 1,
                                        cursor: p.health > 0 ? 'pointer' : 'not-allowed',
                                        boxShadow: (isNextResponder || isPendingTarget)
                                            ? '0 0 16px rgba(251,191,36,0.7)'
                                            : isTurn ? '0 0 12px rgba(34,197,94,0.5)' : 'none',
                                        position: 'absolute' as const
                                    }}
                                >
                                    {/* 사망 시 해골 오버레이 */}
                                    {p.health === 0 && (
                                        <div style={{
                                            position: 'absolute', inset: 0,
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            background: 'rgba(0,0,0,0.6)', borderRadius: '0.4rem',
                                            zIndex: 5, pointerEvents: 'none'
                                        }}>
                                            <div style={{ fontSize: '2.5rem', filter: 'grayscale(1)', opacity: 0.85 }}>💀</div>
                                            <div style={{ position: 'absolute', bottom: '0.3rem', color: '#94a3b8', fontSize: '0.65rem', fontWeight: 'bold' }}>사망</div>
                                        </div>
                                    )}
                                    {/* 응답 큐 위치 배지 */}
                                    {inQueue && (
                                        <div style={{
                                            position: 'absolute', top: '-10px', right: '-10px',
                                            background: isNextResponder ? '#fbbf24' : '#475569',
                                            color: isNextResponder ? '#0f172a' : '#cbd5e1',
                                            borderRadius: '50%', width: '24px', height: '24px',
                                            display: 'flex', alignItems: 'center', justifyContent: 'center',
                                            fontSize: '0.75rem', fontWeight: 'bold',
                                            boxShadow: isNextResponder ? '0 0 8px rgba(251,191,36,0.8)' : 'none'
                                        }} title={isNextResponder ? '응답 차례' : `대기 #${queueIdx + 1}`}>
                                            {queueIdx + 1}
                                        </div>
                                    )}
                                    {/* 응답 대상 배지 */}
                                    {isPendingTarget && (
                                        <div style={{
                                            position: 'absolute', top: '-10px', right: '-10px',
                                            background: '#fbbf24', color: '#0f172a',
                                            borderRadius: '0.3rem', padding: '0.1rem 0.4rem',
                                            fontSize: '0.7rem', fontWeight: 'bold'
                                        }}>응답</div>
                                    )}
                                    <div style={{ fontWeight: 'bold', wordBreak: 'keep-all', lineHeight: 1.3 }}>
                                        {p.isBot && <span style={{ color: '#a78bfa', marginRight: '0.2rem' }}>🤖</span>}
                                        {p.name}
                                        {p.roleRevealed && (
                                            <div style={{ fontSize: '0.8rem', color: '#fbbf24', marginTop: '0.1rem' }}>
                                                {ROLE_NAMES[p.role]}
                                            </div>
                                        )}
                                    </div>
                                    <div
                                        onClick={(e) => {
                                            e.stopPropagation();
                                            setAbilityModalCharacter(p.character);
                                        }}
                                        style={{
                                            color: '#a78bfa',
                                            fontSize: '0.7rem',
                                            cursor: 'pointer',
                                            textDecoration: 'underline dotted',
                                            display: 'inline-flex',
                                            alignItems: 'center',
                                            gap: '0.2rem'
                                        }}
                                        title="클릭해서 능력 보기"
                                    >
                                        <img src={CHARACTER_IMAGES[p.character]} alt="" style={{ width: '14px', height: '14px', filter: 'invert(0.8) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        {CHARACTER_NAMES[p.character]} ⓘ
                                    </div>
                                    <div style={{ color: '#ef4444', fontSize: '0.9rem' }}>
                                        ❤ {p.health}/{p.maxHealth}
                                    </div>
                                    <div style={{ color: '#94a3b8', fontSize: '0.75rem' }}>
                                        손 {p.hand.length}장
                                    </div>
                                    {(p.weapon || p.equipment.length > 0) && (
                                        <div style={{ marginTop: '0.4rem', display: 'flex', gap: '0.25rem', flexWrap: 'wrap' }}>
                                            {p.weapon && (
                                                <span style={{ fontSize: '0.7rem', padding: '0.15rem 0.4rem', background: 'linear-gradient(135deg, #7f1d1d, #450a0a)', color: '#fecaca', borderRadius: '0.25rem', display: 'inline-flex', alignItems: 'center', gap: '0.2rem' }} title={`${CARD_NAMES[p.weapon.type]}\n\n${CARD_ABILITIES[p.weapon.type] ?? ''}`}>
                                                    <img src={CARD_IMAGES[p.weapon.type]} alt="" style={{ width: '14px', height: '14px', filter: 'invert(0.85) sepia(0.5) saturate(2) hue-rotate(310deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                                    {CARD_NAMES[p.weapon.type]} {p.weapon.suit !== undefined && <span style={{ color: SUIT_COLORS_ON_DARK[p.weapon.suit] }}>{SUIT_SYMBOLS[p.weapon.suit]}{rankLabel(p.weapon.rank)}</span>}
                                                </span>
                                            )}
                                            {p.equipment.map(eq => (
                                                <span key={eq.id} style={{ fontSize: '0.7rem', padding: '0.15rem 0.4rem', background: 'linear-gradient(135deg, #1e3a5f, #082f4a)', color: '#bae6fd', borderRadius: '0.25rem', display: 'inline-flex', alignItems: 'center', gap: '0.2rem' }} title={`${CARD_NAMES[eq.type]}\n\n${CARD_ABILITIES[eq.type] ?? ''}`}>
                                                    <img src={CARD_IMAGES[eq.type]} alt="" style={{ width: '14px', height: '14px', filter: 'invert(0.85) sepia(0.5) saturate(2) hue-rotate(180deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                                    {CARD_NAMES[eq.type]} {eq.suit !== undefined && <span style={{ color: SUIT_COLORS_ON_DARK[eq.suit] }}>{SUIT_SYMBOLS[eq.suit]}{rankLabel(eq.rank)}</span>}
                                                </span>
                                            ))}
                                        </div>
                                    )}
                                </div>
                            );
                        })}
                    </div>

                    {/* 덱 + 버린 카드 정보 — 가운데 정렬 */}
                    <div style={{ display: 'flex', gap: '1rem', marginBottom: '1.5rem', justifyContent: 'center' }}>
                        <div style={{ padding: '0.75rem 1rem', background: '#064e3b', borderRadius: '0.5rem', color: 'white' }}>
                            덱: {bang.deck.length}장
                        </div>
                        <button
                            onClick={() => bang.discardPile.length > 0 && setShowDiscardModal(true)}
                            disabled={bang.discardPile.length === 0}
                            style={{
                                padding: '0.75rem 1rem',
                                background: '#1e293b',
                                border: '1px solid #475569',
                                borderRadius: '0.5rem',
                                color: '#94a3b8',
                                cursor: bang.discardPile.length > 0 ? 'pointer' : 'not-allowed',
                                fontWeight: 'bold'
                            }}
                            title="클릭하여 버린 카드 미리보기"
                        >
                            🗑 버린 카드: {bang.discardPile.length}장
                        </button>
                    </div>

                    {/* 내 정보 + 손패 */}
                    {me && (
                        <div
                            key={`me-${myTurnFlashKey}`}
                            style={{
                                padding: '1rem',
                                background: '#1e293b',
                                border: `2px solid ${isMyTurn ? '#22c55e' : '#38bdf8'}`,
                                borderRadius: '0.5rem',
                                boxShadow: isMyTurn ? '0 0 20px rgba(34,197,94,0.5)' : 'none',
                                position: 'relative',
                                animation: isMyTurn && myTurnFlashKey ? 'my-turn-flash 1.5s ease-out' : 'none'
                            }}
                        >
                            {isMyTurn && (
                                <div style={{
                                    position: 'absolute',
                                    top: '-12px',
                                    left: '1rem',
                                    background: '#22c55e',
                                    color: '#0f172a',
                                    padding: '0.2rem 0.75rem',
                                    borderRadius: '0.4rem',
                                    fontSize: '0.85rem',
                                    fontWeight: 'bold',
                                    boxShadow: '0 0 8px rgba(34,197,94,0.6)'
                                }}>
                                    ▶ 당신의 차례 — 카드를 클릭해 사용하거나 턴 종료
                                </div>
                            )}
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.75rem' }}>
                                <div>
                                    <span style={{ fontWeight: 'bold', fontSize: '1.1rem' }}>{me.name}</span>
                                    <span style={{ marginLeft: '0.75rem', color: '#f59e0b' }}>{ROLE_NAMES[me.role]}</span>
                                    <button
                                        onClick={() => setAbilityModalCharacter(me.character)}
                                        style={{ marginLeft: '0.75rem', color: '#a78bfa', fontSize: '0.85rem', background: 'transparent', border: '1px solid #a78bfa', padding: '0.15rem 0.4rem', borderRadius: '0.3rem', cursor: 'pointer', display: 'inline-flex', alignItems: 'center', gap: '0.3rem' }}
                                        title="캐릭터 능력 보기"
                                    >
                                        <img src={CHARACTER_IMAGES[me.character]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        {CHARACTER_NAMES[me.character]} ⓘ
                                    </button>
                                </div>
                                <div style={{ color: '#ef4444', fontWeight: 'bold' }}>❤ {me.health}/{me.maxHealth}</div>
                            </div>
                            {(me.weapon || me.equipment.length > 0) && (
                                <div style={{ marginBottom: '0.75rem', display: 'flex', gap: '0.4rem', flexWrap: 'wrap' }}>
                                    {me.weapon && (
                                        <span style={{ fontSize: '0.8rem', padding: '0.3rem 0.6rem', background: 'linear-gradient(135deg, #7f1d1d, #450a0a)', color: '#fecaca', borderRadius: '0.3rem', fontWeight: 'bold', display: 'inline-flex', alignItems: 'center', gap: '0.3rem' }} title={CARD_ABILITIES[me.weapon.type] ?? ''}>
                                            <img src={CARD_IMAGES[me.weapon.type]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(0.9) sepia(0.4) saturate(2) hue-rotate(310deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                            {CARD_NAMES[me.weapon.type]} {me.weapon.suit !== undefined && <span style={{ color: SUIT_COLORS_ON_DARK[me.weapon.suit] }}>{SUIT_SYMBOLS[me.weapon.suit]}{rankLabel(me.weapon.rank)}</span>}
                                        </span>
                                    )}
                                    {me.equipment.map(eq => (
                                        <span key={eq.id} style={{ fontSize: '0.8rem', padding: '0.3rem 0.6rem', background: 'linear-gradient(135deg, #1e3a5f, #082f4a)', color: '#bae6fd', borderRadius: '0.3rem', fontWeight: 'bold', display: 'inline-flex', alignItems: 'center', gap: '0.3rem' }} title={CARD_ABILITIES[eq.type] ?? ''}>
                                            <img src={CARD_IMAGES[eq.type]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(0.9) sepia(0.5) saturate(2) hue-rotate(180deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                            {CARD_NAMES[eq.type]} {eq.suit !== undefined && <span style={{ color: SUIT_COLORS_ON_DARK[eq.suit] }}>{SUIT_SYMBOLS[eq.suit]}{rankLabel(eq.rank)}</span>}
                                        </span>
                                    ))}
                                </div>
                            )}
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                                <div style={{ color: '#94a3b8', fontSize: '0.8rem' }}>내 손패 ({me.hand.length}장)</div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem' }}>
                                    <label style={{ color: '#64748b', fontSize: '0.7rem', display: 'inline-flex', alignItems: 'center', gap: '0.2rem', cursor: 'pointer' }} title="뱅!/개틀링 받을 때 빗나감 자동 사용">
                                        <input
                                            type="checkbox"
                                            checked={autoDodge}
                                            onChange={e => {
                                                setAutoDodge(e.target.checked);
                                                localStorage.setItem("bangAutoDodge", e.target.checked ? "1" : "0");
                                            }}
                                            style={{ accentColor: '#22c55e' }}
                                        />
                                        🛡 자동 회피
                                    </label>
                                    <span style={{ color: '#64748b', fontSize: '0.7rem' }}>정렬:</span>
                                    <select
                                        value={handSort}
                                        onChange={e => {
                                            const v = e.target.value as 'default' | 'type' | 'suit' | 'rank';
                                            setHandSort(v);
                                            localStorage.setItem("bangHandSort", v);
                                        }}
                                        style={{ background: '#0f172a', color: '#f1f5f9', border: '1px solid #334155', borderRadius: '0.25rem', padding: '0.2rem 0.4rem', fontSize: '0.75rem' }}
                                    >
                                        <option value="default">기본</option>
                                        <option value="type">타입</option>
                                        <option value="suit">무늬</option>
                                        <option value="rank">숫자</option>
                                    </select>
                                </div>
                            </div>
                            <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                                {(() => {
                                    const sorted = [...me.hand];
                                    if (handSort === 'type') sorted.sort((a, b) => a.type - b.type);
                                    else if (handSort === 'suit') sorted.sort((a, b) => (a.suit ?? 0) - (b.suit ?? 0) || (a.rank ?? 0) - (b.rank ?? 0));
                                    else if (handSort === 'rank') sorted.sort((a, b) => (a.rank ?? 0) - (b.rank ?? 0));
                                    return sorted;
                                })().map(c => {
                                    const isSelected = c.id === selectedCardId;
                                    const isDiscarding = discardSelection.includes(c.id);
                                    const suit = c.suit;
                                    const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                    const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#94a3b8';
                                    // Suit별 배경 그라데이션 — ♥/♦은 빨간 톤, ♣/♠는 어두운 톤
                                    const isRedSuit = suit === 0 || suit === 1;
                                    const defaultBg = isRedSuit
                                        ? 'linear-gradient(135deg, #2a0e0e 0%, #0f172a 60%)'
                                        : 'linear-gradient(135deg, #0f172a 0%, #1e293b 100%)';
                                    return (
                                        <div
                                            key={c.id}
                                            className="bang-hand-card"
                                            onClick={() => {
                                                if (!isMyTurn) return;
                                                setSelectedCardId(isSelected ? null : c.id);
                                            }}
                                            onContextMenu={(e) => {
                                                e.preventDefault();
                                                if (isMyTurn) toggleDiscard(c.id);
                                            }}
                                            style={{
                                                position: 'relative',
                                                width: '88px',
                                                height: '120px',
                                                padding: '0.4rem',
                                                background: isDiscarding ? '#7f1d1d' : isSelected ? 'linear-gradient(135deg, #1e3a8a, #1e40af)' : defaultBg,
                                                border: `2px solid ${isDiscarding ? '#ef4444' : isSelected ? '#38bdf8' : '#334155'}`,
                                                borderRadius: '0.5rem',
                                                cursor: isMyTurn ? 'pointer' : 'default',
                                                display: 'flex',
                                                flexDirection: 'column',
                                                alignItems: 'center',
                                                justifyContent: 'space-between',
                                                boxShadow: isSelected ? '0 0 8px rgba(56,189,248,0.5)' : 'none'
                                            }}
                                            title={`${CARD_NAMES[c.type]}\n\n${CARD_ABILITIES[c.type] ?? ''}${isMyTurn ? '\n\n(좌클릭: 사용 / 우클릭: 버리기 선택)' : ''}`}
                                        >
                                            <div style={{ position: 'absolute', top: '0.25rem', left: '0.4rem', color: suitColor, fontSize: '0.75rem', fontWeight: 'bold', lineHeight: 1 }}>
                                                {rankLabel(c.rank)}
                                                <div>{suitSym}</div>
                                            </div>
                                            <div style={{ flex: 1, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', padding: '0.4rem 0.2rem 0.2rem 0.2rem', color: '#f1f5f9' }}>
                                                <img
                                                    src={CARD_IMAGES[c.type]}
                                                    alt={CARD_NAMES[c.type]}
                                                    onError={(e) => {
                                                        const img = e.currentTarget;
                                                        img.style.display = 'none';
                                                        const fb = img.nextElementSibling as HTMLElement | null;
                                                        if (fb) fb.style.display = 'block';
                                                    }}
                                                    style={{ width: '38px', height: '38px', filter: 'invert(0.95)', marginBottom: '0.1rem' }}
                                                />
                                                <div style={{ fontSize: '1.6rem', lineHeight: 1, marginBottom: '0.1rem', display: 'none' }}>{CARD_ICONS[c.type] ?? '🃏'}</div>
                                                <div style={{ fontSize: '0.7rem', fontWeight: 'bold', textAlign: 'center' }}>{CARD_NAMES[c.type]}</div>
                                            </div>
                                            <div style={{ position: 'absolute', bottom: '0.25rem', right: '0.4rem', color: suitColor, fontSize: '0.75rem', fontWeight: 'bold', lineHeight: 1, transform: 'rotate(180deg)' }}>
                                                {rankLabel(c.rank)}
                                                <div>{suitSym}</div>
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                            {isMyTurn && (
                                <div style={{ marginTop: '1rem', display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                                    <button
                                        onClick={playCard}
                                        disabled={!selectedCardId}
                                        style={{
                                            padding: '0.5rem 1rem',
                                            background: selectedCardId ? '#22c55e' : '#334155',
                                            color: 'white',
                                            border: 'none',
                                            borderRadius: '0.4rem',
                                            cursor: selectedCardId ? 'pointer' : 'not-allowed',
                                            fontWeight: 'bold'
                                        }}
                                    >
                                        카드 사용
                                    </button>
                                    <button
                                        onClick={endTurn}
                                        style={{
                                            padding: '0.5rem 1rem',
                                            background: !selectedCardId && !bang.pending ? 'linear-gradient(135deg, #f59e0b, #d97706)' : '#f59e0b',
                                            color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold',
                                            boxShadow: !selectedCardId && !bang.pending ? '0 0 12px rgba(245,158,11,0.7)' : 'none',
                                            transition: 'box-shadow 0.2s'
                                        }}
                                    >
                                        ⏭ 턴 종료 {me.hand.length > me.health && `(버릴 카드 ${me.hand.length - me.health}장)`}
                                    </button>
                                </div>
                            )}
                            {/* Sid Ketchum 능력 버튼 — 캐릭터일 때만 + 자기 턴 아니어도 가능 */}
                            {me.character === 12 && me.health < me.maxHealth && (
                                <div style={{ marginTop: '0.5rem' }}>
                                    <button
                                        onClick={() => {
                                            if (discardSelection.length !== 2) {
                                                setErrorMsg('손패 2장을 우클릭으로 선택하세요.');
                                                return;
                                            }
                                            connection.invoke("UseSidKetchum", roomId, discardSelection);
                                            setDiscardSelection([]);
                                        }}
                                        style={{ padding: '0.5rem 1rem', background: '#a855f7', color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.85rem', boxShadow: '0 0 12px rgba(168,85,247,0.6)', display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
                                    >
                                        <img src={CHARACTER_IMAGES[12]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(1)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        시드 케첨 능력 — 손 2장 버려 HP +1 ({discardSelection.length}/2 선택됨)
                                    </button>
                                </div>
                            )}
                            {/* Dodge City — Chuck Wengam: 본인 턴에만, HP > 1일 때 */}
                            {me.character === 19 && isMyTurn && me.health > 1 && (
                                <div style={{ marginTop: '0.5rem' }}>
                                    <button
                                        onClick={() => connection.invoke("UseChuckWengam", roomId)}
                                        style={{ padding: '0.5rem 1rem', background: '#0ea5e9', color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.85rem', boxShadow: '0 0 12px rgba(14,165,233,0.6)', display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
                                    >
                                        <img src={CHARACTER_IMAGES[19]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(1)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        척 웬감 능력 — HP -1, 카드 +2
                                    </button>
                                </div>
                            )}
                            {/* Dodge City — Doc Holyday: 본인 턴에만, 손 2장 우클릭 + 대상 클릭 후 사용 */}
                            {me.character === 20 && isMyTurn && (
                                <div style={{ marginTop: '0.5rem' }}>
                                    <button
                                        onClick={() => {
                                            if (discardSelection.length !== 2) {
                                                setErrorMsg('손패 2장을 우클릭으로 선택하세요.');
                                                return;
                                            }
                                            if (!selectedTargetId) {
                                                setErrorMsg('대상 플레이어를 클릭하세요.');
                                                return;
                                            }
                                            connection.invoke("UseDocHolyday", roomId, discardSelection, selectedTargetId);
                                            setDiscardSelection([]);
                                            setSelectedTargetId(null);
                                        }}
                                        style={{ padding: '0.5rem 1rem', background: '#ec4899', color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.85rem', boxShadow: '0 0 12px rgba(236,72,153,0.6)', display: 'inline-flex', alignItems: 'center', gap: '0.4rem' }}
                                    >
                                        <img src={CHARACTER_IMAGES[20]} alt="" style={{ width: '18px', height: '18px', filter: 'invert(1)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        닥 홀리데이 능력 — 손 2장 버려 뱅! ({discardSelection.length}/2, 대상: {selectedTargetId ? '✓' : '✗'})
                                    </button>
                                </div>
                            )}
                            <div style={{ marginTop: '0.5rem', color: '#94a3b8', fontSize: '0.75rem' }}>
                                좌클릭: 사용 선택 · 우클릭: 턴 종료/Sid 버릴 카드 선택
                            </div>
                        </div>
                    )}
                </div>

                {/* 사이드: 로그 + 채팅 */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
                    <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '0.5rem', padding: '0.75rem', maxHeight: '400px', overflowY: 'auto' }}>
                        <div style={{ fontWeight: 'bold', marginBottom: '0.5rem' }}>📜 게임 로그</div>
                        {logLines.length === 0 ? (
                            <div style={{ color: '#64748b', fontSize: '0.85rem' }}>아직 로그가 없습니다.</div>
                        ) : (
                            logLines.map((m, i) => {
                                const isLast = i === logLines.length - 1;
                                return (
                                    <div
                                        key={i}
                                        style={{
                                            fontSize: '0.85rem',
                                            padding: '0.3rem 0.4rem',
                                            borderBottom: '1px solid #334155',
                                            display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: '0.1rem',
                                            animation: isLast ? 'log-highlight 1.5s ease-out' : 'none'
                                        }}
                                    >
                                        {renderLogWithIcons(m)}
                                    </div>
                                );
                            })
                        )}
                        <div ref={logEndRef} />
                    </div>

                    {/* 채팅 */}
                    <div style={{ background: '#1e293b', border: '1px solid #334155', borderRadius: '0.5rem', padding: '0.75rem', display: 'flex', flexDirection: 'column', maxHeight: '250px' }}>
                        <div style={{ fontWeight: 'bold', marginBottom: '0.5rem' }}>💬 채팅</div>
                        <div style={{ flex: 1, overflowY: 'auto', maxHeight: '160px', marginBottom: '0.5rem' }}>
                            {chatMessages.length === 0 ? (
                                <div style={{ color: '#64748b', fontSize: '0.8rem' }}>메시지가 없습니다.</div>
                            ) : (
                                chatMessages.map((m, i) => {
                                    const splitIdx = m.indexOf(" : ");
                                    const user = m.substring(0, splitIdx);
                                    const msg = m.substring(splitIdx + 3);
                                    return (
                                        <div key={i} style={{ fontSize: '0.8rem', padding: '0.2rem 0', color: '#cbd5e1' }}>
                                            <span style={{ color: '#38bdf8', fontWeight: 'bold' }}>{user}</span>: {msg}
                                        </div>
                                    );
                                })
                            )}
                        </div>
                        <div style={{ display: 'flex', gap: '0.4rem' }}>
                            <input
                                value={chatInput}
                                onChange={e => setChatInput(e.target.value)}
                                onKeyDown={e => {
                                    if (e.key === 'Enter' && chatInput.trim()) {
                                        connection.invoke("SendRoomChat", roomId, chatInput);
                                        setChatInput("");
                                    }
                                }}
                                placeholder="메시지..."
                                style={{ flex: 1, padding: '0.4rem', background: '#0f172a', border: '1px solid #334155', borderRadius: '0.3rem', color: '#f1f5f9', fontSize: '0.85rem' }}
                            />
                            <button
                                onClick={() => {
                                    if (!chatInput.trim()) return;
                                    connection.invoke("SendRoomChat", roomId, chatInput);
                                    setChatInput("");
                                }}
                                style={{ padding: '0.4rem 0.75rem', background: '#38bdf8', color: '#0f172a', border: 'none', borderRadius: '0.3rem', cursor: 'pointer', fontWeight: 'bold', fontSize: '0.8rem' }}
                            >
                                전송
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            {/* General Store 카드 선택 모달 — 현재 차례인 사람에게만 */}
            {bang.pending?.type === 2 && bang.pending.responderQueue?.[0] === myId && (() => {
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #f59e0b', textAlign: 'center', maxWidth: '700px' }}>
                            <h2 style={{ color: '#f59e0b', marginTop: 0 }}>🏪 잡화점</h2>
                            <p style={{ fontSize: '1rem', margin: '1rem 0' }}>펴진 카드 중 1장을 선택하세요</p>
                            <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'center', marginTop: '1.5rem' }}>
                                {bang.pending.revealedCards?.map(c => {
                                    const suit = c.suit;
                                    const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                    const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#f59e0b';
                                    return (
                                        <button
                                            key={c.id}
                                            className="bang-modal-card"
                                            onClick={() => connection.invoke("PickGeneralStoreCard", roomId, c.id)}
                                            style={{
                                                position: 'relative',
                                                width: '90px', height: '130px',
                                                background: '#0f172a',
                                                border: '2px solid #f59e0b',
                                                borderRadius: '0.5rem',
                                                color: '#f59e0b',
                                                fontWeight: 'bold',
                                                cursor: 'pointer',
                                                display: 'flex', flexDirection: 'column',
                                                alignItems: 'center', justifyContent: 'center',
                                                padding: '0.4rem'
                                            }}
                                        >
                                            <div style={{ position: 'absolute', top: '0.2rem', left: '0.3rem', color: suitColor, fontSize: '0.75rem', lineHeight: 1 }}>
                                                {rankLabel(c.rank)}<div>{suitSym}</div>
                                            </div>
                                            <img
                                                src={CARD_IMAGES[c.type]}
                                                alt={CARD_NAMES[c.type]}
                                                onError={(e) => { e.currentTarget.style.display = 'none'; }}
                                                style={{ width: '44px', height: '44px', filter: 'invert(0.85) sepia(0.5) saturate(2) hue-rotate(15deg)', marginBottom: '0.3rem' }}
                                            />
                                            <div style={{ fontSize: '0.75rem' }}>{CARD_NAMES[c.type]}</div>
                                        </button>
                                    );
                                })}
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* General Store 대기 안내 */}
            {bang.pending?.type === 2 && bang.pending.responderQueue?.[0] !== myId && (() => {
                const next = bang.players.find(p => p.playerId === bang.pending!.responderQueue?.[0]);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #f59e0b', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#f59e0b', zIndex: 1500 }}>
                        🏪 General Store — <b>{next?.name}</b> 선택 중... ({bang.pending.revealedCards?.length}장 남음)
                    </div>
                );
            })()}

            {/* Indians 응답 모달 — 큐의 첫 사람이 본인일 때 */}
            {bang.pending?.type === 5 && bang.pending.responderQueue?.[0] === myId && (() => {
                const source = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const hasBang = me?.hand.some(c => c.type === 0);
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #ef4444', textAlign: 'center', maxWidth: '420px' }}>
                            <img src={CARD_IMAGES[9]} alt="" style={{ width: '80px', height: '80px', filter: 'invert(0.85) sepia(0.5) saturate(3) hue-rotate(330deg)', marginBottom: '0.5rem' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                            <h2 style={{ color: '#ef4444', marginTop: 0 }}>🪶 인디언!</h2>
                            <p style={{ fontSize: '1rem', margin: '1rem 0' }}>
                                <b>{source?.name ?? '상대'}</b>이(가) 인디언 사용.
                            </p>
                            <p style={{ color: '#94a3b8', fontSize: '0.9rem' }}>
                                BANG으로 응답하거나 1 데미지를 받습니다.
                            </p>
                            <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem', justifyContent: 'center' }}>
                                <button
                                    onClick={() => connection.invoke("RespondIndians", roomId, true)}
                                    disabled={!hasBang}
                                    style={{
                                        padding: '0.75rem 1.5rem',
                                        background: hasBang ? '#22c55e' : '#334155',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '0.5rem',
                                        cursor: hasBang ? 'pointer' : 'not-allowed',
                                        fontWeight: 'bold'
                                    }}
                                >
                                    🔫 BANG 응답
                                </button>
                                <button
                                    onClick={() => connection.invoke("RespondIndians", roomId, false)}
                                    style={{ padding: '0.75rem 1.5rem', background: '#ef4444', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}
                                >
                                    💥 맞기 (-1 HP)
                                </button>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* Indians 대기 안내 (큐 첫 사람이 본인 아닐 때) */}
            {bang.pending?.type === 5 && bang.pending.responderQueue?.[0] !== myId && (() => {
                const next = bang.players.find(p => p.playerId === bang.pending!.responderQueue?.[0]);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #ef4444', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#ef4444', zIndex: 1500 }}>
                        🪶 Indians — <b>{next?.name}</b> 응답 대기 중 ({bang.pending.responderQueue?.length}명 남음)
                    </div>
                );
            })()}

            {/* Gatling 응답 모달 — 큐의 첫 사람이 본인일 때 */}
            {bang.pending?.type === 6 && bang.pending.responderQueue?.[0] === myId && (() => {
                const source = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const hasMissed = me?.hand.some(c => c.type === 1);
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #ef4444', textAlign: 'center', maxWidth: '420px' }}>
                            <img src={CARD_IMAGES[10]} alt="" style={{ width: '80px', height: '80px', filter: 'invert(0.85) sepia(0.5) saturate(3) hue-rotate(330deg)', marginBottom: '0.5rem' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                            <h2 style={{ color: '#ef4444', marginTop: 0 }}>🔫 개틀링!</h2>
                            <p style={{ fontSize: '1rem', margin: '1rem 0' }}>
                                <b>{source?.name ?? '상대'}</b>이(가) 개틀링 발사.
                            </p>
                            <p style={{ color: '#94a3b8', fontSize: '0.9rem' }}>
                                Missed로 회피하거나 1 데미지를 받습니다.
                            </p>
                            <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem', justifyContent: 'center' }}>
                                <button
                                    onClick={() => connection.invoke("RespondGatling", roomId, true)}
                                    disabled={!hasMissed}
                                    style={{
                                        padding: '0.75rem 1.5rem',
                                        background: hasMissed ? '#22c55e' : '#334155',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '0.5rem',
                                        cursor: hasMissed ? 'pointer' : 'not-allowed',
                                        fontWeight: 'bold'
                                    }}
                                >
                                    🛡 Missed 사용
                                </button>
                                <button
                                    onClick={() => connection.invoke("RespondGatling", roomId, false)}
                                    style={{ padding: '0.75rem 1.5rem', background: '#ef4444', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}
                                >
                                    💥 맞기 (-1 HP)
                                </button>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* Gatling 대기 안내 */}
            {bang.pending?.type === 6 && bang.pending.responderQueue?.[0] !== myId && (() => {
                const next = bang.players.find(p => p.playerId === bang.pending!.responderQueue?.[0]);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #ef4444', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#ef4444', zIndex: 1500 }}>
                        🔫 Gatling — <b>{next?.name}</b> 응답 대기 중 ({bang.pending.responderQueue?.length}명 남음)
                    </div>
                );
            })()}

            {/* Duel 응답 모달 — TargetPlayerId가 본인일 때 */}
            {bang.pending?.type === 7 && bang.pending.targetPlayerId === myId && (() => {
                const opponent = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const hasBang = me?.hand.some(c => c.type === 0);
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #a855f7', textAlign: 'center', maxWidth: '420px' }}>
                            <img src={CARD_IMAGES[11]} alt="" style={{ width: '80px', height: '80px', filter: 'invert(0.85) sepia(0.5) saturate(3) hue-rotate(260deg)', marginBottom: '0.5rem' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                            <h2 style={{ color: '#a855f7', marginTop: 0 }}>⚔ 결투!</h2>
                            <p style={{ fontSize: '1rem', margin: '1rem 0' }}>
                                <b>{opponent?.name ?? '상대'}</b>와의 결투 — 뱅!을 던지지 않으면 1 데미지.
                            </p>
                            <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem', justifyContent: 'center' }}>
                                <button
                                    onClick={() => connection.invoke("RespondDuel", roomId, true)}
                                    disabled={!hasBang}
                                    style={{
                                        padding: '0.75rem 1.5rem',
                                        background: hasBang ? '#22c55e' : '#334155',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '0.5rem',
                                        cursor: hasBang ? 'pointer' : 'not-allowed',
                                        fontWeight: 'bold'
                                    }}
                                >
                                    🔫 BANG 던지기
                                </button>
                                <button
                                    onClick={() => connection.invoke("RespondDuel", roomId, false)}
                                    style={{ padding: '0.75rem 1.5rem', background: '#ef4444', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}
                                >
                                    💥 항복 (-1 HP)
                                </button>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* Duel 대기 안내 (Target이 본인 아닐 때) */}
            {bang.pending?.type === 7 && bang.pending.targetPlayerId !== myId && (() => {
                const target = bang.players.find(p => p.playerId === bang.pending!.targetPlayerId);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #a855f7', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#a855f7', zIndex: 1500 }}>
                        ⚔ Duel — <b>{target?.name}</b> 응답 대기 중
                    </div>
                );
            })()}

            {/* Pedro Ramirez 드로우 옵션 — 본인일 때 */}
            {bang.pending?.type === 8 && bang.pending.sourcePlayerId === myId && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                    <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #a78bfa', textAlign: 'center', maxWidth: '440px' }}>
                        <img src={CHARACTER_IMAGES[11]} alt="" style={{ width: '70px', height: '70px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                        <h2 style={{ color: '#a78bfa', marginTop: 0 }}>🎭 페드로 라미레즈</h2>
                        <p>드로우 첫 카드를 어디서?</p>
                        <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem', justifyContent: 'center' }}>
                            <button onClick={() => connection.invoke("ChoosePedroRamirez", roomId, true)} style={{ padding: '0.75rem 1.5rem', background: '#22c55e', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}>덱에서 2장</button>
                            <button onClick={() => connection.invoke("ChoosePedroRamirez", roomId, false)} style={{ padding: '0.75rem 1.5rem', background: '#a78bfa', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}>버린 카드 top + 덱 1장</button>
                        </div>
                    </div>
                </div>
            )}

            {/* Jesse Jones 드로우 옵션 */}
            {bang.pending?.type === 9 && bang.pending.sourcePlayerId === myId && (() => {
                const targets = bang.players.filter(p => p.playerId !== myId && p.health > 0 && p.hand.length > 0);
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #a78bfa', textAlign: 'center', maxWidth: '500px' }}>
                            <img src={CHARACTER_IMAGES[7]} alt="" style={{ width: '70px', height: '70px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                            <h2 style={{ color: '#a78bfa', marginTop: 0 }}>🎭 제시 존스</h2>
                            <p>첫 카드를 어디서?</p>
                            <div style={{ marginTop: '1rem' }}>
                                <button onClick={() => connection.invoke("ChooseJesseJones", roomId, true, null)} style={{ padding: '0.75rem 1.5rem', background: '#22c55e', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold', marginBottom: '1rem' }}>덱에서 2장</button>
                            </div>
                            <div style={{ color: '#94a3b8', fontSize: '0.85rem', margin: '0.5rem 0' }}>또는 다른 사람 손에서 무작위 1장:</div>
                            <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'center' }}>
                                {targets.map(t => (
                                    <button key={t.playerId} onClick={() => connection.invoke("ChooseJesseJones", roomId, false, t.playerId)} style={{ padding: '0.5rem 0.75rem', background: '#a78bfa', color: 'white', border: 'none', borderRadius: '0.4rem', cursor: 'pointer', fontSize: '0.85rem', fontWeight: 'bold' }}>
                                        {t.name} (손 {t.hand.length}장)
                                    </button>
                                ))}
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* Kit Carlson 3장 중 1장 덱으로 */}
            {bang.pending?.type === 10 && bang.pending.sourcePlayerId === myId && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                    <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #a78bfa', textAlign: 'center', maxWidth: '600px' }}>
                        <img src={CHARACTER_IMAGES[9]} alt="" style={{ width: '70px', height: '70px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                        <h2 style={{ color: '#a78bfa', marginTop: 0 }}>🎭 킷 칼슨</h2>
                        <p>3장 중 <b>덱으로 돌려보낼</b> 1장 선택 (나머지 2장은 손으로)</p>
                        <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'center', marginTop: '1.5rem' }}>
                            {bang.pending.revealedCards?.map(c => {
                                const suit = c.suit;
                                const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#a78bfa';
                                return (
                                    <button key={c.id} className="bang-modal-card" onClick={() => connection.invoke("ChooseKitCarlson", roomId, c.id)}
                                        style={{
                                            position: 'relative',
                                            width: '90px', height: '130px',
                                            padding: '0.4rem',
                                            background: '#0f172a',
                                            border: '2px solid #a78bfa',
                                            borderRadius: '0.5rem',
                                            color: '#a78bfa',
                                            fontWeight: 'bold',
                                            cursor: 'pointer',
                                            display: 'flex', flexDirection: 'column',
                                            alignItems: 'center', justifyContent: 'center'
                                        }}>
                                        <div style={{ position: 'absolute', top: '0.2rem', left: '0.3rem', color: suitColor, fontSize: '0.75rem', lineHeight: 1 }}>
                                            {rankLabel(c.rank)}<div>{suitSym}</div>
                                        </div>
                                        <img src={CARD_IMAGES[c.type]} alt="" style={{ width: '44px', height: '44px', filter: 'invert(0.85) sepia(0.3) saturate(2) hue-rotate(240deg)', marginBottom: '0.3rem' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                        <div style={{ fontSize: '0.75rem' }}>{CARD_NAMES[c.type]}</div>
                                    </button>
                                );
                            })}
                        </div>
                    </div>
                </div>
            )}

            {/* Lucky Duke 펴기 선택 모달 — pending type 11, target만 */}
            {bang.pending?.type === 11 && bang.pending.targetPlayerId === myId && (() => {
                const cards = bang.pending.revealedCards ?? [];
                const ctx = bang.pending.context ?? '';
                const ctxMap: Record<string, string> = {
                    'barrel_bang': '🛢 Barrel 펴기 (♥이면 BANG 회피)',
                    'barrel_punch': '🛢 Barrel 펴기 (♥이면 Punch 회피)',
                    'barrel_springfield': '🛢 Barrel 펴기 (♥이면 Springfield 회피)',
                    'barrel_buffalo': '🛢 Barrel 펴기 (♥이면 Buffalo Rifle 회피)',
                    'barrel_doc': '🛢 Barrel 펴기 (♥이면 Doc 능력 회피)',
                    'ironplate_bang': '🛡 Iron Plate 펴기 (♠이면 BANG 회피)',
                    'ironplate_punch': '🛡 Iron Plate 펴기 (♠이면 Punch 회피)',
                    'ironplate_springfield': '🛡 Iron Plate 펴기 (♠이면 Springfield 회피)',
                    'ironplate_buffalo': '🛡 Iron Plate 펴기 (♠이면 Buffalo Rifle 회피)',
                    'dynamite': '💣 Dynamite 펴기 (♠ 2~9이면 폭발 3 데미지, 아니면 다음 사람 전달)',
                    'jail': '⛓ Jail 펴기 (♥이면 탈옥 + 드로우, 아니면 턴 스킵)'
                };
                const ctxLabel = ctxMap[ctx] ?? '펴기';
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #fbbf24', textAlign: 'center', maxWidth: '500px' }}>
                            <h2 style={{ color: '#fbbf24', marginTop: 0 }}>🍀 Lucky Duke</h2>
                            <p style={{ fontSize: '1rem', margin: '0.5rem 0', color: '#cbd5e1' }}>{ctxLabel}</p>
                            <p style={{ fontSize: '0.85rem', color: '#94a3b8', marginBottom: '1rem' }}>두 카드 중 1장 선택</p>
                            <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'center' }}>
                                {cards.map(c => {
                                    const suit = c.suit;
                                    const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                    const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#f1f5f9';
                                    return (
                                        <button
                                            key={c.id}
                                            className="bang-modal-card"
                                            onClick={() => connection.invoke("ChooseLuckyDuke", roomId, c.id)}
                                            style={{
                                                position: 'relative',
                                                width: '100px', height: '140px',
                                                background: '#0f172a',
                                                border: '2px solid #fbbf24',
                                                borderRadius: '0.6rem',
                                                cursor: 'pointer',
                                                display: 'flex', flexDirection: 'column',
                                                alignItems: 'center', justifyContent: 'space-between',
                                                padding: '0.5rem',
                                                color: '#f1f5f9'
                                            }}
                                        >
                                            <div style={{ alignSelf: 'flex-start', color: suitColor, fontSize: '1rem', fontWeight: 'bold', lineHeight: 1 }}>
                                                {rankLabel(c.rank)}<div>{suitSym}</div>
                                            </div>
                                            <img
                                                src={CARD_IMAGES[c.type]}
                                                alt={CARD_NAMES[c.type]}
                                                onError={(e) => { e.currentTarget.style.display = 'none'; }}
                                                style={{ width: '48px', height: '48px', filter: 'invert(0.9) sepia(0.5) saturate(2) hue-rotate(15deg)' }}
                                            />
                                            <div style={{ fontSize: '0.85rem', fontWeight: 'bold' }}>{CARD_NAMES[c.type]}</div>
                                            <div style={{ alignSelf: 'flex-end', color: suitColor, fontSize: '1rem', fontWeight: 'bold', lineHeight: 1, transform: 'rotate(180deg)' }}>
                                                {rankLabel(c.rank)}<div>{suitSym}</div>
                                            </div>
                                        </button>
                                    );
                                })}
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* Lucky Duke 펴기 대기 안내 — target 아닌 사람 */}
            {bang.pending?.type === 11 && bang.pending.targetPlayerId !== myId && (() => {
                const target = bang.players.find(p => p.playerId === bang.pending!.targetPlayerId);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #fbbf24', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#fbbf24', zIndex: 1500 }}>
                        🍀 <b>{target?.name}</b>의 Lucky Duke 펴기 선택 중...
                    </div>
                );
            })()}

            {/* 8/9/10 — 본인 아닐 때 대기 안내 */}
            {bang.pending && [8, 9, 10].includes(bang.pending.type) && bang.pending.sourcePlayerId !== myId && (() => {
                const src = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const labels: Record<number, string> = { 8: 'Pedro Ramirez', 9: 'Jesse Jones', 10: 'Kit Carlson' };
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #a78bfa', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#a78bfa', zIndex: 1500 }}>
                        🎭 <b>{src?.name}</b>의 {labels[bang.pending.type]} 드로우 선택 중...
                    </div>
                );
            })()}

            {/* Cat Balou/Panic 카드 선택 모달 — 사용자(source)에게만 */}
            {bang.pending?.type === 4 && bang.pending.sourcePlayerId === myId && (() => {
                const target = bang.players.find(p => p.playerId === bang.pending!.targetPlayerId);
                if (!target) return null;
                const isPanic = bang.pending!.pickToHand;
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #38bdf8', textAlign: 'center', maxWidth: '520px' }}>
                            <h2 style={{ color: '#38bdf8', marginTop: 0 }}>{isPanic ? '🎯 Panic' : '😼 Cat Balou'}</h2>
                            <p style={{ fontSize: '1rem', margin: '1rem 0' }}>
                                <b>{target.name}</b>의 카드 1장을 <b>{isPanic ? '가져옴' : '버림'}</b>
                            </p>
                            <p style={{ color: '#94a3b8', fontSize: '0.85rem' }}>
                                손패는 비공개(번호로 선택), 장비/무기는 공개됨
                            </p>
                            <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap', justifyContent: 'center', marginTop: '1.5rem' }}>
                                {target.hand.map((c, idx) => (
                                    <button
                                        key={c.id}
                                        className="bang-modal-card"
                                        onClick={() => connection.invoke("PickBangCardFromTarget", roomId, c.id)}
                                        style={{
                                            width: '72px', height: '100px',
                                            background: 'linear-gradient(135deg, #1e3a5f, #0f1e30)',
                                            border: '2px solid #38bdf8', borderRadius: '0.5rem',
                                            color: '#38bdf8', fontSize: '1.2rem', fontWeight: 'bold',
                                            cursor: 'pointer', display: 'flex', flexDirection: 'column',
                                            alignItems: 'center', justifyContent: 'center', gap: '0.5rem'
                                        }}
                                        title={`손패 ${idx + 1}번 선택`}
                                    >
                                        <div style={{ fontSize: '1.5rem' }}>🂠</div>
                                        <div style={{ fontSize: '0.85rem' }}>{idx + 1}</div>
                                    </button>
                                ))}
                                {target.weapon && (() => {
                                    const w = target.weapon;
                                    const suit = w.suit;
                                    const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                    const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#fecaca';
                                    return (
                                        <button
                                            key={w.id}
                                            className="bang-modal-card"
                                            onClick={() => connection.invoke("PickBangCardFromTarget", roomId, w.id)}
                                            style={{
                                                position: 'relative',
                                                width: '90px', height: '100px',
                                                background: 'linear-gradient(135deg, #7f1d1d, #450a0a)',
                                                border: '2px solid #ef4444', borderRadius: '0.5rem',
                                                color: '#fecaca', fontSize: '0.75rem', fontWeight: 'bold',
                                                cursor: 'pointer', display: 'flex', flexDirection: 'column',
                                                alignItems: 'center', justifyContent: 'center', gap: '0.25rem',
                                                padding: '0.25rem'
                                            }}
                                            title="무기 선택"
                                        >
                                            <div style={{ position: 'absolute', top: '0.2rem', left: '0.3rem', color: suitColor, fontSize: '0.7rem', lineHeight: 1 }}>
                                                {rankLabel(w.rank)}{suitSym}
                                            </div>
                                            <img src={CARD_IMAGES[w.type]} alt="" style={{ width: '32px', height: '32px', filter: 'invert(0.9) sepia(0.4) saturate(2) hue-rotate(310deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                            <div>{CARD_NAMES[w.type]}</div>
                                        </button>
                                    );
                                })()}
                                {target.equipment.map(eq => {
                                    const suit = eq.suit;
                                    const suitSym = suit !== undefined ? SUIT_SYMBOLS[suit] : '';
                                    const suitColor = suit !== undefined ? SUIT_COLORS_ON_DARK[suit] : '#bae6fd';
                                    return (
                                        <button
                                            key={eq.id}
                                            className="bang-modal-card"
                                            onClick={() => connection.invoke("PickBangCardFromTarget", roomId, eq.id)}
                                            style={{
                                                position: 'relative',
                                                width: '90px', height: '100px',
                                                background: 'linear-gradient(135deg, #1e3a5f, #082f4a)',
                                                border: '2px solid #38bdf8', borderRadius: '0.5rem',
                                                color: '#bae6fd', fontSize: '0.75rem', fontWeight: 'bold',
                                                cursor: 'pointer', display: 'flex', flexDirection: 'column',
                                                alignItems: 'center', justifyContent: 'center', gap: '0.25rem',
                                                padding: '0.25rem'
                                            }}
                                            title="장비 선택"
                                        >
                                            <div style={{ position: 'absolute', top: '0.2rem', left: '0.3rem', color: suitColor, fontSize: '0.7rem', lineHeight: 1 }}>
                                                {rankLabel(eq.rank)}{suitSym}
                                            </div>
                                            <img src={CARD_IMAGES[eq.type]} alt="" style={{ width: '32px', height: '32px', filter: 'invert(0.9) sepia(0.5) saturate(2) hue-rotate(180deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                            <div>{CARD_NAMES[eq.type]}</div>
                                        </button>
                                    );
                                })}
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* 다른 사람이 Cat Balou/Panic 선택 중일 때 안내 */}
            {bang.pending?.type === 4 && bang.pending.sourcePlayerId !== myId && (() => {
                const source = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const target = bang.players.find(p => p.playerId === bang.pending!.targetPlayerId);
                const isPanic = bang.pending!.pickToHand;
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #38bdf8', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#38bdf8', zIndex: 1500 }}>
                        ⏳ <b>{source?.name}</b>이(가) {isPanic ? 'Panic' : 'Cat Balou'}으로 <b>{target?.name}</b>의 카드 선택 중...
                    </div>
                );
            })()}

            {/* BANG 응답 모달 — 본인이 응답 대상일 때만 */}
            {bang.pending?.type === 1 && bang.pending.targetPlayerId === myId && (() => {
                const source = bang.players.find(p => p.playerId === bang.pending!.sourcePlayerId);
                const hasMissed = me?.hand.some(c => c.type === 1);
                return (
                    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                        <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #ef4444', textAlign: 'center', maxWidth: '420px' }}>
                            <img src={CARD_IMAGES[0]} alt="" style={{ width: '80px', height: '80px', filter: 'invert(0.85) sepia(0.5) saturate(3) hue-rotate(330deg)', marginBottom: '0.5rem' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                            <h2 style={{ color: '#ef4444', marginTop: 0 }}>🔫 뱅!</h2>
                            <p style={{ fontSize: '1.1rem', margin: '1rem 0' }}>
                                <b>{source?.name ?? '상대'}</b>이(가) 당신에게 뱅을 사용했습니다.
                            </p>
                            <p style={{ color: '#94a3b8', fontSize: '0.9rem' }}>
                                {hasMissed
                                    ? 'Missed 카드로 회피하거나 데미지를 받습니다.'
                                    : 'Missed 카드가 없어 회피할 수 없습니다.'}
                            </p>
                            <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem', justifyContent: 'center' }}>
                                <button
                                    onClick={() => connection.invoke("RespondBang", roomId, true)}
                                    disabled={!hasMissed}
                                    style={{
                                        padding: '0.75rem 1.5rem',
                                        background: hasMissed ? '#22c55e' : '#334155',
                                        color: 'white',
                                        border: 'none',
                                        borderRadius: '0.5rem',
                                        cursor: hasMissed ? 'pointer' : 'not-allowed',
                                        fontWeight: 'bold'
                                    }}
                                >
                                    🛡 Missed 사용
                                </button>
                                <button
                                    onClick={() => connection.invoke("RespondBang", roomId, false)}
                                    style={{ padding: '0.75rem 1.5rem', background: '#ef4444', color: 'white', border: 'none', borderRadius: '0.5rem', cursor: 'pointer', fontWeight: 'bold' }}
                                >
                                    💥 맞기 (-1 HP)
                                </button>
                            </div>
                        </div>
                    </div>
                );
            })()}

            {/* 응답 대기 안내 — 본인은 대상이 아니지만 진행 중일 때 */}
            {bang.pending?.type === 1 && bang.pending.targetPlayerId !== myId && (() => {
                const target = bang.players.find(p => p.playerId === bang.pending!.targetPlayerId);
                return (
                    <div style={{ position: 'fixed', bottom: '1rem', left: '50%', transform: 'translateX(-50%)', background: '#1e293b', border: '1px solid #f59e0b', padding: '0.75rem 1.5rem', borderRadius: '0.5rem', color: '#f59e0b', zIndex: 1500 }}>
                        ⏳ <b>{target?.name}</b>의 BANG 응답 대기 중...
                    </div>
                );
            })()}

            {bang.isFinished && (
                <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.7)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 2000 }}>
                    <div style={{ background: '#1e293b', padding: '2rem', borderRadius: '1rem', border: '2px solid #f59e0b', textAlign: 'center', maxWidth: '500px' }}>
                        <h2 style={{ color: '#f59e0b', marginTop: 0 }}>🏆 게임 종료</h2>
                        {bang.winnerPlayerIds.length > 0 && (() => {
                            const winners = bang.winnerPlayerIds.map(id => bang.players.find(p => p.playerId === id)).filter(Boolean) as typeof bang.players;
                            return (
                                <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'center', marginBottom: '1rem', flexWrap: 'wrap' }}>
                                    {winners.map(w => (
                                        <div key={w.playerId} style={{ background: '#0f172a', padding: '0.75rem', borderRadius: '0.5rem', border: '2px solid #fbbf24', minWidth: '100px' }}>
                                            <img src={CHARACTER_IMAGES[w.character]} alt="" style={{ width: '60px', height: '60px', filter: 'invert(0.85) sepia(0.5) saturate(3) hue-rotate(15deg)' }} onError={(e) => { e.currentTarget.style.display = 'none'; }} />
                                            <div style={{ color: '#fbbf24', fontWeight: 'bold', fontSize: '0.9rem' }}>{w.isBot && '🤖 '}{w.name}</div>
                                            <div style={{ color: '#94a3b8', fontSize: '0.75rem' }}>{CHARACTER_NAMES[w.character]}</div>
                                            <div style={{ color: '#f59e0b', fontSize: '0.7rem', marginTop: '0.2rem' }}>{ROLE_NAMES[w.role]}</div>
                                        </div>
                                    ))}
                                </div>
                            );
                        })()}
                        <p style={{ fontSize: '1.1rem', margin: '0.5rem 0', color: '#cbd5e1' }}>{bang.endReason}</p>
                        <div style={{ display: 'flex', gap: '0.75rem', justifyContent: 'center', marginTop: '1rem' }}>
                            {room.hostPlayerId === myId && (
                                <button
                                    onClick={() => connection.invoke("RestartBangGame", roomId)}
                                    style={{ padding: '0.75rem 1.5rem', background: '#a855f7', border: 'none', borderRadius: '0.5rem', color: 'white', fontWeight: 'bold', cursor: 'pointer', fontSize: '1rem' }}
                                >
                                    🔄 재대국
                                </button>
                            )}
                            <button onClick={() => navigate(`/room/${roomId}`)} style={{ padding: '0.75rem 2rem', background: '#22c55e', border: 'none', borderRadius: '0.5rem', color: 'white', fontWeight: 'bold', cursor: 'pointer', fontSize: '1rem' }}>
                                대기실로
                            </button>
                            <button onClick={() => navigate('/lobby?showStats=1')} style={{ padding: '0.75rem 1.5rem', background: '#7c3aed', border: 'none', borderRadius: '0.5rem', color: 'white', fontWeight: 'bold', cursor: 'pointer', fontSize: '1rem' }}>
                                📊 통계
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
}
