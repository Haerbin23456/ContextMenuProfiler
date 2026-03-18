#include "../include/common.h"
#include "../include/MinHook.h"
#include <stdio.h>

static const LONG kMaxConcurrentPipeClients = 4;
static volatile LONG g_ActivePipeClients = 0;
static const DWORD kWorkerTimeoutMs = 1800;
static const DWORD kFrameHeaderBytes = 4;
static const size_t kMaxRequestBytes = 16384;
static const size_t kMaxResponseBytes = 65536;

struct IpcWorkItem {
    std::string request;
    char response[65536];
    int maxLen;
    volatile LONG releaseByWorker;
};

static bool ReadExactFromPipe(HANDLE hPipe, void* buffer, DWORD bytesToRead) {
    BYTE* cursor = reinterpret_cast<BYTE*>(buffer);
    DWORD totalRead = 0;
    while (totalRead < bytesToRead) {
        DWORD chunkRead = 0;
        BOOL ok = ReadFile(hPipe, cursor + totalRead, bytesToRead - totalRead, &chunkRead, NULL);
        if (!ok || chunkRead == 0) {
            return false;
        }

        totalRead += chunkRead;
    }

    return true;
}

static bool WriteFrameToPipe(HANDLE hPipe, const char* payload, DWORD payloadLength) {
    if (payloadLength > static_cast<DWORD>(kMaxResponseBytes)) {
        return false;
    }

    DWORD written = 0;
    DWORD frameLength = payloadLength;
    if (!WriteFile(hPipe, &frameLength, kFrameHeaderBytes, &written, NULL) || written != kFrameHeaderBytes) {
        return false;
    }

    if (payloadLength == 0) {
        FlushFileBuffers(hPipe);
        return true;
    }

    if (!WriteFile(hPipe, payload, payloadLength, &written, NULL) || written != payloadLength) {
        return false;
    }

    FlushFileBuffers(hPipe);
    return true;
}

static void WriteJsonFrameToPipe(HANDLE hPipe, const char* json) {
    WriteFrameToPipe(hPipe, json, static_cast<DWORD>(strlen(json)));
}

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

    std::string mode;
    std::string clsidStr, pathStr, dllHintStr;

    if (reqStr.rfind("CMP1|", 0) != 0) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_PROTOCOL\",\"error\":\"Unsupported Protocol\"}");
        return;
    }

    reqStr = reqStr.substr(5);
    size_t sep0 = reqStr.find('|');
    if (sep0 == std::string::npos) {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_FORMAT\",\"error\":\"Missing Mode\"}");
        return;
    }

    mode = reqStr.substr(0, sep0);
    reqStr = reqStr.substr(sep0 + 1);

    if (mode != "AUTO" && mode != "COM" && mode != "ECMD") {
        snprintf(response, maxLen, "{\"success\":false,\"code\":\"E_MODE\",\"error\":\"Unsupported Mode\"}");
        return;
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
    DoIpcWork(item->request.c_str(), item->response, item->maxLen);
    if (InterlockedCompareExchange(&item->releaseByWorker, 0, 0) == 1) {
        delete item;
    }
    return 0;
}

DWORD WINAPI HandlePipeClientThread(LPVOID param) {
    HANDLE hPipe = (HANDLE)param;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

    DWORD requestLength = 0;
    if (ReadExactFromPipe(hPipe, &requestLength, kFrameHeaderBytes)) {
        if (requestLength == 0 || requestLength > static_cast<DWORD>(kMaxRequestBytes)) {
            WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"code\":\"E_REQ_TOO_LARGE\",\"error\":\"Request Too Large\"}");
        } else {
            std::string req(requestLength, '\0');
            if (!ReadExactFromPipe(hPipe, &req[0], requestLength)) {
                WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"code\":\"E_REQ_READ\",\"error\":\"Request Read Failed\"}");
            } else {
        IpcWorkItem* workItem = new IpcWorkItem();
                workItem->request = std::move(req);
        workItem->response[0] = '\0';
        workItem->maxLen = static_cast<int>(kMaxResponseBytes) - 1;
        workItem->releaseByWorker = 0;

        HANDLE hWorkThread = CreateThread(NULL, 0, DoIpcWorkThread, workItem, 0, NULL);
        if (!hWorkThread) {
                    WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"error\":\"Hook Worker Launch Failed\"}");
            delete workItem;
        } else {
            DWORD waitRc = WaitForSingleObject(hWorkThread, kWorkerTimeoutMs);
            if (waitRc == WAIT_OBJECT_0) {
                DWORD resLen = static_cast<DWORD>(strnlen_s(workItem->response, workItem->maxLen));
                if (resLen > 0) {
                            WriteFrameToPipe(hPipe, workItem->response, resLen);
                        } else {
                            WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"error\":\"Empty Hook Response\"}");
                }
                delete workItem;
            } else {
                        WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"error\":\"Hook Worker Timeout\"}");
                InterlockedExchange(&workItem->releaseByWorker, 1);
            }
            CloseHandle(hWorkThread);
        }
            }
        }
    } else {
        WriteJsonFrameToPipe(hPipe, "{\"success\":false,\"code\":\"E_REQ_HEADER\",\"error\":\"Request Header Read Failed\"}");
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

    while (!g_ShouldExit) {
        HANDLE hPipe = CreateNamedPipeA(
            "\\\\.\\pipe\\ContextMenuProfilerHook",
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES, 65536, 65536, 0, NULL);
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
                WriteJsonFrameToPipe(hPipe, busyRes);
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
    CoUninitialize();
    LogToFile(L"--- Pipe Thread Exited Cleanly ---\n");
    return 0;
}
