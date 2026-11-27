효과음 폴더

여기에 다음 파일을 넣으면 자동 재생됨. 파일이 없으면 조용히 무시 (게임 진행에 영향 없음).

※ 카드 버림(play-card), 카드 뽑기(draw-card), 본인 차례(my-turn), 뻥(pung)은
  src/sound/synth.ts가 Web Audio로 직접 만든 소리를 쓰므로 파일이 필요 없고, 넣어도 쓰이지 않음.
  음원 파일로 바꾸려면 synth.ts의 SYNTHS에서 해당 항목을 빼고 파일을 넣을 것.

필요한 파일 (mp3 권장, wav/ogg도 가능 — 단 코드의 경로와 정확히 일치해야 함):
  place-stone.mp3   오목 돌 놓을 때 "딱" 소리
  win.mp3           라운드/게임 승리
  resign.mp3        기권
  bang.mp3          뱅 BANG/Punch 발사 — "탕!"
  heal.mp3          뱅 회복 (Beer/Saloon/Whisky/Tequila)
  equip.mp3         뱅 장착 (무기/장비)
  damage.mp3        뱅 데미지/사망/Dynamite 폭발

무료 음원 사이트:
  https://pixabay.com/sound-effects
  https://freesound.org
  https://zapsplat.com

길이 권장: 0.3~1.5초 (트리거형). win.mp3는 2~3초 정도까지 OK.
포맷: mp3 — 모든 브라우저 지원. 용량은 각 100KB 이하 권장.

음원 변경/추가하려면 src/sound/sound.ts의 SoundName + SOUND_FILES 수정.
