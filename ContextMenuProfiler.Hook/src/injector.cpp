#include <windows.h>
#include <tlhelp32.h>
#include <stdio.h>
#include <iostream>
#include <vector>

// Enable Debug Privilege (Required for injecting into system processes)
bool EnableDebugPrivilege()
{
    HANDLE hToken;
    LUID luid;
    TOKEN_PRIVILEGES tkp;

    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &hToken))
    {
        return false;
    }

    if (!LookupPrivilegeValue(NULL, SE_DEBUG_NAME, &luid))
    {
        CloseHandle(hToken);
        return false;
    }

    tkp.PrivilegeCount = 1;
    tkp.Privileges[0].Luid = luid;
    tkp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

    if (!AdjustTokenPrivileges(hToken, false, &tkp, sizeof(tkp), NULL, NULL))
    {
        CloseHandle(hToken);
        return false;
    }

    CloseHandle(hToken);
    return true;
}

static std::vector<DWORD> FindProcessIdsByName(const char* processName)
{
    std::vector<DWORD> processIds;
    HANDLE hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (hSnapshot == INVALID_HANDLE_VALUE)
    {
        return processIds;
    }

    PROCESSENTRY32 pe;
    pe.dwSize = sizeof(PROCESSENTRY32);
    if (Process32First(hSnapshot, &pe))
    {
        do
        {
            if (_stricmp(pe.szExeFile, processName) == 0)
            {
                processIds.push_back(pe.th32ProcessID);
            }
        } while (Process32Next(hSnapshot, &pe));
    }

    CloseHandle(hSnapshot);
    return processIds;
}

static bool InjectDllToProcess(DWORD processId, const char* dllPath)
{
    std::cout << "Target Process ID: " << processId << std::endl;

    HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, processId);
    if (!hProcess)
    {
        std::cerr << "Failed to open process. Error: " << GetLastError() << std::endl;
        return false;
    }

    // Get absolute path
    char absPath[MAX_PATH];
    if (GetFullPathNameA(dllPath, MAX_PATH, absPath, NULL) == 0)
    {
        std::cerr << "Failed to get absolute path." << std::endl;
        CloseHandle(hProcess);
        return false;
    }
    
    std::cout << "Injecting DLL: " << absPath << std::endl;

    LPVOID pRemotePath = VirtualAllocEx(hProcess, NULL, strlen(absPath) + 1, MEM_COMMIT, PAGE_READWRITE);
    if (!pRemotePath)
    {
        std::cerr << "VirtualAllocEx failed. Error: " << GetLastError() << std::endl;
        CloseHandle(hProcess);
        return false;
    }

    if (!WriteProcessMemory(hProcess, pRemotePath, (void*)absPath, strlen(absPath) + 1, NULL))
    {
        std::cerr << "WriteProcessMemory failed. Error: " << GetLastError() << std::endl;
        VirtualFreeEx(hProcess, pRemotePath, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    HANDLE hThread = CreateRemoteThread(hProcess, NULL, 0, (LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleA("kernel32.dll"), "LoadLibraryA"), pRemotePath, 0, NULL);
    if (!hThread)
    {
        std::cerr << "CreateRemoteThread failed. Error: " << GetLastError() << std::endl;
        VirtualFreeEx(hProcess, pRemotePath, 0, MEM_RELEASE);
        CloseHandle(hProcess);
        return false;
    }

    WaitForSingleObject(hThread, INFINITE);
    
    // Check exit code of LoadLibrary
    DWORD exitCode = 0;
    GetExitCodeThread(hThread, &exitCode);
    if (exitCode == 0)
    {
         std::cerr << "LoadLibrary failed in remote process (ExitCode 0). Maybe architecture mismatch or missing dependencies?" << std::endl;
    }
    else
    {
         std::cout << "Remote LoadLibrary success. Handle: 0x" << std::hex << exitCode << std::dec << std::endl;
    }

    VirtualFreeEx(hProcess, pRemotePath, 0, MEM_RELEASE);
    CloseHandle(hThread);
    CloseHandle(hProcess);

    return exitCode != 0;
}

// Helper to inject DLL into all matching processes by name
bool InjectDll(const char* processName, const char* dllPath)
{
    std::vector<DWORD> processIds = FindProcessIdsByName(processName);
    if (processIds.empty())
    {
        std::cerr << "Process not found: " << processName << std::endl;
        return false;
    }

    bool anySuccess = false;
    for (DWORD pid : processIds)
    {
        if (InjectDllToProcess(pid, dllPath))
        {
            anySuccess = true;
        }
    }

    return anySuccess;
}

static bool EjectDllFromProcess(DWORD processId, const char* dllName)
{
    HANDLE hProcess = OpenProcess(PROCESS_ALL_ACCESS, FALSE, processId);
    if (!hProcess)
    {
        std::cerr << "Failed to open process. Error: " << GetLastError() << std::endl;
        return false;
    }

    // Find the module handle in the remote process
    HMODULE hModule = NULL;
    HANDLE hModuleSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);
    if (hModuleSnapshot != INVALID_HANDLE_VALUE)
    {
        MODULEENTRY32 me;
        me.dwSize = sizeof(MODULEENTRY32);
        if (Module32First(hModuleSnapshot, &me))
        {
            do
            {
                if (_stricmp(me.szModule, dllName) == 0)
                {
                    hModule = me.hModule;
                    break;
                }
            } while (Module32Next(hModuleSnapshot, &me));
        }
        CloseHandle(hModuleSnapshot);
    }

    if (!hModule)
    {
        std::cerr << "Module not found in process: " << dllName << std::endl;
        CloseHandle(hProcess);
        return false;
    }

    std::cout << "Ejecting DLL: " << dllName << " (Handle: 0x" << std::hex << hModule << std::dec << ")" << std::endl;

    HANDLE hThread = CreateRemoteThread(hProcess, NULL, 0, (LPTHREAD_START_ROUTINE)GetProcAddress(GetModuleHandleA("kernel32.dll"), "FreeLibrary"), (LPVOID)hModule, 0, NULL);
    if (!hThread)
    {
        std::cerr << "CreateRemoteThread failed. Error: " << GetLastError() << std::endl;
        CloseHandle(hProcess);
        return false;
    }

    WaitForSingleObject(hThread, INFINITE);
    CloseHandle(hThread);
    CloseHandle(hProcess);

    return true;
}

