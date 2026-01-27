import React from "react";
import "./GamePage.css"; // 기존 게임 스타일 재사용

export default function RulePage() {
    // 족보 데이터 정의
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
            title: "65-",
            description: "6장 카드의 값이 65 이상인 경우",
            score: "카드의 합계만큼 - n점",
            example: ["11", "11", "11", "12", "12", "13"]
        },
        {
            title: "뻥",
            description: "상대방이 카드를 버릴 때 내 패에 해당카드 2장과 추가 카드 1장을 버리는 경우",
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
        <div className="rule-page-container" style={{ padding: '30px', backgroundColor: '#2c3e50', minHeight: '100vh', color: 'white' }}>
            <h1 style={{ textAlign: 'center', color: '#f1c40f', marginBottom: '40px' }}>📜 게임 족보 가이드</h1>
            
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
                            <span style={{ fontSize: '0.9rem', color: '#bdc3c7' }}>Score: {rule.score}</span>
                        </div>
                        <p style={{ marginBottom: '15px', color: '#ecf0f1' }}>{rule.description}</p>
                        
                        <div style={{ display: 'flex', gap: '10px' }}>
                            {rule.example.map((ex, i) => (
                                <div key={i} style={{ 
                                    width: '45px', 
                                    height: '65px', 
                                    background: 'white', 
                                    color: 'black', 
                                    borderRadius: '5px', 
                                    display: 'flex', 
                                    justifyContent: 'center', 
                                    alignItems: 'center',
                                    fontWeight: 'bold',
                                    fontSize: '1.2rem'
                                }}>
                                    {ex}
                                </div>
                            ))}
                        </div>
                    </div>
                ))}
            </div>

            <div style={{ textAlign: 'center', marginTop: '40px' }}>
                <button 
                    onClick={() => window.close()} 
                    style={{ padding: '12px 30px', background: '#e74c3c', color: 'white', border: 'none', borderRadius: '8px', cursor: 'pointer', fontWeight: 'bold' }}
                >
                    닫기
                </button>
            </div>
        </div>
    );
}