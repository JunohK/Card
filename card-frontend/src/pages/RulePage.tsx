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
            description: "내 패가 같은 카드 3장 + 2장 구성(예: 8 세 장, 9 두 장)일 때 상대방이 2장 쪽 카드(9)를 버리는 경우. 조커 1장 + 2장 + 2장 구성이면 두 숫자 모두 해당. 게임 중 본인이 끌 수 있음",
            score: "상대방 + 30점",
            example: ["8", "8", "8", "9", "9"]
        },
        {
            title: "100-",
            description: "6장 카드의 합이 10 이하인 경우 (조커는 1점으로 계산)",
            score: "-100점",
            example: ["A", "A", "A", "2", "2", "3"]
        },
        {
            title: "200-",
            description: "6장 카드의 합이 10 이하이면서 4-2 구성인 경우",
            score: "-200점",
            example: ["A", "A", "A", "A", "2", "2"]
        },
        {
            title: "65- (화투 모드)",
            description: "화투 모드(K 없음)에서는 68- 대신 합계 65 이상이면 성립",
            score: "카드의 합계만큼 - n점",
            example: ["10", "10", "J", "J", "Q", "Q"]
        },
        {
            title: "조커",
            description: "어떤 카드로도 대체 가능. 스트레이트 빈칸과 세트 구성에 쓰이며, 68- 판정 시 13점(화투 12점), 100-/200- 판정 시 1점으로 계산. 뻥은 같은 숫자 카드를 먼저 쓰고 모자란 만큼 조커로 채움. 라운드 종료 시 남은 조커는 1점",
            score: "-",
            example: ["7", "7", "JK"]
        },
        {
            title: "승리 선언",
            description: "내 턴에 카드를 뽑아 6장이 되고 족보가 완성되면 승리 선언 가능 (내 2번째 턴부터). 승자는 족보 점수, 나머지는 남은 패의 합 (같은 숫자 3장 이상, '2장 + 조커'는 0점 처리)",
            score: "족보 점수",
            example: ["5", "5", "5", "9", "9", "9"]
        },
        {
            title: "STOP",
            description: "내 턴에 카드를 뽑아 3장이고, 패가 2장인 사람이 본인 포함 2명 이상이면 선언 가능. 선언 후 카드 1장을 버리면 라운드 종료. 내 남은 2장의 합이 2장인 다른 사람들 중 최저 점수보다 낮으면 성공",
            score: "성공 시 0점",
            example: ["2", "3"]
        },
        {
            title: "독박 (STOP 실패)",
            description: "STOP 선언자의 남은 2장 합이 2장인 다른 사람의 최저 점수 이상이면 실패. 남은 패 점수에 50점 추가",
            score: "본인 + 50점",
            example: ["K", "Q"]
        },
        {
            title: "시간 제한",
            description: "상대가 카드를 버린 뒤 2초 안에만 뻥 가능. 그 사이 다음 사람은 기다리지 않고 바로 뽑을 수 있고, 이미 뽑은 뒤 뻥이 성립하면 뽑은 카드는 덱으로 돌아가 섞임. 턴당 60초가 지나면 마지막 카드가 자동으로 버려지고 턴이 넘어감 (이때는 뻥 불가). 게임이 끝나면 총점이 가장 낮은 사람이 최종 우승",
            score: "-",
            example: ["2초", "60초"]
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