// Helper to eject DLL from all matching processes by name
bool EjectDll(const char* processName, const char* dllName)
{
    std::vector<DWORD> processIds = FindProcessIdsByName(processName);
    if (processIds.empty())
    {
        std::cerr << "Process not found: " << processName << std::endl;
        return false;
    }

    bool anySuccess = false;
    for (DWORD pid : processIds)
    {
        if (EjectDllFromProcess(pid, dllName))
        {
            anySuccess = true;
        }
    }

    return anySuccess;
}

int main(int argc, char* argv[])
{
    if (argc < 2)
    {
        std::cout << "Usage: Injector.exe <path_to_dll> [--eject]" << std::endl;
        return 1;
    }

    bool eject = false;
    const char* dllPath = argv[1];

    if (argc >= 3 && _stricmp(argv[2], "--eject") == 0)
    {
        eject = true;
    }

    if (!EnableDebugPrivilege())
    {
        std::cout << "Warning: Failed to enable SeDebugPrivilege. Operation might fail." << std::endl;
    }

    if (eject)
    {
        // 1. 先尝试通过管道发送 SHUTDOWN 命令，让 DLL 主动拆钩
        HANDLE hPipe = CreateFileA("\\\\.\\pipe\\ContextMenuProfilerHook", GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
        if (hPipe != INVALID_HANDLE_VALUE) {
            DWORD written;
            const char* shutdownCmd = "SHUTDOWN";
            WriteFile(hPipe, shutdownCmd, (DWORD)strlen(shutdownCmd), &written, NULL);
            std::cout << "Sent SHUTDOWN signal to Hook DLL..." << std::endl;
            
            // 给 DLL 一点时间处理拆钩和线程退出
            Sleep(500);
            CloseHandle(hPipe);
            Sleep(200); // 再等一下确保管道彻底关闭
        }

        // Get only filename for ejection
        const char* dllName = strrchr(dllPath, '\\');
        if (dllName) dllName++; else dllName = dllPath;

        std::cout << "Ejecting " << dllName << " from explorer.exe..." << std::endl;
        if (EjectDll("explorer.exe", dllName))
        {
            std::cout << "Ejection sequence completed." << std::endl;
        }
        else
        {
            std::cout << "Ejection FAILED." << std::endl;
            return 1;
        }
    }
    else
    {
        std::cout << "Injecting " << dllPath << " into explorer.exe..." << std::endl;
        if (InjectDll("explorer.exe", dllPath))
        {
            std::cout << "Injection sequence completed." << std::endl;
        }
        else
        {
            std::cout << "Injection FAILED." << std::endl;
            return 1;
        }
    }
    
    return 0;
}
