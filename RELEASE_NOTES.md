# DK Arrow Ez Drawing Tool v1.1.0

배포 구조와 런타임 기반을 정리한 버전입니다. 기존 그리기, 편집, 저장/복원, 설정 저장, PNG 출력 기능은 유지됩니다.

## 변경 사항

- 대상 프레임워크를 .NET 8에서 .NET 10으로 변경
- Windows x64 self-contained 배포를 framework-dependent single-file 배포로 변경
- 사용자 실행 파일 이름은 기존처럼 `DK Arrow Ez Drawing Tool.exe`로 유지
- 실제 WPF 앱을 `app/DK Arrow Ez Drawing Tool.App.exe`로 분리
- 작은 x64 네이티브 런처를 추가하여 .NET 10 Desktop Runtime 설치 여부를 실행 전에 확인
- 필요한 런타임이 없으면 한국어 안내와 Microsoft 공식 다운로드 페이지 열기 제공
- GitHub Actions 릴리스 버전을 프로젝트 파일에서 읽도록 변경
- 이미 존재하는 같은 버전의 GitHub Release 자산을 덮어쓰지 않도록 변경
- Pull Request에서도 실제 .NET 10 앱/네이티브 런처 빌드와 ZIP 패키징을 검증하도록 변경

## 호환성

- Windows 10/11 x64
- Microsoft .NET 10 Desktop Runtime (x64) 필요
- 기존 사용자 설정은 계속 `%LOCALAPPDATA%\DK Arrow Ez Drawing Tool\settings.json`에 저장되므로 프로그램 파일 교체와 독립적으로 유지됩니다.
