// connection.ts에서 getBaseUrl을 가져옵니다. 
// (파일 경로가 다르다면 ../signalr/connection 등으로 맞춰주세요)
import { getBaseUrl } from "../signalr/connection"; 

// 회원가입
export async function signup(nickname: string, password: string){
    // 주소를 getBaseUrl()을 사용하도록 변경
    const res = await fetch(`${getBaseUrl()}/api/auth/signup`, {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
        },
        body: JSON.stringify({
            nickname,
            password,
        }),
    });

    if(!res.ok) {
        throw new Error(await res.text());
    }
}

// 로그인
export async function login(nickname: string, password: string){
    // 주소를 getBaseUrl()을 사용하도록 변경
    const res = await fetch(`${getBaseUrl()}/api/auth/login`, {
        method: "POST",
        headers: { "Content-Type" : "application/json"},
        body : JSON.stringify({
            nickname,
            password
        })
    });

    if(!res.ok) {
        throw new Error("로그인 실패 : " + await res.text());
    }

    return await res.json(); // {token}
}