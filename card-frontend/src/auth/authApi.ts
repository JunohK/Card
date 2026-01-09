// 회원가입
export async function signup(nickname: string, password: string){
    const  res = await fetch("http://localhost:5101/api/auth/signup",{
        method: "POST",
        headers: {
            "Content-Type": "application/json",
        },
        body:JSON.stringify({
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
    const res = await fetch("http://localhost:5101/api/auth/login", {
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