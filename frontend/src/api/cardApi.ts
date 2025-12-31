export async function pingServer(): Promise<string>{
    const res = await fetch("http://localhost:5101/api/card/ping");

    if(!res.ok){
        throw new Error("API 호출 실패");
    }

    return res.text();
}