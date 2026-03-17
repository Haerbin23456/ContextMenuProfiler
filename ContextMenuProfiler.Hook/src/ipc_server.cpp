#include "../include/common.h"
#include "../include/MinHook.h"
#include <stdio.h>

static const LONG kMaxConcurrentPipeClients = 4;
static volatile LONG g_ActivePipeClients = 0;
static const DWORD kWorkerTimeoutMs = 1800;
static const size_t kMaxRequestBytes = 16384;

struct IpcWorkItem {
    char request[2048];
    char response[65536];
    int maxLen;
    volatile LONG releaseByWorker;
};

void DoIpcWorkInternal(const char* request, char* response, int maxLen) {
    std::string reqStr = request;
    
    // 增加关停指令处理
    if (reqStr == "SHUTDOWN") {
        g_ShouldExit = true;
        // 立即禁用所有钩子，防止新的调用进入
        MH_DisableHook(MH_ALL_HOOKS);
        snprintf(response, maxLen, "{\"success\":true,\"message\":\"Hooks disabled, shutting down...\"}");
        return;
    }

    std::string mode = "AUTO";
    std::string clsidStr, pathStr, dllHintStr;
    bool v1Protocol = false;

    if (reqStr.rfind("CMP1|", 0) == 0) {
        v1Protocol = true;
        reqStr = reqStr.substr(5);
        size_t sep0 = reqStr.find('|');
        if (sep0 == std::string::npos) {
            snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_FORMAT\",\"error\":\"Missing Mode\"}");
            return;
        }
        mode = reqStr.substr(0, sep0);
        reqStr = reqStr.substr(sep0 + 1);
    }

    if (!v1Protocol && reqStr.substr(0, 4) == "COM|") {
        mode = "COM";
        reqStr = reqStr.substr(4);
    } else if (!v1Protocol && reqStr.substr(0, 5) == "ECMD|") {
        mode = "ECMD";
        reqStr = reqStr.substr(5);
    }

    // Format: CLSID|Path[|DllHint]
    size_t sep1 = reqStr.find('|');
    if (sep1 == std::string::npos) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_FORMAT\",\"error\":\"Format Error\"}");
        return;
    }
    clsidStr = reqStr.substr(0, sep1);
    
    std::string remaining = reqStr.substr(sep1 + 1);
    size_t sep2 = remaining.find('|');
    if (sep2 != std::string::npos) {
        pathStr = remaining.substr(0, sep2);
        dllHintStr = remaining.substr(sep2 + 1);
    } else {
        pathStr = remaining;
    }

    CLSID clsid;
    wchar_t wClsid[64];
    if (MultiByteToWideChar(CP_UTF8, 0, clsidStr.c_str(), -1, wClsid, 64) <= 0) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_CLSID_UTF8\",\"error\":\"Bad CLSID Encoding\"}");
        return;
    }
    if (FAILED(CLSIDFromString(wClsid, &clsid))) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_CLSID\",\"error\":\"Bad CLSID\"}");
        return;
    }

    wchar_t wPath[MAX_PATH];
    if (MultiByteToWideChar(CP_UTF8, 0, pathStr.c_str(), -1, wPath, MAX_PATH) <= 0) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_PATH_UTF8\",\"error\":\"Bad Path Encoding\"}");
        return;
    }

    wchar_t wDllHint[MAX_PATH] = { 0 };
    if (!dllHintStr.empty()) {
        if (MultiByteToWideChar(CP_UTF8, 0, dllHintStr.c_str(), -1, wDllHint, MAX_PATH) <= 0) {
            snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_DLLHINT_UTF8\",\"error\":\"Bad DllHint Encoding\"}");
            return;
        }
    }

    // We'll pass the dllHint to the handlers
    const wchar_t* pDllHint = dllHintStr.empty() ? NULL : wDllHint;

    if (mode == "COM") {
        QueryComExtension(clsid, wPath, response, maxLen, pDllHint);
    } else if (mode == "ECMD") {
        QueryExplorerCommand(clsid, wPath, response, maxLen, pDllHint);
    } else {
        IExplorerCommand* pTest = NULL;
        HRESULT hr = fpCoCreateInstance(clsid, NULL, CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER, __uuidof(IExplorerCommand), (void**)&pTest);
        if (SUCCEEDED(hr) && pTest) {
            pTest->Release();
            QueryExplorerCommand(clsid, wPath, response, maxLen, pDllHint);
        } else {
            QueryComExtension(clsid, wPath, response, maxLen, pDllHint);
        }
    }
}

void DoIpcWork(const char* request, char* response, int maxLen) {
    __try {
        DoIpcWorkInternal(request, response, maxLen);
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        snprintf(response, maxLen, "{\"success\":false,\"error\":\"Crash in Hook\"}");
    }
}

DWORD WINAPI DoIpcWorkThread(LPVOID param) {
    IpcWorkItem* item = (IpcWorkItem*)param;
    item->response[0] = '\0';
    DoIpcWork(item->request, item->response, item->maxLen);
    if (InterlockedCompareExchange(&item->releaseByWorker, 0, 0) == 1) {
        delete item;
    }
    return 0;
}

