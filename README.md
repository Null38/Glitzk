# Glitzk
 
[치지직](https://chzzk.naver.com/) 채팅에서 커스텀 명령어를 감지해 유튜브 영상을 재생하는 프로그램입니다.
 
## 설정 방법
 
### 1. 치지직 애플리케이션 등록
 
[치지직 개발자 센터](https://developers.chzzk.naver.com/)에 접속한 뒤 다음 순서로 애플리케이션을 등록합니다.
 
1. `내 서비스` → `애플리케이션 등록`을 선택합니다.
2. `애플리케이션 ID`와 `애플리케이션 이름`은 적절히 입력합니다.
3. `로그인 리디렉션 URL`을 `http://localhost:8080/api/path/`로 설정합니다.
4. `API Scopes`는 `채팅 메시지 조회`와 `채팅 메시지 쓰기`가 필요합니다.
등록을 마치면 `Client ID`와 `Client Secret`이 발급됩니다.
 
### 2. 프로그램 연동
 
1. 프로그램에서 `Menu` → `Settings` → `Chzzk` 로 이동합니다.
2. `Chzzk Client Id`와 `Chzzk Client Secret`에 위에서 발급받은 값을 입력합니다.
3. `Connect`를 누르면 인증 링크가 열리며 치지직 인증을 요구합니다.
4. 인증을 완료하면 채팅창과 연동됩니다.


## 의존성 (Dependencies)
 
- [SDL3-CS](https://github.com/edwardgushchin/SDL3-CS)
- [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui)
- [Hexa.NET.OpenGL](https://github.com/HexaEngine/Hexa.NET.OpenGL)