화투 모드 카드 이미지 폴더

파일 컨벤션:
  <rank>_<suit>.png    예: 1_spade.png, 2_heart.png, 12_club.png
  joker.png            조커 카드 (모드와 무관하게 단일 이미지)

rank: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11(=J), 12(=Q)
suit: spade, heart, diamond, club

서버는 Rank를 "A","2"~"10","J","Q" 문자열로 전송함. 프론트에서 다음 매핑으로 파일명 결정:
  A → 1, J → 11, Q → 12, 그 외는 그대로 숫자

이미지가 없으면 GamePage가 자동으로 텍스트 카드로 fallback 렌더링.
총 필요 이미지: 12 rank × 4 suit + 1 joker = 49장 (단, 사용자가 원하는 만큼만 만들어도 됨)
