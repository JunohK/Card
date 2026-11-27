// 합성 효과음 — 브라우저 Web Audio API로 직접 만든다 (음원 파일·외부 라이브러리 없음).
// 카드 버림("탁"), 카드 뽑기("슥"), 본인 차례("띵-동"), 뻥("뻥!") 네 가지. sound.ts의 playSound가 호출한다.
import type { SoundName } from "./sound";

let ctx: AudioContext | null = null;

// AudioContext는 하나만 만들어 재사용. 사용자가 페이지를 한 번이라도 클릭하기 전에는
// 브라우저 자동재생 정책 때문에 suspended 상태라 소리가 나지 않는다 (클릭 이후부터 재생)
function getContext(): AudioContext | null {
    if (typeof window === "undefined" || typeof window.AudioContext === "undefined") return null;
    if (!ctx) ctx = new AudioContext();
    if (ctx.state === "suspended") ctx.resume().catch(() => { /* 사용자 클릭 전 — 다음 재생 때 다시 시도 */ });
    return ctx;
}

// 백색 소음 버퍼 — 종이·카드가 스치는 소리의 재료
function createNoise(c: AudioContext, seconds: number): AudioBufferSourceNode {
    const buffer = c.createBuffer(1, Math.floor(c.sampleRate * seconds), c.sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = Math.random() * 2 - 1;
    const source = c.createBufferSource();
    source.buffer = buffer;
    return source;
}

// 카드 버림 — 짧은 "탁": 소음을 중간 대역만 남겨 빠르게 줄이고, 낮은 울림을 살짝 더함
function cardSlap(c: AudioContext, volume: number): void {
    const t = c.currentTime;

    const noise = createNoise(c, 0.12);
    const band = c.createBiquadFilter();
    band.type = "bandpass";
    band.frequency.value = 1800;
    band.Q.value = 0.8;
    const noiseGain = c.createGain();
    noiseGain.gain.setValueAtTime(0.0001, t);
    noiseGain.gain.exponentialRampToValueAtTime(volume, t + 0.003);
    noiseGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.12);
    noise.connect(band).connect(noiseGain).connect(c.destination);
    noise.start(t);
    noise.stop(t + 0.12);

    const thump = c.createOscillator();
    thump.type = "sine";
    thump.frequency.setValueAtTime(170, t);
    thump.frequency.exponentialRampToValueAtTime(80, t + 0.08);
    const thumpGain = c.createGain();
    thumpGain.gain.setValueAtTime(volume * 0.6, t);
    thumpGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.09);
    thump.connect(thumpGain).connect(c.destination);
    thump.start(t);
    thump.stop(t + 0.1);
}

// 카드 뽑기 — "슥": 소음의 대역을 낮은 쪽에서 높은 쪽으로 쓸어 올림
function cardSwish(c: AudioContext, volume: number): void {
    const t = c.currentTime;
    const noise = createNoise(c, 0.22);
    const band = c.createBiquadFilter();
    band.type = "bandpass";
    band.Q.value = 1.2;
    band.frequency.setValueAtTime(600, t);
    band.frequency.exponentialRampToValueAtTime(3200, t + 0.2);
    const gain = c.createGain();
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(volume * 0.7, t + 0.06);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.22);
    noise.connect(band).connect(gain).connect(c.destination);
    noise.start(t);
    noise.stop(t + 0.22);
}

// 본인 차례 — 부드러운 두 음 "띵-동" (솔 → 레)
function turnChime(c: AudioContext, volume: number): void {
    const t = c.currentTime;
    const notes: Array<[number, number]> = [[784, 0], [1175, 0.13]]; // [주파수 Hz, 시작 지연 초]
    for (const [freq, delay] of notes) {
        const osc = c.createOscillator();
        osc.type = "triangle";
        osc.frequency.value = freq;
        const gain = c.createGain();
        gain.gain.setValueAtTime(0.0001, t + delay);
        gain.gain.exponentialRampToValueAtTime(volume * 0.5, t + delay + 0.01);
        gain.gain.exponentialRampToValueAtTime(0.0001, t + delay + 0.4);
        osc.connect(gain).connect(c.destination);
        osc.start(t + delay);
        osc.stop(t + delay + 0.42);
    }
}

// 뻥 — 묵직한 "뻥!": 낮게 떨어지는 울림 + 짧고 날카로운 터짐
function pungPop(c: AudioContext, volume: number): void {
    const t = c.currentTime;

    const boom = c.createOscillator();
    boom.type = "sine";
    boom.frequency.setValueAtTime(260, t);
    boom.frequency.exponentialRampToValueAtTime(55, t + 0.25);
    const boomGain = c.createGain();
    boomGain.gain.setValueAtTime(0.0001, t);
    boomGain.gain.exponentialRampToValueAtTime(volume, t + 0.005);
    boomGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.3);
    boom.connect(boomGain).connect(c.destination);
    boom.start(t);
    boom.stop(t + 0.32);

    const crack = createNoise(c, 0.08);
    const band = c.createBiquadFilter();
    band.type = "bandpass";
    band.frequency.value = 2500;
    band.Q.value = 0.7;
    const crackGain = c.createGain();
    crackGain.gain.setValueAtTime(volume * 0.7, t);
    crackGain.gain.exponentialRampToValueAtTime(0.0001, t + 0.08);
    crack.connect(band).connect(crackGain).connect(c.destination);
    crack.start(t);
    crack.stop(t + 0.08);
}

const SYNTHS: Partial<Record<SoundName, (c: AudioContext, volume: number) => void>> = {
    "play-card": cardSlap,
    "draw-card": cardSwish,
    "my-turn": turnChime,
    "pung": pungPop
};

/** 합성음이 있는 소리면 재생하고 true, 없으면 false (호출 측이 음원 파일로 재생). */
export function playSynth(name: SoundName, volume: number): boolean {
    const synth = SYNTHS[name];
    if (!synth) return false;
    const c = getContext();
    if (!c) return true; // Web Audio 미지원 브라우저 — 소리 없이 넘어감
    synth(c, Math.max(0.0001, Math.min(1, volume)));
    return true;
}