DWORD WINAPI HandlePipeClientThread(LPVOID param) {
    HANDLE hPipe = (HANDLE)param;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

    std::string req;
    req.reserve(2048);
    DWORD read = 0;
    DWORD written = 0;

    while (true) {
        char chunk[2048];
        BOOL ok = ReadFile(hPipe, chunk, sizeof(chunk), &read, NULL);
        if (ok && read > 0) {
            req.append(chunk, read);
            if (req.size() > kMaxRequestBytes) {
                const char* errRes = "{\"success\":false,\"code\":\"E_REQ_TOO_LARGE\",\"error\":\"Request Too Large\"}";
                WriteFile(hPipe, errRes, (DWORD)strlen(errRes), &written, NULL);
                FlushFileBuffers(hPipe);
                req.clear();
                break;
            }
            break;
        }
        DWORD err = GetLastError();
        if (!ok && err == ERROR_MORE_DATA) {
            if (read > 0) {
                req.append(chunk, read);
            }
            if (req.size() > kMaxRequestBytes) {
                const char* errRes = "{\"success\":false,\"code\":\"E_REQ_TOO_LARGE\",\"error\":\"Request Too Large\"}";
                WriteFile(hPipe, errRes, (DWORD)strlen(errRes), &written, NULL);
                FlushFileBuffers(hPipe);
                req.clear();
                break;
            }
            continue;
        }
        break;
    }

    if (!req.empty()) {
        req.push_back('\0');
        IpcWorkItem* workItem = new IpcWorkItem();
        strncpy_s(workItem->request, sizeof(workItem->request), req.c_str(), _TRUNCATE);
        workItem->response[0] = '\0';
        workItem->maxLen = 65535;
        workItem->releaseByWorker = 0;

        HANDLE hWorkThread = CreateThread(NULL, 0, DoIpcWorkThread, workItem, 0, NULL);
        if (!hWorkThread) {
            const char* errRes = "{\"success\":false,\"error\":\"Hook Worker Launch Failed\"}";
            WriteFile(hPipe, errRes, (DWORD)strlen(errRes), &written, NULL);
            FlushFileBuffers(hPipe);
            delete workItem;
        } else {
            DWORD waitRc = WaitForSingleObject(hWorkThread, kWorkerTimeoutMs);
            if (waitRc == WAIT_OBJECT_0) {
                int resLen = (int)strlen(workItem->response);
                if (resLen > 0) {
                    WriteFile(hPipe, workItem->response, (DWORD)resLen, &written, NULL);
                    FlushFileBuffers(hPipe);
                }
                delete workItem;
            } else {
                const char* timeoutRes = "{\"success\":false,\"error\":\"Hook Worker Timeout\"}";
                WriteFile(hPipe, timeoutRes, (DWORD)strlen(timeoutRes), &written, NULL);
                FlushFileBuffers(hPipe);
                InterlockedExchange(&workItem->releaseByWorker, 1);
            }
            CloseHandle(hWorkThread);
        }
    }

    DisconnectNamedPipe(hPipe);
    CloseHandle(hPipe);
    InterlockedDecrement(&g_ActivePipeClients);
    CoUninitialize();
    return 0;
}

DWORD WINAPI PipeThread(LPVOID) {
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    
    Gdiplus::GdiplusStartupInput gsi;
    Gdiplus::GdiplusStartup(&g_GdiplusToken, &gsi, NULL);

    PSECURITY_DESCRIPTOR pSD = (PSECURITY_DESCRIPTOR)LocalAlloc(LPTR, SECURITY_DESCRIPTOR_MIN_LENGTH);
    InitializeSecurityDescriptor(pSD, SECURITY_DESCRIPTOR_REVISION);
    SetSecurityDescriptorDacl(pSD, TRUE, NULL, FALSE);
    SECURITY_ATTRIBUTES sa = { sizeof(sa), pSD, FALSE };

    while (!g_ShouldExit) {
        HANDLE hPipe = CreateNamedPipeA(
            "\\\\.\\pipe\\ContextMenuProfilerHook",
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES, 65536, 65536, 0, &sa);
        if (hPipe == INVALID_HANDLE_VALUE) { Sleep(100); continue; }
        
        if (ConnectNamedPipe(hPipe, NULL) || GetLastError() == ERROR_PIPE_CONNECTED) {
            if (g_ShouldExit) {
                CloseHandle(hPipe);
                break;
            }
            LONG active = InterlockedIncrement(&g_ActivePipeClients);
            if (active > kMaxConcurrentPipeClients) {
                InterlockedDecrement(&g_ActivePipeClients);
                const char* busyRes = "{\"success\":false,\"error\":\"Hook Busy\"}";
                DWORD written = 0;
                WriteFile(hPipe, busyRes, (DWORD)strlen(busyRes), &written, NULL);
                FlushFileBuffers(hPipe);
                DisconnectNamedPipe(hPipe);
                CloseHandle(hPipe);
                continue;
            }

            HANDLE hClientThread = CreateThread(NULL, 0, HandlePipeClientThread, hPipe, 0, NULL);
            if (hClientThread) {
                CloseHandle(hClientThread);
                continue;
            }
            InterlockedDecrement(&g_ActivePipeClients);
        }
        DisconnectNamedPipe(hPipe);
        CloseHandle(hPipe);
    }

    Gdiplus::GdiplusShutdown(g_GdiplusToken);
    MH_Uninitialize();
    LocalFree(pSD);
    CoUninitialize();
    LogToFile(L"--- Pipe Thread Exited Cleanly ---\n");
    return 0;
}
