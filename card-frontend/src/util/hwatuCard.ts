// 화투 모드 카드 앞면 — 실물 이미지 대신 12달 상징을 단순한 SVG로 그려 data URI로 돌려준다.
// 카드 한 장 = 90×130. 네 모서리에 달 숫자, 가운데 띠에 그 달의 그림. 같은 달 4장은 같은 그림 (화투에는 무늬 개념이 없음).
// 1 소나무·해  2 매화·꾀꼬리  3 벚꽃·장막  4 흑싸리(등나무)  5 창포·다리  6 모란
// 7 홍싸리  8 공산(달·검은 산)  9 국화  10 단풍  11 오동  12 비·버들

const RED = "#c0392b";
const GREEN = "#2e7d32";
const BROWN = "#6d4c2a";
const INK = "#1b1b1b";

const blossom = (x: number, y: number, color: string) =>
    `<circle cx="${x}" cy="${y}" r="6" fill="${color}"/><circle cx="${x}" cy="${y}" r="2" fill="#fff6c8"/>`;

const leaf = (x: number, y: number, rx: number, ry: number, rot: number, color: string) =>
    `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" transform="rotate(${rot} ${x} ${y})" fill="${color}"/>`;

const STAR = "0,-12 3.5,-4 12,-4 5.5,1.5 7.5,10 0,5 -7.5,10 -5.5,1.5 -12,-4 -3.5,-4"; // 단풍잎

const MOTIFS: Record<number, string> = {
    1: `<circle cx="64" cy="44" r="9" fill="${RED}"/>
        <polygon points="30,40 12,66 48,66" fill="${GREEN}"/>
        <polygon points="30,52 8,84 52,84" fill="${GREEN}"/>
        <polygon points="30,64 4,100 56,100" fill="#1f6b2a"/>
        <rect x="27" y="96" width="6" height="10" fill="${BROWN}"/>
        <path d="M56 80 q10 -12 24 -4" stroke="#fff" stroke-width="3" fill="none"/>`,
    2: `<path d="M12 100 C30 80 40 70 70 40" stroke="${BROWN}" stroke-width="4" fill="none"/>
        <path d="M40 70 C50 68 56 60 60 50" stroke="${BROWN}" stroke-width="3" fill="none"/>
        ${blossom(24, 86, "#d94b5c")}${blossom(44, 66, "#d94b5c")}${blossom(62, 46, "#d94b5c")}
        ${blossom(70, 66, "#e8a0b4")}${blossom(30, 56, "#e8a0b4")}
        <ellipse cx="60" cy="88" rx="9" ry="6" fill="#d4a017"/><circle cx="69" cy="85" r="3.5" fill="#d4a017"/>`,
    3: `<rect x="8" y="80" width="74" height="22" fill="${RED}"/>
        <rect x="8" y="80" width="74" height="4" fill="#fff"/>
        <path d="M20 84 V102 M32 84 V102 M44 84 V102 M56 84 V102 M68 84 V102" stroke="#fff" stroke-width="2"/>
        ${blossom(30, 48, "#f1a7c0")}${blossom(46, 40, "#f1a7c0")}${blossom(62, 50, "#f1a7c0")}
        ${blossom(38, 62, "#e889ab")}${blossom(56, 64, "#e889ab")}`,
    4: `<path d="M8 32 H82" stroke="${BROWN}" stroke-width="3"/>
        ${[20, 45, 70].map(x => [0, 1, 2, 3, 4, 5].map(i =>
            `<ellipse cx="${x}" cy="${40 + i * 11}" rx="${6 - i * 0.7}" ry="4" fill="#5b3a8e"/>`).join("")).join("")}
        ${leaf(32, 36, 7, 3.5, -20, GREEN)}${leaf(58, 36, 7, 3.5, 20, GREEN)}`,
    5: `<rect x="6" y="84" width="78" height="8" fill="${BROWN}"/>
        <rect x="14" y="92" width="6" height="12" fill="${BROWN}"/><rect x="70" y="92" width="6" height="12" fill="${BROWN}"/>
        <ellipse cx="45" cy="48" rx="7" ry="14" fill="#3b6fb6"/>
        ${leaf(33, 58, 6, 12, -35, "#3b6fb6")}${leaf(57, 58, 6, 12, 35, "#3b6fb6")}
        <circle cx="45" cy="58" r="3" fill="#f1c40f"/>
        <path d="M45 84 V62 M30 84 L36 56 M60 84 L54 56" stroke="${GREEN}" stroke-width="3" fill="none"/>`,
    6: `<circle cx="45" cy="60" r="20" fill="${RED}"/><circle cx="45" cy="60" r="13" fill="#e35d6a"/><circle cx="45" cy="60" r="6" fill="#f1c40f"/>
        ${leaf(22, 86, 12, 6, -30, GREEN)}${leaf(68, 86, 12, 6, 30, GREEN)}
        <path d="M45 80 V102" stroke="${GREEN}" stroke-width="3"/>`,
    7: `<path d="M20 104 C30 80 40 60 60 36" stroke="${BROWN}" stroke-width="3" fill="none"/>
        <path d="M40 104 C44 84 56 70 76 56" stroke="${BROWN}" stroke-width="3" fill="none"/>
        ${leaf(28, 84, 8, 4, -40, RED)}${leaf(40, 66, 8, 4, -45, "#e74c3c")}${leaf(52, 50, 8, 4, -45, RED)}${leaf(64, 40, 7, 3.5, -40, "#e74c3c")}
        ${leaf(50, 88, 8, 4, -30, "#e74c3c")}${leaf(62, 72, 8, 4, -35, RED)}${leaf(74, 60, 7, 3.5, -35, "#e74c3c")}`,
    8: `<rect x="4" y="28" width="82" height="78" fill="${RED}"/>
        <circle cx="45" cy="52" r="15" fill="#fff"/>
        <path d="M4 106 Q45 58 86 106 Z" fill="${INK}"/>
        <path d="M20 96 l-4 -14 M30 100 l-2 -16 M60 100 l2 -16 M70 96 l4 -14" stroke="${INK}" stroke-width="2"/>`,
    9: `${[...Array(12).keys()].map(i =>
            `<ellipse cx="45" cy="42" rx="5" ry="12" fill="#f1c40f" transform="rotate(${i * 30} 45 58)"/>`).join("")}
        <circle cx="45" cy="58" r="7" fill="#e67e22"/>
        ${leaf(26, 90, 11, 5, -25, GREEN)}${leaf(64, 90, 11, 5, 25, GREEN)}
        <path d="M45 70 V102" stroke="${GREEN}" stroke-width="3"/>`,
    10: `<path d="M45 104 V70" stroke="${BROWN}" stroke-width="4"/>
        <g transform="translate(30 54)"><polygon points="${STAR}" fill="${RED}"/></g>
        <g transform="translate(60 48) scale(0.9)"><polygon points="${STAR}" fill="#e67e22"/></g>
        <g transform="translate(46 78) scale(0.85)"><polygon points="${STAR}" fill="${RED}"/></g>`,
    11: `<ellipse cx="45" cy="80" rx="16" ry="22" fill="${INK}"/>
        ${leaf(22, 84, 12, 18, -25, "#2b2b2b")}${leaf(68, 84, 12, 18, 25, "#2b2b2b")}
        <path d="M45 58 V34" stroke="#7d3c98" stroke-width="2"/>
        ${[[45, 36], [40, 44], [50, 44], [36, 52], [54, 52]].map(([x, y]) =>
            `<circle cx="${x}" cy="${y}" r="3.5" fill="#7d3c98"/>`).join("")}`,
    12: `${[16, 26, 36, 46, 56, 66, 76].map((x, i) =>
            `<path d="M${x} 30 q${i % 2 ? 4 : -4} 30 ${i % 2 ? 2 : -2} 64" stroke="${GREEN}" stroke-width="2.5" fill="none"/>`).join("")}
        <path d="M14 40 l-6 20 M30 36 l-6 20 M50 44 l-6 20 M70 36 l-6 20 M62 72 l-6 20 M26 76 l-6 20 M78 80 l-6 20"
              stroke="#5d7ba3" stroke-width="2"/>`,
};

