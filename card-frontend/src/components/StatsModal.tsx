import { useEffect, useState } from "react";
import { loadHistory, clearHistory, countsByMode, myWinsByMode, currentStreak, longestStreak, currentStreakByMode, longestStreakByMode, type GameRecord } from "../util/gameHistory";
import "../css/StatsModal.css";

type LeaderboardEntry = { nickname: string; wins: number; totalGames: number; maxScore: number; winRate: number };

/**
 * 게임 기록 / 리더보드 모달 — 로비와 게임 결과창 공용.
 * 페이지를 떠나지 않고 띄우므로 게임 결과창에서 열어도 방에서 나가지지 않는다.
 */
export default function StatsModal({ myName, onClose }: { myName: string; onClose: () => void }) {
    const [stats, setStats] = useState<GameRecord[]>(() => loadHistory());
    const [statsFilter, setStatsFilter] = useState<'all' | 'Cards' | 'Omok' | 'Bang'>('all');
    const [statsTab, setStatsTab] = useState<'mine' | 'leaderboard'>('mine');
    const [leaderboard, setLeaderboard] = useState<LeaderboardEntry[]>([]);

    // 리더보드 fetch (탭 전환 시)
    useEffect(() => {
        if (statsTab !== 'leaderboard') return;
        const apiBase = import.meta.env.VITE_API_BASE_URL ?? '';
        fetch(`${apiBase}/api/leaderboard/top?limit=20`)
            .then(r => r.ok ? r.json() : [])
            .then((data) => setLeaderboard(Array.isArray(data) ? data : []))
            .catch(() => setLeaderboard([]));
    }, [statsTab]);

    const counts = countsByMode();
    const wins = myWinsByMode();
    const streak = currentStreak();
    const longest = longestStreak();
    const streakMode = currentStreakByMode();
    const longestMode = longestStreakByMode();
    const rate = (mode: 'Cards' | 'Omok' | 'Bang') =>
        counts[mode] === 0 ? '-' : `${Math.round((wins[mode] / counts[mode]) * 100)}%`;

    return (
        <div className="stats-modal-overlay" onClick={onClose}>
            <div className="stats-modal-content" onClick={e => e.stopPropagation()}>
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
                                    const me = u.nickname === myName;
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
                            onClick={onClose}
                            className="stats-modal-btn"
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
                        className="stats-modal-btn"
                        style={{ flex: 1, background: '#7f1d1d' }}
                        onClick={() => { if (confirm('전체 기록을 삭제할까요?')) { clearHistory(); setStats([]); } }}
                    >🗑 전체 삭제</button>
                    <button className="stats-modal-btn" style={{ flex: 1 }} onClick={onClose}>닫기</button>
                </div>
                </div>)}
            </div>
        </div>
    );
}
