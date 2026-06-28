// GamePage.css를 임포트하되, 아래 스타일 태그로 충돌을 막습니다.
import "../css/GamePage.css";

export default function RulePage() {
    const rules = [
        {
            title: "2-2-2",
            description: "같은 숫자의 카드가 2장씩 3쌍",
            score: "+0점",
            example: ["A", "A", "B", "B", "9", "9"]
        },
        {
            title: "3-3",
            description: "같은 숫자의 카드가 3장씩 2쌍",
            score: "+0점",
            example: ["7", "7", "7", "8", "8", "8"]
        },
        {
            title: "4-2",
            description: "같은 숫자의 카드가 4장, 2장",
            score: "-100점",
            example: ["4", "4", "4", "4", "2", "2"]
        },
        {
            title: "스트레이트",
            description: "순서대로 6장",
            score: "합계만큼 - n점",
            example: ["4", "5", "6", "7", "8", "9"]
        },
        {
            title: "68-",
            description: "6장 카드의 값이 68 이상인 경우",
            score: "카드의 합계만큼 - n점",
            example: ["11", "11", "11", "12", "12", "13"]
        },
        {
            title: "뻥",
            description: "상대방이 카드를 버릴 때 뻥 버튼을 눌러 내 패에 해당카드 2장과 추가 카드 1장을 버림",
            score: "-",
            example: ["J", "J", "3", "4", "8"]
        },
        {
            title: "바가지",
            description: "뻥 후 내 패에 2장이 같을 때 상대방이 그 카드와 같은 카드를 내는 경우",
            score: "상대방 + 30점",
            example: ["10", "10"]
        },
        {
            title: "자연바가지",
            description: "내 패에 같은 카드가 3장, 2장이 있는데 상대방이 3장에 해당하는 카드를 버리는 경우",
            score: "상대방 + 30점",
            example: ["7", "7", "7", "Q", "Q"]
        }
    ];

    return (
        <div className="rule-page-outer" style={{ backgroundColor: '#2c3e50', minHeight: '100vh' }}>
            {/* 🛠 중요: 전역 body 스타일 초기화 (짤림 방지 핵심) */}
            <style>{`
                html, body {
                    height: auto !important;
                    overflow: auto !important;
                    display: block !important;
                    margin: 0;
                    padding: 0;
                }
                #root {
                    display: block !important;
                }
            `}</style>

            <div className="rule-page-container" style={{ padding: '50px 20px', color: 'white' }}>
                <h1 style={{ textAlign: 'center', color: '#f1c40f', marginBottom: '40px', marginTop: 0 }}>
                    📜 게임 족보 가이드
                </h1>
                
                <div style={{ maxWidth: '800px', margin: '0 auto', display: 'flex', flexDirection: 'column', gap: '25px' }}>
                    {rules.map((rule, index) => (
                        <div key={index} style={{ 
                            background: '#34495e', 
                            borderRadius: '15px', 
                            padding: '20px', 
                            boxShadow: '0 4px 15px rgba(0,0,0,0.3)',
                            borderLeft: '5px solid #f1c40f'
                        }}>
                            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '15px' }}>
                                <h2 style={{ margin: 0, color: '#f1c40f' }}>{rule.title}</h2>
                                <span style={{ fontSize: '1rem', color: '#f1c40f', fontWeight: 'bold' }}>
                                    Score: {rule.score}
                                </span>
                            </div>
                            <p style={{ marginBottom: '15px', color: '#ecf0f1', lineHeight: '1.5' }}>
                                {rule.description}
                            </p>
                            
                            {/* 카드 디자인 유지 */}
                            <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
                                {rule.example.map((ex, i) => (
                                    <div key={i} style={{ 
                                        width: '45px', 
                                        height: '65px', 
                                        background: 'white', 
                                        color: 'black', 
                                        borderRadius: '6px', 
                                        display: 'flex', 
                                        justifyContent: 'center', 
                                        alignItems: 'center',
                                        fontWeight: 'bold',
                                        fontSize: '1.2rem',
                                        boxShadow: '2px 2px 5px rgba(0,0,0,0.3)'
                                    }}>
                                        {ex}
                                    </div>
                                ))}
                            </div>
                        </div>
                    ))}
                </div>

                <div style={{ textAlign: 'center', marginTop: '50px', paddingBottom: '50px' }}>
                    <button 
                        onClick={() => window.close()} 
                        style={{ 
                            padding: '12px 40px', 
                            background: '#e74c3c', 
                            color: 'white', 
                            border: 'none', 
                            borderRadius: '8px', 
                            cursor: 'pointer', 
                            fontWeight: 'bold',
                            fontSize: '1.1rem'
                        }}
                    >
                        닫기
                    </button>
                </div>
            </div>
        </div>
    );
}