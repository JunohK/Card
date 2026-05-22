# card-frontend

## WHY
다인용 컬캘레이션/국한 스타일 카드게임의 클라이언트. 백엔드(`../Card.Api`, .NET 10)와 SignalR로 실시간 통신, JWT 인증.

## WHAT
- React 18 + TypeScript 5.9 + Vite 7
- 라우팅: react-router-dom 6.22
- 실시간: @microsoft/signalr 10 (Hub URL `http://localhost:5101/gamehub`)
- 인증: Context API (`src/auth/authContext.tsx`) + JWT (`accessTokenFactory`)
- 스타일: 페이지별 vanilla CSS (`src/css/<PageName>.css`)
- 상태관리: Context API (Zustand/Redux 미사용)
- 폼 라이브러리: 없음 (vanilla `<input>` + `useState`)
- 테스트: 없음
- 배포: Vercel (`vercel.json`)
- 구조:
  - `src/pages/`   — 페이지 컴포넌트 (Main/Login/Signup/Lobby/Room/Game/Rule)
  - `src/auth/`    — 인증 (Context, API, storage, hook)
  - `src/signalr/` — SignalR 연결 + Provider
  - `src/router/`  — AppRouter
  - `src/css/`     — 페이지별 CSS

## HOW — 핵심 명령어
```
npm run dev      → Vite 개발 서버
npm run build    → tsc -b && vite build (타입체크 + 프로덕션 빌드)
npm run lint     → ESLint (코드 스타일은 이것이 정한다)
npm run preview  → 빌드 결과 미리보기
```

> 타입체크만 따로 돌리려면 `npx tsc -b --noEmit`.

## MUST NOT
- **TypeScript `any` 사용 금지** — `unknown` 또는 구체 타입 사용
- 새 라이브러리 추가 전 반드시 먼저 질문 (Zustand/React Hook Form 등 미사용은 의도된 결정)
- 인라인 스타일 금지 — `src/css/<PageName>.css`에 분리
- `src/signalr.ts`(중복 의심 파일) 변경 시 `src/signalr/`와의 관계 먼저 확인
- 백엔드 포트 변경 시 `src/signalr/connection.ts`의 `http://localhost:5101/gamehub`도 동기화

## Summary Instructions
When summarizing this conversation, always preserve:
- 변경된 파일 경로 전체 목록
- `npm run build` / `npm run lint` 통과 여부와 출력 마지막 5~10줄
- SignalR 이벤트 이름과 페이로드 스키마 변경 사항 (백엔드와 계약)
- 내린 결정과 그 근거