const cache = new Map<number, string>();

/** month: 1~12. 반환값은 background-image에 바로 쓰는 data URI (같은 달 4장은 같은 그림) */
export function hwatuCardDataUri(month: number): string {
    const hit = cache.get(month);
    if (hit) return hit;

    const textColor = "#2c3e50";
    const motif = MOTIFS[month] ?? "";
    const svg =
        `<svg xmlns="http://www.w3.org/2000/svg" width="90" height="130" viewBox="0 0 90 130">` +
        `<rect width="90" height="130" rx="10" fill="#f7f0dc"/>` +
        `<rect x="3" y="3" width="84" height="124" rx="8" fill="none" stroke="#7a1f1f" stroke-width="1.5"/>` +
        motif +
        `<g font-family="Arial, Helvetica, sans-serif" font-size="14" font-weight="bold" fill="${textColor}">` +
        `<text x="8" y="20">${month}</text><text x="82" y="20" text-anchor="end">${month}</text>` +
        `<g transform="rotate(180 45 65)"><text x="8" y="20">${month}</text><text x="82" y="20" text-anchor="end">${month}</text></g>` +
        `</g>` +
        `</svg>`;

    // CSS url(...)에 따옴표 없이 들어가므로 encodeURIComponent가 남기는 괄호·작은따옴표도 반드시 인코딩
    // (rotate(…) 같은 괄호가 그대로 있으면 url()이 거기서 끊겨 이미지가 깨짐)
    const encoded = encodeURIComponent(svg).replace(/[()']/g, c => `%${c.charCodeAt(0).toString(16).toUpperCase()}`);
    const uri = `data:image/svg+xml;charset=utf-8,${encoded}`;
    cache.set(month, uri);
    return uri;
}
