화투 모드 카드 이미지 폴더

현재 화투 모드 카드 앞면은 이 폴더의 이미지가 아니라 코드에서 그린 SVG로 표시됨.
  src/util/hwatuCard.ts — 12달 상징(소나무·매화·벚꽃·…·비)을 단순 도형으로 그려 data URI로 반환
  GamePage.tsx getCardImageSrc — 화투 모드이면 위 함수 결과를 background-image로 사용
  숫자(달)는 네 모서리에, 무늬(♠♥♦♣, 같은 달 4장 구분)는 위 가운데에 SVG 안에 포함됨

실물 이미지로 바꾸고 싶으면 getCardImageSrc가 이 폴더의 파일 경로를 돌려주도록 수정하면 됨.
  권장 파일 컨벤션: <달>_<suit>.png (예: 1_spade.png … 12_club.png), suit: spade/heart/diamond/club
  서버 Rank 문자열 매핑: A → 1, J → 11, Q → 12, 그 외는 그대로 숫자

조커는 모드와 무관하게 기존 텍스트 카드(노란 배경 🃏)로 표시됨.
