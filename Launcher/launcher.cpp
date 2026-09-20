#include <windows.h>
#include <shellapi.h>

#include <cwchar>
#include <string>
#include <vector>

namespace
{
constexpr wchar_t kAppRelativePath[] = L"app\\DK Arrow Ez Drawing Tool.App.exe";
constexpr wchar_t kRuntimeDownloadUrl[] = L"https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime";

std::wstring GetEnvironmentValue(const wchar_t* name)
{
    const DWORD required = GetEnvironmentVariableW(name, nullptr, 0);
    if (required == 0)
        return {};

    std::wstring value(required, L'\0');
    const DWORD written = GetEnvironmentVariableW(name, value.data(), required);
    if (written == 0 || written >= required)
        return {};

    value.resize(written);
    return value;
}

std::wstring ReadDotnetInstallLocation()
{
    HKEY key = nullptr;
    if (RegOpenKeyExW(
            HKEY_LOCAL_MACHINE,
            L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",
            0,
            KEY_READ | KEY_WOW64_64KEY,
            &key) != ERROR_SUCCESS)
    {
        return {};
    }

    DWORD type = 0;
    DWORD bytes = 0;
    if (RegQueryValueExW(key, L"InstallLocation", nullptr, &type, nullptr, &bytes) != ERROR_SUCCESS ||
        (type != REG_SZ && type != REG_EXPAND_SZ) ||
        bytes < sizeof(wchar_t))
    {
        RegCloseKey(key);
        return {};
    }

    std::vector<wchar_t> buffer(bytes / sizeof(wchar_t) + 1, L'\0');
    if (RegQueryValueExW(
            key,
            L"InstallLocation",
            nullptr,
            &type,
            reinterpret_cast<LPBYTE>(buffer.data()),
            &bytes) != ERROR_SUCCESS)
    {
        RegCloseKey(key);
        return {};
    }

    RegCloseKey(key);
    return std::wstring(buffer.data());
}

void AddUniqueRoot(std::vector<std::wstring>& roots, const std::wstring& root)
{
    if (root.empty())
        return;

    for (const auto& existing : roots)
    {
        if (_wcsicmp(existing.c_str(), root.c_str()) == 0)
            return;
    }

    roots.push_back(root);
}

bool HasWindowsDesktopRuntime10At(const std::wstring& dotnetRoot)
{
    const std::wstring base = dotnetRoot + L"\\shared\\Microsoft.WindowsDesktop.App";
    const std::wstring pattern = base + L"\\*";

    WIN32_FIND_DATAW data{};
    HANDLE handle = FindFirstFileW(pattern.c_str(), &data);
    if (handle == INVALID_HANDLE_VALUE)
        return false;

    bool found = false;
    do
    {
        if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
            continue;
        if (data.cFileName[0] == L'.')
            continue;

        int major = 0;
        int minor = 0;
        if (swscanf_s(data.cFileName, L"%d.%d", &major, &minor) >= 2 &&
            major == 10 &&
            minor == 0)
        {
            found = true;
            break;
        }
    } while (FindNextFileW(handle, &data));

    FindClose(handle);
    return found;
}

bool HasWindowsDesktopRuntime10()
{
    std::vector<std::wstring> roots;
    AddUniqueRoot(roots, GetEnvironmentValue(L"DOTNET_ROOT_X64"));
    AddUniqueRoot(roots, GetEnvironmentValue(L"DOTNET_ROOT"));
    AddUniqueRoot(roots, ReadDotnetInstallLocation());

    const auto programFiles = GetEnvironmentValue(L"ProgramFiles");
    if (!programFiles.empty())
        AddUniqueRoot(roots, programFiles + L"\\dotnet");

    for (const auto& root : roots)
    {
        if (HasWindowsDesktopRuntime10At(root))
            return true;
    }

    return false;
}

std::wstring GetLauncherDirectory()
{
    std::wstring path(32768, L'\0');
    const DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size())
        return {};

    path.resize(length);
    const size_t separator = path.find_last_of(L"\\/");
    if (separator == std::wstring::npos)
        return {};

    path.resize(separator);
    return path;
}

bool FileExists(const std::wstring& path)
{
    const DWORD attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES &&
           (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0;
}

int ShowError(const wchar_t* message)
{
    MessageBoxW(
        nullptr,
        message,
        L"DK Arrow Ez Drawing Tool",
        MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return 1;
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    if (!HasWindowsDesktopRuntime10())
    {
        const int choice = MessageBoxW(
            nullptr,
            L"이 프로그램을 실행하려면 Microsoft .NET 10 Desktop Runtime (x64)이 필요합니다.\n\n"
            L"Microsoft 공식 다운로드 페이지를 여시겠습니까?\n"
            L"설치 후 프로그램을 다시 실행해 주세요.",
            L"필요한 구성 요소가 없습니다",
            MB_YESNO | MB_ICONINFORMATION | MB_SETFOREGROUND);

        if (choice == IDYES)
        {
            ShellExecuteW(
                nullptr,
                L"open",
                kRuntimeDownloadUrl,
                nullptr,
                nullptr,
                SW_SHOWNORMAL);
        }

        return 0;
    }

    const std::wstring launcherDirectory = GetLauncherDirectory();
    if (launcherDirectory.empty())
        return ShowError(L"프로그램 실행 경로를 확인할 수 없습니다.");

    const std::wstring appPath = launcherDirectory + L"\\" + kAppRelativePath;
    const std::wstring appDirectory = launcherDirectory + L"\\app";

    if (!FileExists(appPath))
        return ShowError(L"프로그램 본체 파일이 없습니다. ZIP 파일을 다시 압축 해제해 주세요.");

    const HINSTANCE result = ShellExecuteW(
        nullptr,
        L"open",
        appPath.c_str(),
        nullptr,
        appDirectory.c_str(),
        SW_SHOWNORMAL);

    if (reinterpret_cast<INT_PTR>(result) <= 32)
        return ShowError(L"프로그램 본체를 실행하지 못했습니다.");

    return 0;
}
