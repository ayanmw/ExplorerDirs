#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#if defined(EXPLORERDIRS_NATIVEPROBE)
#define ED_PROBE_EXPORT __declspec(dllexport)
#define ED_PROBE_METRIC_INCREMENT(counter) InterlockedIncrement(&(counter))
#define ED_PROBE_METRIC_ADD(counter, value) InterlockedAdd(&(counter), (value))
#define ED_PROBE_METRIC_ADD64(counter, value) InterlockedAdd64(&(counter), (value))
#else
#define ED_PROBE_EXPORT
#define ED_PROBE_METRIC_INCREMENT(counter) ((void)0)
#define ED_PROBE_METRIC_ADD(counter, value) ((void)sizeof(value))
#define ED_PROBE_METRIC_ADD64(counter, value) ((void)sizeof(value))
#endif
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <commctrl.h>
#include <propsys.h>
#include <propkey.h>
#include <strsafe.h>
#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <functional>
#include <memory>
#include <map>
#include <new>
#include <mutex>
#include <queue>
#include <set>
#include <string>
#include <thread>
#include <vector>
#include <cstdio>
#include <iomanip>
#include <sstream>

extern "C" ED_PROBE_EXPORT void WINAPI ResetSyntheticSortCounters();

#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
static HRESULT CreateSizeSortPolicyPidl(PCIDLIST_ABSOLUTE source, DWORD policy, PIDLIST_ABSOLUTE* output);
#endif

using FolderSizeProviderCallback = HRESULT(WINAPI*)(PCWSTR, volatile LONG*, ULONGLONG*, ULONGLONG*, ULONGLONG*, ULONGLONG*, DWORD*);
using FolderSizeInvalidationCallback = void(WINAPI*)(PCWSTR);
static PVOID volatile g_folderSizeProviderCallback = nullptr;
static PVOID volatile g_folderSizeInvalidationCallback = nullptr;
static SRWLOCK g_folderSizeProviderLock = SRWLOCK_INIT;
static volatile LONG g_folderSizeEnabled = 1;
static volatile LONG g_folderSizeGeneration = 0;

static bool FolderSizeScanCancelled(volatile LONG* flag, LONG expectedGeneration = -1)
{
    return !InterlockedCompareExchange(&g_folderSizeEnabled, 0, 0) ||
        (expectedGeneration != -1 && InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0) != expectedGeneration) ||
        (flag && InterlockedCompareExchange(flag, 0, 0));
}

static void InvalidateManagedFolderSize(PCWSTR path)
{
    AcquireSRWLockShared(&g_folderSizeProviderLock);
    const auto callback = reinterpret_cast<FolderSizeInvalidationCallback>(
        InterlockedCompareExchangePointer(&g_folderSizeInvalidationCallback, nullptr, nullptr));
    if (callback && path) callback(path);
    ReleaseSRWLockShared(&g_folderSizeProviderLock);
}

namespace
{
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
    std::atomic<ULONG_PTR> g_sizeDisplayCookie{0};
#endif
#if defined(EXPLORERDIRS_SIZE_TRACE_EXPERIMENT)
#define ED_SIZE_TRACE TraceSizeRequest
    void TraceSizeRequest(const char* stage, const void* instance, PCWSTR name, DWORD code,
        DWORD attributes, ULONGLONG size)
    {
        // Opt-in isolated-build evidence only. Bound synchronous diagnostic IO and never
        // log full browsing paths; normal product builds compile this body out.
        static volatile LONG entries = 0;
        if (InterlockedIncrement(&entries) > 4096) return;
        HMODULE module = nullptr;
        WCHAR path[2048]{};
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&TraceSizeRequest), &module)) return;
        const auto length = GetModuleFileNameW(module, path, ARRAYSIZE(path));
        if (!length || length >= ARRAYSIZE(path) || FAILED(StringCchCatW(path, ARRAYSIZE(path), L".size-trace.log"))) return;
        char item[1024]{};
        if (name) WideCharToMultiByte(CP_UTF8, 0, name, -1, item, ARRAYSIZE(item), nullptr, nullptr);
        char line[1400]{};
        const auto count = sprintf_s(line, "%llu tid=%lu self=%p %s code=%08lX attrs=%08lX bytes=%llu item=%s\r\n",
            GetTickCount64(), GetCurrentThreadId(), instance, stage, code, attributes, size, item);
        if (count <= 0) return;
        const auto file = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return;
        DWORD written = 0; WriteFile(file, line, static_cast<DWORD>(count), &written, nullptr); CloseHandle(file);
    }
    BOOL CALLBACK TraceSizeViewChild(HWND window, LPARAM instance)
    {
        WCHAR className[128]{};
        if (GetClassNameW(window, className, ARRAYSIZE(className)))
            TraceSizeRequest("view-control", reinterpret_cast<const void*>(instance), className, 0, 0, 0);
        return TRUE;
    }
#else
#define ED_SIZE_TRACE(...) ((void)0)
#endif
    void TraceSyntheticSortStage(const char* stage)
    {
#if defined(EXPLORERDIRS_NATIVEPROBE)
        const auto output = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!output || output == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(output, stage, static_cast<DWORD>(strlen(stage)), &written, nullptr);
        WriteFile(output, "\r\n", 2, &written, nullptr);
#else
        (void)stage;
#endif
    }

    void TraceSyntheticFolderStage(const char* stage, const std::wstring& backingPath, HRESULT result)
    {
        char pathUtf8[1024]{};
        if (!backingPath.empty())
            WideCharToMultiByte(CP_UTF8, 0, backingPath.c_str(), -1, pathUtf8,
                static_cast<int>(ARRAYSIZE(pathUtf8)), nullptr, nullptr);
        char message[1400]{};
        sprintf_s(message, "synthetic-sort: %s; hr=0x%08X; backing-path=%s",
            stage, static_cast<unsigned>(result), pathUtf8);
        TraceSyntheticSortStage(message);
    }

    void TraceSyntheticFolderAttributes(const std::wstring& backingPath, SFGAOF requested, SFGAOF returned, HRESULT result)
    {
        char pathUtf8[1024]{};
        if (!backingPath.empty())
            WideCharToMultiByte(CP_UTF8, 0, backingPath.c_str(), -1, pathUtf8,
                static_cast<int>(ARRAYSIZE(pathUtf8)), nullptr, nullptr);
        char message[1400]{};
        sprintf_s(message, "synthetic-sort: GetAttributesOf-nested; hr=0x%08X; requested=0x%08X; returned=0x%08X; backing-path=%s",
            static_cast<unsigned>(result), static_cast<unsigned>(requested), static_cast<unsigned>(returned), pathUtf8);
        TraceSyntheticSortStage(message);
    }

    void TraceSyntheticNotificationPidl(LONG event, unsigned index, PCIDLIST_ABSOLUTE pidl)
    {
        if (!pidl)
        {
            char message[128]{};
            sprintf_s(message, "synthetic-sort: SFVM_FSNOTIFY pidl[%u]=null; event=0x%08X", index, static_cast<unsigned>(event));
            TraceSyntheticSortStage(message);
            return;
        }
        LPWSTR parsingName = nullptr;
        LPWSTR fileSystemPath = nullptr;
        const auto parsingResult = SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, &parsingName);
        const auto fileSystemResult = SHGetNameFromIDList(pidl, SIGDN_FILESYSPATH, &fileSystemPath);
        char parsingUtf8[900]{};
        char fileSystemUtf8[900]{};
        if (SUCCEEDED(parsingResult) && parsingName)
            WideCharToMultiByte(CP_UTF8, 0, parsingName, -1, parsingUtf8, static_cast<int>(ARRAYSIZE(parsingUtf8)), nullptr, nullptr);
        if (SUCCEEDED(fileSystemResult) && fileSystemPath)
            WideCharToMultiByte(CP_UTF8, 0, fileSystemPath, -1, fileSystemUtf8, static_cast<int>(ARRAYSIZE(fileSystemUtf8)), nullptr, nullptr);
        char message[2048]{};
        sprintf_s(message,
            "synthetic-sort: SFVM_FSNOTIFY pidl[%u]; event=0x%08X; parsing-hr=0x%08X; parsing=%s; filesystem-hr=0x%08X; filesystem=%s",
            index, static_cast<unsigned>(event), static_cast<unsigned>(parsingResult), parsingUtf8,
            static_cast<unsigned>(fileSystemResult), fileSystemUtf8);
        TraceSyntheticSortStage(message);
        if (parsingName) CoTaskMemFree(parsingName);
        if (fileSystemPath) CoTaskMemFree(fileSystemPath);
    }

    struct SyntheticListViewSearch
    {
        HWND window = nullptr;
    };

    BOOL CALLBACK FindSyntheticListView(HWND window, LPARAM parameter)
    {
        wchar_t className[64]{};
        if (GetClassNameW(window, className, ARRAYSIZE(className)) > 0 &&
            _wcsicmp(className, WC_LISTVIEWW) == 0)
        {
            static_cast<SyntheticListViewSearch*>(reinterpret_cast<void*>(parameter))->window = window;
            return FALSE;
        }
        return TRUE;
    }

    HWND GetSyntheticListView(HWND viewWindow)
    {
        SyntheticListViewSearch search{};
        if (viewWindow) EnumChildWindows(viewWindow, FindSyntheticListView, reinterpret_cast<LPARAM>(&search));
        return search.window;
    }

    volatile LONG g_syntheticLiveObjects = 0;
    volatile LONG g_syntheticServerLocks = 0;
    volatile LONG g_syntheticDeferredRefreshCount = 0;
    volatile LONG g_syntheticChildNotificationTranslationQueued = 0;
    volatile LONG g_syntheticChildNotificationTranslationSent = 0;
    volatile LONG g_syntheticTranslateChildNotificationsForProbe = 0;
    volatile LONG g_syntheticAttributeNotificationTranslationCount = 0;
    // Four independent 1,025-row product panes need 4,100 snapshots; keep a measured, bounded margin above that working set.
    volatile LONG g_syntheticRecursiveSizeCacheCapacityForProbe = 8192;
    bool ShouldTranslateSyntheticChildNotification(LONG event)
    {
        // UPDATEITEM carries property changes for an existing child; the custom
        // row model must re-enumerate its parent to observe updated derived values.
        // ATTRIBUTES can also change row presentation (for example, read-only rules).
        // Shell uses separate lifecycle events for folders and nonfolder items.
        const auto eventType = event & ~SHCNE_INTERRUPT;
        return eventType == SHCNE_CREATE || eventType == SHCNE_RENAMEITEM ||
            eventType == SHCNE_DELETE || eventType == SHCNE_UPDATEITEM ||
            eventType == SHCNE_MKDIR || eventType == SHCNE_RMDIR ||
            eventType == SHCNE_RENAMEFOLDER || eventType == SHCNE_ATTRIBUTES;
    }
    // The navigation prototype creates one held folder/view pair per visited Shell view; keep enough slots for multi-step history probes and peer browsers.
    constexpr size_t kSyntheticSortViewHoldCount = 32;
    PVOID volatile g_syntheticSortViewHolds[kSyntheticSortViewHoldCount]{};
    CLSID g_syntheticFolderClassId =
    { 0xA17D2A61, 0x4C0B, 0x4B22, { 0x8E, 0x36, 0x35, 0xD9, 0x2C, 0xBC, 0x11, 0x70 } };
    struct SyntheticRootClassIdLess
    {
        bool operator()(const CLSID& left, const CLSID& right) const
        {
            return memcmp(&left, &right, sizeof(CLSID)) < 0;
        }
    };
    std::mutex g_syntheticRootConfigurationMutex;
    std::map<CLSID, std::wstring, SyntheticRootClassIdLess> g_syntheticRootBackingPaths;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
    std::mutex g_productCompareDiagnosticMutex;
    std::vector<std::wstring> g_productCompareDiagnostics;
    volatile LONG g_productCompareTraceEnabled = 1;
    volatile LONG g_productSyntheticEnumStarted = 0;
    volatile LONG g_productSyntheticEnumCompleted = 0;
    volatile LONG64 g_productSyntheticEnumItemCount = 0;
    volatile LONG64 g_productSyntheticEnumTotalMilliseconds = 0;
    volatile LONG64 g_productSyntheticEnumMaximumMilliseconds = 0;
    volatile LONG64 g_productRecursiveCompareCount = 0;
    volatile LONG64 g_productRecursiveCompareTimingSamples = 0;
    volatile LONG64 g_productRecursiveCompareSampledTicks = 0;
    volatile LONG64 g_productRecursiveCompareMaximumSampleTicks = 0;
    volatile LONG64 g_productRecursiveCompareFastPathHits = 0;
    volatile LONG64 g_productRecursiveCompareFastPathMisses = 0;

    void RecordProductSyntheticEnum(ULONGLONG startMilliseconds, size_t itemCount)
    {
        const auto elapsed = GetTickCount64() - startMilliseconds;
        InterlockedIncrement(&g_productSyntheticEnumCompleted);
        InterlockedAdd64(&g_productSyntheticEnumItemCount, static_cast<LONG64>(itemCount));
        InterlockedAdd64(&g_productSyntheticEnumTotalMilliseconds, static_cast<LONG64>(elapsed));
        auto maximum = InterlockedCompareExchange64(&g_productSyntheticEnumMaximumMilliseconds, 0, 0);
        while (maximum < static_cast<LONG64>(elapsed))
        {
            const auto previous = InterlockedCompareExchange64(&g_productSyntheticEnumMaximumMilliseconds,
                static_cast<LONG64>(elapsed), maximum);
            if (previous == maximum) break;
            maximum = previous;
        }
    }

    void RecordProductCompareDiagnostic(LPARAM column, PCWSTR leftName, ULONGLONG leftSize,
        PCWSTR rightName, ULONGLONG rightSize, short comparison, bool categoryOnly = false, const void* instance = nullptr, int encodedPolicy = -1)
    {
        if (!InterlockedCompareExchange(&g_productCompareTraceEnabled, 0, 0)) return;
        std::lock_guard<std::mutex> lock(g_productCompareDiagnosticMutex);
        if (g_productCompareDiagnostics.size() >= 512) return;
        g_productCompareDiagnostics.push_back(L"column=" + std::to_wstring(LOWORD(column)) +
            L";left=" + (leftName ? leftName : L"<null>") + L";left-bytes=" + std::to_wstring(leftSize) +
            L";right=" + (rightName ? rightName : L"<null>") + L";right-bytes=" + std::to_wstring(rightSize) +
            L";result=" + std::to_wstring(comparison) + L";category-only=" + std::to_wstring(categoryOnly) + L";instance=" + std::to_wstring(reinterpret_cast<UINT_PTR>(instance)) + L";encoded-policy=" + std::to_wstring(encodedPolicy));
    }
#endif
    // This adapter is loaded only by the opt-in NativeProbe. It forwards normal
    // filesystem PIDLs and Shell operations while adding deterministic test columns.
    constexpr PROPERTYKEY kStaticSizeKey =
    { { 0xA38B883C, 0xFA8A, 0x4E1C, { 0x8B, 0x52, 0xC6, 0x05, 0xE5, 0xD0, 0x12, 0x34 } }, 1 };
    constexpr PROPERTYKEY kSizeKey =
    { { 0xB725F130, 0x47EF, 0x101A, { 0xA5, 0xF1, 0x02, 0x60, 0x8C, 0x9E, 0xEB, 0xAC } }, 12 };
    constexpr PROPERTYKEY kDateCreatedKey =
    { { 0xB725F130, 0x47EF, 0x101A, { 0xA5, 0xF1, 0x02, 0x60, 0x8C, 0x9E, 0xEB, 0xAC } }, 15 };
    constexpr PROPERTYKEY kDocumentByteCountKey =
    { { 0xD5CDD502, 0x2E9C, 0x101B, { 0x93, 0x97, 0x08, 0x00, 0x2B, 0x2C, 0xF9, 0xAE } }, 4 };
    constexpr UINT kColumnLimit = 64;
    constexpr DWORD kWrappedPidlMagic = 0x4C445045; // "EPDL"; diagnostic adapter identity, not a persisted format.
    constexpr UINT kWrappedPidlHeaderSize = sizeof(USHORT) + sizeof(DWORD) + sizeof(UINT);
    volatile LONG g_staticColumnComparisons = 0;
    volatile LONG g_allComparisons = 0;
    volatile LONG g_lastCompareColumn = -1;
    volatile LONG g_customDetailsEx = 0;
    volatile LONG g_standardSizeDetailsEx = 0;
    volatile LONG g_standardSizeComparisons = 0;
    volatile LONG g_standardSizeDetailsOf = 0;
    volatile LONG g_customDetailsOf = 0;
    volatile LONG g_customItemDetailsOf = 0;
    volatile LONG g_customHeaderDetailsOf = 0;
    volatile LONG g_documentByteCountComparisons = 0;
    volatile LONG g_documentByteCountDetailsEx = 0;
    volatile LONG g_documentByteCountDetailsOf = 0;
    volatile LONG g_mapColumnToScidRequests = 0;
    volatile LONG g_defaultColumnRequests = 0;
    volatile LONG g_defaultColumnSortProbeEnabled = 0;
    volatile LONG g_propertyStoreQueryInterfaceRequests = 0;
    volatile LONG g_persistClassIdRequests = 0;
    volatile LONG g_persistRootRequests = 0;
    volatile LONG g_propertyStoreBindToObjectRequests = 0;
    volatile LONG g_propertyStoreBindToStorageRequests = 0;
    volatile LONG g_propertyStoreUiObjectRequests = 0;
    volatile LONG g_legacyDetailsCallbackRequests = 0;
    volatile LONG g_legacyDetailsCallbackHeaderRequests = 0;
    volatile LONG g_legacyDetailsCallbackItemRequests = 0;
    volatile LONG g_legacyDetailsCallbackLastColumn = -1;
    volatile LONG g_legacyDetailsCallbackEnabled = 0;
    volatile LONG g_sortCallbackProbeEnabled = 0;
    volatile LONG g_sortCallbackColumnClickRequests = 0;
    volatile LONG g_sortCallbackLastColumn = -1;
    volatile LONG g_sortCallbackGetDefaultsRequests = 0;
    volatile LONG g_propertyStoreFactoryBindRequests = 0;
    volatile LONG g_propertyStoreFactoryCreateRequests = 0;
    volatile LONG g_propertyStoreValueRequests = 0;
    volatile LONG g_propertyStoreFactoryProbeEnabled = 0;
    volatile LONG g_failNextWrappedEnumeratorCacheInsert = 0;

    // Diagnostic-only property path: DefView ignored GetDetailsEx for extra
    // columns, so this probe can expose a known-schema test value through the
    // documented Shell folder property-store factory binding path.
    class StaticByteCountPropertyStore final : public IPropertyStore
    {
    public:
        StaticByteCountPropertyStore(LONG byteCount, ULONGLONG size) : _byteCount(byteCount), _size(size) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IPropertyStore) return E_NOINTERFACE;
            *value = static_cast<IPropertyStore*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT STDMETHODCALLTYPE GetCount(DWORD* count) override
        {
            if (!count) return E_POINTER;
            *count = 2;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetAt(DWORD index, PROPERTYKEY* key) override
        {
            if (!key) return E_POINTER;
            if (index == 0) *key = kSizeKey;
            else if (index == 1) *key = kDocumentByteCountKey;
            else return E_INVALIDARG;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetValue(REFPROPERTYKEY key, PROPVARIANT* value) override
        {
            ED_PROBE_METRIC_INCREMENT(g_propertyStoreValueRequests);
            if (!value) return E_POINTER;
            PropVariantInit(value);
            if (key == kSizeKey)
            {
                value->vt = VT_UI8;
                value->uhVal.QuadPart = _size;
                return S_OK;
            }
            if (key != kDocumentByteCountKey) return S_FALSE;
            value->vt = VT_I4;
            value->lVal = _byteCount;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE SetValue(REFPROPERTYKEY, REFPROPVARIANT) override { return STG_E_ACCESSDENIED; }
        HRESULT STDMETHODCALLTYPE Commit() override { return S_OK; }
    private:
        volatile LONG _references = 1;
        LONG _byteCount;
        ULONGLONG _size;
    };

    class StaticByteCountPropertyStoreFactory final : public IPropertyStoreFactory
    {
    public:
        StaticByteCountPropertyStoreFactory(LONG byteCount, ULONGLONG size) : _byteCount(byteCount), _size(size) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IPropertyStoreFactory) return E_NOINTERFACE;
            *value = static_cast<IPropertyStoreFactory*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT STDMETHODCALLTYPE GetPropertyStore(GETPROPERTYSTOREFLAGS, IUnknown*, REFIID iid, void** value) override
        {
            ED_PROBE_METRIC_INCREMENT(g_propertyStoreFactoryCreateRequests);
            return CreateStore(_byteCount, _size, iid, value);
        }
        HRESULT STDMETHODCALLTYPE GetPropertyStoreForKeys(const PROPERTYKEY*, UINT, GETPROPERTYSTOREFLAGS, REFIID iid, void** value) override
        {
            ED_PROBE_METRIC_INCREMENT(g_propertyStoreFactoryCreateRequests);
            return CreateStore(_byteCount, _size, iid, value);
        }
    private:
        static HRESULT CreateStore(LONG byteCount, ULONGLONG size, REFIID iid, void** value)
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            auto* store = new (std::nothrow) StaticByteCountPropertyStore(byteCount, size);
            if (!store) return E_OUTOFMEMORY;
            const auto result = store->QueryInterface(iid, value);
            store->Release();
            return result;
        }
        volatile LONG _references = 1;
        LONG _byteCount;
        ULONGLONG _size;
    };

    class LegacyDetailsCallback final : public IShellFolderViewCB
    {
    public:
        explicit LegacyDetailsCallback(UINT sortColumn) : _sortColumn(sortColumn) {}
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IShellFolderViewCB) return E_NOINTERFACE;
            *value = static_cast<IShellFolderViewCB*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT STDMETHODCALLTYPE MessageSFVCB(UINT message, WPARAM column, LPARAM data) override
        {
            if (message == SFVM_COLUMNCLICK)
            {
                ED_PROBE_METRIC_INCREMENT(g_sortCallbackColumnClickRequests);
                InterlockedExchange(&g_sortCallbackLastColumn, static_cast<LONG>(column));
                return E_NOTIMPL;
            }
            if (message == SFVM_GETSORTDEFAULTS)
            {
                ED_PROBE_METRIC_INCREMENT(g_sortCallbackGetDefaultsRequests);
                if (InterlockedCompareExchange(&g_sortCallbackProbeEnabled, 0, 0) && column && data)
                {
                    *reinterpret_cast<int*>(column) = 1; // Ascending.
                    *reinterpret_cast<int*>(data) = static_cast<int>(_sortColumn);
                    return S_OK;
                }
                return E_NOTIMPL;
            }
            if (message != SFVM_GETDETAILSOF || !data) return E_NOTIMPL;
            auto* details = reinterpret_cast<DETAILSINFO*>(data);
            ED_PROBE_METRIC_INCREMENT(g_legacyDetailsCallbackRequests);
            InterlockedExchange(&g_legacyDetailsCallbackLastColumn, static_cast<LONG>(column));
            if (details->pidl) ED_PROBE_METRIC_INCREMENT(g_legacyDetailsCallbackItemRequests);
            else ED_PROBE_METRIC_INCREMENT(g_legacyDetailsCallbackHeaderRequests);
            details->fmt = LVCFMT_RIGHT;
            details->cxChar = 16;
            details->str.uType = STRRET_WSTR;
            const wchar_t* text = details->pidl ? L"callback-item" : L"Callback column";
            const auto length = wcslen(text) + 1;
            details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
            if (!details->str.pOleStr) return E_OUTOFMEMORY;
            StringCchCopyW(details->str.pOleStr, length, text);
            return S_OK;
        }
    private:
        volatile LONG _references = 1;
        const UINT _sortColumn;
    };

    SRWLOCK g_queryTraceLock = SRWLOCK_INIT;
    GUID g_forwardedQueryIds[64]{};
    UINT g_forwardedQueryCount = 0;

    HRESULT WrapChildPidl(PCUIDLIST_RELATIVE source, PIDLIST_RELATIVE* wrapped);
    HRESULT UnwrapChildPidlView(PCUIDLIST_RELATIVE source, PCUIDLIST_RELATIVE* original);

    bool IsBoundedPidl(const BYTE* bytes, UINT length)
    {
        if (!bytes || length < sizeof(USHORT)) return false;
        UINT offset = 0;
        while (offset + sizeof(USHORT) <= length)
        {
            USHORT itemSize = 0;
            CopyMemory(&itemSize, bytes + offset, sizeof(itemSize));
            if (!itemSize) return offset + sizeof(USHORT) == length;
            if (itemSize < sizeof(USHORT) || itemSize > length - offset) return false;
            offset += itemSize;
        }
        return false;
    }

    void TraceForwardedQuery(REFIID iid, HRESULT result, bool forwarded)
    {
        // Prototype diagnostics only: bounded distinct interfaces, no retained COM references.
        AcquireSRWLockExclusive(&g_queryTraceLock);
        for (UINT index = 0; index < g_forwardedQueryCount; ++index)
            if (g_forwardedQueryIds[index] == iid)
            {
                ReleaseSRWLockExclusive(&g_queryTraceLock);
                return;
            }
        if (g_forwardedQueryCount < ARRAYSIZE(g_forwardedQueryIds))
        {
            g_forwardedQueryIds[g_forwardedQueryCount++] = iid;
            wchar_t name[40]{};
            StringFromGUID2(iid, name, ARRAYSIZE(name));
            std::printf("native-static-shell-column: extra-QI=%ls; source=%s; hr=0x%08lX\n",
                name, forwarded ? "inner" : "unsupported", static_cast<unsigned long>(result));
            std::fflush(stdout);
        }
        ReleaseSRWLockExclusive(&g_queryTraceLock);
    }
    bool NameFromPidl(IShellFolder2* folder, PCUITEMID_CHILD pidl, std::wstring& name)
    {
        if (!pidl) return false;
        SHELLDETAILS details{};
        if (FAILED(folder->GetDetailsOf(pidl, 0, &details))) return false;
        if (details.str.uType == STRRET_WSTR)
        {
            if (!details.str.pOleStr) return false;
            name.assign(details.str.pOleStr);
            CoTaskMemFree(details.str.pOleStr);
            return true;
        }
        if (details.str.uType == STRRET_CSTR)
        {
            wchar_t buffer[MAX_PATH]{};
            if (FAILED(MultiByteToWideChar(CP_ACP, 0, details.str.cStr, -1, buffer, ARRAYSIZE(buffer)))) return false;
            name.assign(buffer);
            return true;
        }
        return false;
    }

    class WrappedEnumIdListState final
    {
    public:
        explicit WrappedEnumIdListState(IEnumIDList* inner) : _inner(inner) { if (_inner) _inner->AddRef(); }
        ULONG AddRef() { return InterlockedIncrement(&_refs); }
        ULONG Release()
        {
            const auto refs = InterlockedDecrement(&_refs);
            if (!refs) delete this;
            return refs;
        }
        HRESULT GetItem(ULONG index, PIDLIST_RELATIVE* item)
        {
            if (!item) return E_POINTER;
            *item = nullptr;
            std::lock_guard<std::mutex> lock(_gate);
            while (index >= _items.size() && !_exhausted)
            {
                if (_pending)
                {
                    try { _items.push_back(_pending); }
                    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
                    _pending = nullptr;
                    continue;
                }

                PITEMID_CHILD next = nullptr;
                ULONG fetched = 0;
                const auto result = _inner->Next(1, &next, &fetched);
                if (FAILED(result))
                {
                    if (next) CoTaskMemFree(next);
                    return result;
                }
                if (!fetched)
                {
                    if (next) CoTaskMemFree(next);
                    _exhausted = true;
                    break;
                }
                if (!next) return E_UNEXPECTED;

                if (InterlockedExchange(&g_failNextWrappedEnumeratorCacheInsert, 0) != 0)
                {
                    // The inner enumerator already advanced; keep its PIDL for the caller's retry.
                    _pending = next;
                    return E_OUTOFMEMORY;
                }
                try { _items.push_back(next); }
                catch (const std::bad_alloc&)
                {
                    _pending = next;
                    return E_OUTOFMEMORY;
                }
            }
            if (index >= _items.size()) return S_FALSE;

            const auto size = ILGetSize(_items[index]);
            auto* copy = size ? static_cast<PIDLIST_RELATIVE>(CoTaskMemAlloc(size)) : nullptr;
            if (!copy) return E_OUTOFMEMORY;
            CopyMemory(copy, _items[index], size);
            *item = copy;
            return S_OK;
        }
    private:
        ~WrappedEnumIdListState()
        {
            for (auto* item : _items) CoTaskMemFree(item);
            if (_pending) CoTaskMemFree(_pending);
            if (_inner) _inner->Release();
        }
        volatile LONG _refs = 1;
        IEnumIDList* _inner = nullptr;
        std::mutex _gate;
        std::vector<PIDLIST_RELATIVE> _items;
        PIDLIST_RELATIVE _pending = nullptr;
        bool _exhausted = false;
    };

    class WrappedEnumIdList final : public IEnumIDList
    {
    public:
        static HRESULT Create(IEnumIDList* inner, WrappedEnumIdList** value)
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            auto* state = new (std::nothrow) WrappedEnumIdListState(inner);
            if (!state) return E_OUTOFMEMORY;
            auto* wrapped = new (std::nothrow) WrappedEnumIdList(state, 0);
            state->Release();
            if (!wrapped) return E_OUTOFMEMORY;
            *value = wrapped;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid != IID_IUnknown && iid != IID_IEnumIDList) return E_NOINTERFACE;
            *object = static_cast<IEnumIDList*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_refs); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto refs = InterlockedDecrement(&_refs);
            if (!refs) delete this;
            return refs;
        }
        HRESULT STDMETHODCALLTYPE Next(ULONG count, PITEMID_CHILD* items, ULONG* fetched) override
        {
            if (!items || (count != 1 && !fetched)) return E_POINTER;
            if (fetched) *fetched = 0;
            for (ULONG index = 0; index < count; ++index) items[index] = nullptr;
            ULONG completed = 0;
            for (; completed < count; ++completed)
            {
                PIDLIST_RELATIVE raw = nullptr;
                const auto itemResult = _state->GetItem(_cursor, &raw);
                if (itemResult == S_FALSE) break;
                if (FAILED(itemResult))
                {
                    if (raw) CoTaskMemFree(raw);
                    if (fetched) *fetched = completed;
                    return itemResult;
                }
                const auto wrapResult = WrapChildPidl(raw, &items[completed]);
                CoTaskMemFree(raw);
                if (FAILED(wrapResult))
                {
                    if (fetched) *fetched = completed;
                    return wrapResult;
                }
                ++_cursor;
            }
            if (fetched) *fetched = completed;
            return completed == count ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Skip(ULONG count) override
        {
            ULONG skipped = 0;
            for (; skipped < count; ++skipped)
            {
                PIDLIST_RELATIVE item = nullptr;
                const auto result = _state->GetItem(_cursor, &item);
                if (item) CoTaskMemFree(item);
                if (result == S_FALSE) break;
                if (FAILED(result)) return result;
                ++_cursor;
            }
            return skipped == count ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Reset() override { _cursor = 0; return S_OK; }
        HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            auto* wrapped = new (std::nothrow) WrappedEnumIdList(_state, _cursor);
            if (!wrapped) return E_OUTOFMEMORY;
            *value = wrapped;
            return S_OK;
        }
    private:
        WrappedEnumIdList(WrappedEnumIdListState* state, ULONG cursor) : _state(state), _cursor(cursor) { _state->AddRef(); }
        ~WrappedEnumIdList() { _state->Release(); }
        volatile LONG _refs = 1;
        WrappedEnumIdListState* _state = nullptr;
        ULONG _cursor = 0;
    };

    HRESULT WrapChildPidl(PCUIDLIST_RELATIVE source, PIDLIST_RELATIVE* wrapped)
    {
        if (!source || !wrapped) return E_POINTER;
        *wrapped = nullptr;
        const auto sourceSize = ILGetSize(source);
        if (sourceSize < sizeof(USHORT) || sourceSize > USHRT_MAX - kWrappedPidlHeaderSize) return E_INVALIDARG;
        const auto itemSize = static_cast<USHORT>(kWrappedPidlHeaderSize + sourceSize);
        const auto allocationSize = static_cast<SIZE_T>(itemSize) + sizeof(USHORT);
        auto* result = static_cast<BYTE*>(CoTaskMemAlloc(allocationSize));
        if (!result) return E_OUTOFMEMORY;
        auto* cursor = result;
        CopyMemory(cursor, &itemSize, sizeof(itemSize));
        cursor += sizeof(USHORT);
        CopyMemory(cursor, &kWrappedPidlMagic, sizeof(kWrappedPidlMagic));
        cursor += sizeof(DWORD);
        CopyMemory(cursor, &sourceSize, sizeof(sourceSize));
        cursor += sizeof(UINT);
        CopyMemory(cursor, source, sourceSize);
        const USHORT terminator = 0;
        CopyMemory(result + itemSize, &terminator, sizeof(terminator));
        *wrapped = reinterpret_cast<PITEMID_CHILD>(result);
        return S_OK;
    }

    HRESULT UnwrapChildPidlView(PCUIDLIST_RELATIVE source, PCUIDLIST_RELATIVE* original)
    {
        if (!original) return E_POINTER;
        *original = source;
        if (!source) return S_OK;
        const auto itemSize = source->mkid.cb;
        if (itemSize < sizeof(USHORT) + sizeof(DWORD)) return S_OK;
        DWORD magic = 0;
        CopyMemory(&magic, reinterpret_cast<const BYTE*>(source) + sizeof(USHORT), sizeof(magic));
        if (magic != kWrappedPidlMagic) return S_OK;
        if (itemSize < kWrappedPidlHeaderSize + sizeof(USHORT)) return E_INVALIDARG;
        UINT payloadSize = 0;
        CopyMemory(&payloadSize, reinterpret_cast<const BYTE*>(source) + sizeof(USHORT) + sizeof(DWORD), sizeof(payloadSize));
        if (payloadSize < sizeof(USHORT) || kWrappedPidlHeaderSize + payloadSize != itemSize) return E_INVALIDARG;
        const auto* payload = reinterpret_cast<PCUIDLIST_RELATIVE>(reinterpret_cast<const BYTE*>(source) + kWrappedPidlHeaderSize);
        USHORT outerTerminator = 1;
        CopyMemory(&outerTerminator, reinterpret_cast<const BYTE*>(source) + itemSize, sizeof(outerTerminator));
        if (outerTerminator != 0 || !IsBoundedPidl(reinterpret_cast<const BYTE*>(payload), payloadSize)) return E_INVALIDARG;
        *original = payload;
        return S_OK;
    }

    ULONGLONG ValueForName(const std::wstring& name, bool& found)
    {
        found = true;
        if (_wcsicmp(name.c_str(), L"two.bin") == 0) return 2;
        if (_wcsicmp(name.c_str(), L"a-ten.bin") == 0 || _wcsicmp(name.c_str(), L"z-ten.bin") == 0) return 10;
        // The a- prefix makes the view fixture name order differ from either numeric direction.
        if (_wcsicmp(name.c_str(), L"ten.bin") == 0 || _wcsicmp(name.c_str(), L"ten-copy.bin") == 0 ||
            _wcsicmp(name.c_str(), L"a-ten-copy.bin") == 0) return 10;
        if (_wcsicmp(name.c_str(), L"hundred.bin") == 0) return 100;
        found = false;
        return 0;
    }

    class StaticShellFolderAdapter final : public IShellFolder2, public IPersistFolder2
    {
    public:
        explicit StaticShellFolderAdapter(IShellFolder2* inner, bool strictInterfaces = false, bool prototypeClass = false, bool wrappedPidls = false)
            : _inner(inner), _strictInterfaces(strictInterfaces), _prototypeClass(prototypeClass), _wrappedPidls(wrappedPidls)
        {
            if (_inner) _inner->AddRef();
            if (_inner)
            {
                for (UINT index = 0; index < kColumnLimit; ++index)
                {
                    SHELLDETAILS details{};
                    if (FAILED(_inner->GetDetailsOf(nullptr, index, &details))) break;
                    std::wstring title;
                    const bool hasTitle = details.str.uType == STRRET_WSTR && details.str.pOleStr && *details.str.pOleStr;
                    if (details.str.uType == STRRET_WSTR && details.str.pOleStr)
                    {
                        title.assign(details.str.pOleStr);
                        CoTaskMemFree(details.str.pOleStr);
                    }
                    else if (details.str.uType == STRRET_CSTR)
                    {
                        wchar_t buffer[MAX_PATH]{};
                        MultiByteToWideChar(CP_ACP, 0, details.str.cStr, -1, buffer, ARRAYSIZE(buffer));
                        title.assign(buffer);
                    }
                    if (!hasTitle && title.empty()) break;
                    ++_columnCount;
                }
            }
        }

        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override
        {
            if (iid == IID_IPropertyStore) ED_PROBE_METRIC_INCREMENT(g_propertyStoreQueryInterfaceRequests);
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IShellFolder || iid == IID_IShellFolder2)
            {
                *object = static_cast<IShellFolder2*>(this);
                AddRef();
                return S_OK;
            }
            if (_strictInterfaces && (iid == IID_IPersist || iid == IID_IPersistFolder || iid == IID_IPersistFolder2))
            {
                *object = static_cast<IPersistFolder2*>(this);
                AddRef();
                return S_OK;
            }            const auto result = !_strictInterfaces && _inner ? _inner->QueryInterface(iid, object) : E_NOINTERFACE;
            TraceForwardedQuery(iid, result, !_strictInterfaces);
            return result;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_refs); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto refs = InterlockedDecrement(&_refs);
            if (!refs) delete this;
            return refs;
        }

        HRESULT STDMETHODCALLTYPE GetClassID(CLSID* id) override
        {
            ED_PROBE_METRIC_INCREMENT(g_persistClassIdRequests);
            if (!id) return E_POINTER;
            if (_prototypeClass)
            {
                // Unregistered diagnostic identity: keeps the filesystem root PIDL unchanged.
                *id = { 0xE319CE20, 0x65C4, 0x41F7, { 0xA4, 0x96, 0x37, 0x5E, 0xBE, 0x0B, 0x2F, 0xC1 } };
                return S_OK;
            }
            IPersistFolder2* persist = nullptr;
            auto result = _inner->QueryInterface(IID_PPV_ARGS(&persist));
            if (FAILED(result)) return result;
            result = persist->GetClassID(id);
            persist->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE pidl) override
        {
            IPersistFolder2* persist = nullptr;
            auto result = _inner->QueryInterface(IID_PPV_ARGS(&persist));
            if (FAILED(result)) return result;
            result = persist->Initialize(pidl);
            persist->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetCurFolder(PIDLIST_ABSOLUTE* pidl) override
        {
            ED_PROBE_METRIC_INCREMENT(g_persistRootRequests);
            if (!pidl) return E_POINTER;
            *pidl = nullptr;
            IPersistFolder2* persist = nullptr;
            auto result = _inner->QueryInterface(IID_PPV_ARGS(&persist));
            if (FAILED(result)) return result;
            result = persist->GetCurFolder(pidl);
            persist->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND hwnd, IBindCtx* bind, LPWSTR name, ULONG* eaten, PIDLIST_RELATIVE* item, ULONG* attributes) override
        {
            if (!item) return E_POINTER;
            *item = nullptr;
            PIDLIST_RELATIVE raw = nullptr;
            auto result = _inner->ParseDisplayName(hwnd, bind, name, eaten, &raw, attributes);
            if (FAILED(result) || !_wrappedPidls) { *item = raw; return result; }
            result = WrapChildPidl(raw, item);
            CoTaskMemFree(raw);
            return result;
        }
        HRESULT STDMETHODCALLTYPE EnumObjects(HWND hwnd, SHCONTF flags, IEnumIDList** enumerator) override
        {
            if (!enumerator) return E_POINTER;
            *enumerator = nullptr;
            IEnumIDList* raw = nullptr;
            auto result = _inner->EnumObjects(hwnd, flags, &raw);
            if (FAILED(result)) { if (raw) raw->Release(); return result; }
            if (!_wrappedPidls) { *enumerator = raw; return result; }
            if (!raw) return E_UNEXPECTED;
            WrappedEnumIdList* wrapped = nullptr;
            result = WrappedEnumIdList::Create(raw, &wrapped);
            raw->Release();
            if (FAILED(result)) return result;
            *enumerator = wrapped;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (iid == IID_IPropertyStore) ED_PROBE_METRIC_INCREMENT(g_propertyStoreBindToObjectRequests);
            if (!value) return E_POINTER;
            *value = nullptr;
            PCUIDLIST_RELATIVE original = nullptr;
            auto result = UnwrapChildPidlView(item, &original);
            if (FAILED(result)) return result;
            if (iid == IID_IPropertyStoreFactory && InterlockedCompareExchange(&g_propertyStoreFactoryProbeEnabled, 0, 0))
            {
                ED_PROBE_METRIC_INCREMENT(g_propertyStoreFactoryBindRequests);
                VARIANT size{};
                VariantInit(&size);
                result = _inner->GetDetailsEx(reinterpret_cast<PCUITEMID_CHILD>(original), &kSizeKey, &size);
                if (FAILED(result)) { VariantClear(&size); return result; }
                ULONGLONG byteCount = size.vt == VT_UI8 ? size.ullVal : size.vt == VT_I8 ? static_cast<ULONGLONG>(size.llVal) : 10;
                VariantClear(&size);
                const auto testValue = byteCount > LONG_MAX ? LONG_MAX : static_cast<LONG>(byteCount);
                ULONGLONG testSize = byteCount;
                std::wstring itemName;
                if (NameFromPidl(_inner, reinterpret_cast<PCUITEMID_CHILD>(original), itemName))
                {
                    if (_wcsicmp(itemName.c_str(), L"a-ten.bin") == 0) testSize = 100;
                    else if (_wcsicmp(itemName.c_str(), L"two.bin") == 0) testSize = 200;
                    else if (_wcsicmp(itemName.c_str(), L"z-ten.bin") == 0) testSize = 10;
                }
                auto* factory = new (std::nothrow) StaticByteCountPropertyStoreFactory(testValue, testSize);
                if (!factory) return E_OUTOFMEMORY;
                *value = static_cast<IPropertyStoreFactory*>(factory);
                return S_OK;
            }
            if (!_wrappedPidls || (iid != IID_IShellFolder && iid != IID_IShellFolder2))
                return _inner->BindToObject(original, bind, iid, value);

            // Keep the diagnostic namespace adapter in the hierarchy so child
            // folders expose the same wrapped-PIDL behavior as their parent.
            IShellFolder2* childFolder = nullptr;
            result = _inner->BindToObject(original, bind, IID_IShellFolder2, reinterpret_cast<void**>(&childFolder));
            if (FAILED(result)) return result;
            if (!childFolder) return E_UNEXPECTED;
            auto* childAdapter = new (std::nothrow) StaticShellFolderAdapter(childFolder, false, false, true);
            childFolder->Release();
            if (!childAdapter) return E_OUTOFMEMORY;
            result = childAdapter->QueryInterface(iid, value);
            childAdapter->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (iid == IID_IPropertyStore) ED_PROBE_METRIC_INCREMENT(g_propertyStoreBindToStorageRequests);
            PCUIDLIST_RELATIVE original = nullptr;
            auto result = UnwrapChildPidlView(item, &original);
            return FAILED(result) ? result : _inner->BindToStorage(original, bind, iid, value);
        }
        HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM column, PCUIDLIST_RELATIVE left, PCUIDLIST_RELATIVE right) override
        {
            PCUIDLIST_RELATIVE originalLeft = nullptr, originalRight = nullptr;
            auto unwrapResult = UnwrapChildPidlView(left, &originalLeft);
            if (FAILED(unwrapResult)) return unwrapResult;
            unwrapResult = UnwrapChildPidlView(right, &originalRight);
            if (FAILED(unwrapResult)) return unwrapResult;
            left = originalLeft;
            right = originalRight;
            ED_PROBE_METRIC_INCREMENT(g_allComparisons);
            InterlockedExchange(&g_lastCompareColumn, LOWORD(column));
            const auto columnIndex = LOWORD(column);
            if (columnIndex == _columnCount + 1) return _inner->CompareIDs(15, left, right);
            const bool isStaticNumeric = columnIndex == _columnCount || columnIndex == _columnCount + 2;
            if (!isStaticNumeric && columnIndex != 1) return _inner->CompareIDs(column, left, right);
            if (columnIndex == 1) ED_PROBE_METRIC_INCREMENT(g_standardSizeComparisons);
            else if (columnIndex == _columnCount + 2) ED_PROBE_METRIC_INCREMENT(g_documentByteCountComparisons);
            else ED_PROBE_METRIC_INCREMENT(g_staticColumnComparisons);
            std::wstring leftName, rightName;
            if (!NameFromPidl(_inner, left, leftName) || !NameFromPidl(_inner, right, rightName)) return E_FAIL;
            bool leftFound = false, rightFound = false;
            const auto leftValue = ValueForName(leftName, leftFound);
            const auto rightValue = ValueForName(rightName, rightFound);
            if (!leftFound || !rightFound)
            {
                // Pending diagnostic values are empty, not errors or fabricated zero-byte results.
                if (!leftFound && !rightFound) return _inner->CompareIDs(0, left, right);
                const short comparison = leftFound ? 1 : -1;
                return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, static_cast<USHORT>(comparison));
            }
            const short comparison = leftValue < rightValue ? -1 : leftValue > rightValue ? 1 : 0;
            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, static_cast<USHORT>(comparison));
        }
        HRESULT STDMETHODCALLTYPE CreateViewObject(HWND owner, REFIID iid, void** value) override
        {
            if (iid != IID_IShellView) return _inner->CreateViewObject(owner, iid, value);
            SFV_CREATE create{};
            create.cbSize = sizeof(create);
            create.pshf = static_cast<IShellFolder*>(this);
            LegacyDetailsCallback* callback = nullptr;
            if (InterlockedCompareExchange(&g_legacyDetailsCallbackEnabled, 0, 0) ||
                InterlockedCompareExchange(&g_sortCallbackProbeEnabled, 0, 0))
            {
                callback = new (std::nothrow) LegacyDetailsCallback(_columnCount + 2);
                if (!callback) return E_OUTOFMEMORY;
                create.psfvcb = callback;
            }
            const auto result = SHCreateShellFolderView(&create, reinterpret_cast<IShellView**>(value));
            if (callback) callback->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT count, PCUITEMID_CHILD_ARRAY items, SFGAOF* attributes) override
        {
            std::vector<PCUITEMID_CHILD> original;
            try { original.resize(count); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            for (UINT index = 0; index < count; ++index)
            {
                auto result = UnwrapChildPidlView(items[index], &original[index]);
                if (FAILED(result)) return result;
            }
            return _inner->GetAttributesOf(count, original.data(), attributes);
        }
        HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND owner, UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, UINT* reserved, void** value) override
        {
            if (iid == IID_IPropertyStore) ED_PROBE_METRIC_INCREMENT(g_propertyStoreUiObjectRequests);
            std::vector<PCUITEMID_CHILD> original;
            try { original.resize(count); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            for (UINT index = 0; index < count; ++index)
            {
                auto result = UnwrapChildPidlView(items[index], &original[index]);
                if (FAILED(result)) return result;
            }
            return _inner->GetUIObjectOf(owner, count, original.data(), iid, reserved, value);
        }
        HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD item, SHGDNF flags, STRRET* name) override
        {
            PCUIDLIST_RELATIVE original = nullptr;
            auto result = UnwrapChildPidlView(item, &original);
            return FAILED(result) ? result : _inner->GetDisplayNameOf(original, flags, name);
        }
        HRESULT STDMETHODCALLTYPE SetNameOf(HWND owner, PCUITEMID_CHILD item, LPCWSTR name, SHGDNF flags, PITEMID_CHILD* renamed) override
        {
            PCUIDLIST_RELATIVE original = nullptr;
            auto result = UnwrapChildPidlView(item, &original);
            if (FAILED(result) || !_wrappedPidls || !renamed) return FAILED(result) ? result : _inner->SetNameOf(owner, original, name, flags, renamed);
            *renamed = nullptr;
            PIDLIST_RELATIVE raw = nullptr;
            result = _inner->SetNameOf(owner, original, name, flags, &raw);
            if (FAILED(result)) return result;
            result = WrapChildPidl(raw, renamed);
            CoTaskMemFree(raw);
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID* value) override { return _inner->GetDefaultSearchGUID(value); }
        HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch** enumerator) override { return _inner->EnumSearches(enumerator); }
        HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD reserved, ULONG* sort, ULONG* display) override
        {
            ED_PROBE_METRIC_INCREMENT(g_defaultColumnRequests);
            if (InterlockedCompareExchange(&g_defaultColumnSortProbeEnabled, 0, 0))
            {
                if (!sort || !display) return E_POINTER;
                *sort = _columnCount + 2;
                *display = 0;
                return S_OK;
            }
            return _inner->GetDefaultColumn(reserved, sort, display);
        }
        HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column, SHCOLSTATEF* state) override
        {
            if (column == _columnCount) { *state = SHCOLSTATE_TYPE_INT | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
            if (column == _columnCount + 1) { *state = SHCOLSTATE_TYPE_DATE | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
            if (column == _columnCount + 2) { *state = SHCOLSTATE_TYPE_INT | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
            return column < _columnCount ? _inner->GetDefaultColumnState(column, state) : E_INVALIDARG;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* column, VARIANT* value) override
        {
            PCUIDLIST_RELATIVE original = nullptr;
            auto unwrapResult = UnwrapChildPidlView(item, &original);
            if (FAILED(unwrapResult)) return unwrapResult;
            item = original;
            if (!column || (*column != kStaticSizeKey && *column != kSizeKey && *column != kDocumentByteCountKey)) return _inner->GetDetailsEx(item, column, value);
            if (*column == kSizeKey) ED_PROBE_METRIC_INCREMENT(g_standardSizeDetailsEx);
            else if (*column == kDocumentByteCountKey) ED_PROBE_METRIC_INCREMENT(g_documentByteCountDetailsEx);
            else ED_PROBE_METRIC_INCREMENT(g_customDetailsEx);
            if (!item) return E_INVALIDARG;
            std::wstring name;
            if (!NameFromPidl(_inner, item, name)) return E_FAIL;
            bool found = false;
            const auto number = ValueForName(name, found);
            if (!found && (*column == kSizeKey || *column == kDocumentByteCountKey)) return E_FAIL;
            VariantInit(value);
            if (!found) return S_OK; // VT_EMPTY while the custom result is unavailable.
            if (*column == kDocumentByteCountKey)
            {
                value->vt = VT_I4;
                value->lVal = static_cast<LONG>(number);
            }
            else
            {
                value->vt = VT_UI8;
                value->ullVal = number;
            }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override
        {
            if (!details) return E_POINTER;
            PCUIDLIST_RELATIVE original = nullptr;
            auto unwrapResult = UnwrapChildPidlView(item, &original);
            if (FAILED(unwrapResult)) return unwrapResult;
            item = original;
            if (column == _columnCount + 1)
            {
                if (item) return _inner->GetDetailsOf(item, 15, details);
                details->fmt = LVCFMT_LEFT;
                details->cxChar = 24;
                details->str.uType = STRRET_WSTR;
                const wchar_t* title = L"Known schema control";
                const auto length = wcslen(title) + 1;
                details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
                if (!details->str.pOleStr) return E_OUTOFMEMORY;
                StringCchCopyW(details->str.pOleStr, length, title);
                return S_OK;
            }
            if (column == _columnCount + 2)
            {
                if (item) ED_PROBE_METRIC_INCREMENT(g_documentByteCountDetailsOf);
                details->fmt = LVCFMT_RIGHT;
                details->cxChar = 18;
                details->str.uType = STRRET_WSTR;
                const wchar_t* text = L"Registered schema control";
                std::wstring valueText;
                if (item)
                {
                    std::wstring name;
                    if (!NameFromPidl(_inner, item, name)) return E_FAIL;
                    bool found = false;
                    const auto number = ValueForName(name, found);
                    valueText = found ? std::to_wstring(number) : L"";
                    text = valueText.c_str();
                }
                const auto length = wcslen(text) + 1;
                details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
                if (!details->str.pOleStr) return E_OUTOFMEMORY;
                StringCchCopyW(details->str.pOleStr, length, text);
                return S_OK;
            }
            if (column == 1 && item) ED_PROBE_METRIC_INCREMENT(g_standardSizeDetailsOf);
            if (column == 1 && item)
            {
                std::wstring name;
                if (!NameFromPidl(_inner, item, name)) return E_FAIL;
                bool found = false;
                const auto number = ValueForName(name, found);
                if (!found) return E_FAIL;
                details->fmt = LVCFMT_RIGHT;
                details->cxChar = 16;
                details->str.uType = STRRET_WSTR;
                const auto valueText = std::to_wstring(number);
                const auto length = valueText.size() + 1;
                details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
                if (!details->str.pOleStr) return E_OUTOFMEMORY;
                StringCchCopyW(details->str.pOleStr, length, valueText.c_str());
                return S_OK;
            }
            if (column < _columnCount) return _inner->GetDetailsOf(item, column, details);
            if (column != _columnCount) return E_INVALIDARG;
            // Isolated A/B only: make the native view try its documented legacy callback fallback.
            if (InterlockedCompareExchange(&g_legacyDetailsCallbackEnabled, 0, 0)) return E_NOTIMPL;
            ED_PROBE_METRIC_INCREMENT(g_customDetailsOf);
            if (item) ED_PROBE_METRIC_INCREMENT(g_customItemDetailsOf);
            else ED_PROBE_METRIC_INCREMENT(g_customHeaderDetailsOf);
            details->fmt = LVCFMT_RIGHT;
            details->cxChar = 16;
            details->str.uType = STRRET_WSTR;
            const wchar_t* text = L"Static numeric test";
            std::wstring valueText;
            if (item)
            {
                std::wstring name;
                if (!NameFromPidl(_inner, item, name)) return E_FAIL;
                bool found = false;
                const auto number = ValueForName(name, found);
                valueText = found ? std::to_wstring(number) : L"";
                text = valueText.c_str();
            }
            const auto length = wcslen(text) + 1;
            details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(length * sizeof(wchar_t)));
            if (!details->str.pOleStr) return E_OUTOFMEMORY;
            StringCchCopyW(details->str.pOleStr, length, text);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT column, SHCOLUMNID* id) override
        {
            ED_PROBE_METRIC_INCREMENT(g_mapColumnToScidRequests);
            if (!id) return E_POINTER;
            if (column < _columnCount) return _inner->MapColumnToSCID(column, id);
            if (column == _columnCount) *id = kStaticSizeKey;
            else if (column == _columnCount + 1) *id = kDateCreatedKey;
            else if (column == _columnCount + 2) *id = kDocumentByteCountKey;
            else return E_INVALIDARG;
            return S_OK;
        }

    private:
        ~StaticShellFolderAdapter() { if (_inner) _inner->Release(); }
        volatile LONG _refs = 1;
        IShellFolder2* _inner = nullptr;
        const bool _strictInterfaces;
        const bool _prototypeClass;
        const bool _wrappedPidls;
        UINT _columnCount = 0;
    };
}

// No-window contract exports let the managed probe validate the exact PIDL envelope
// before it is used by the isolated adapter. The payload is an owned copy of the
// complete original PIDL, including its terminator.
extern "C" ED_PROBE_EXPORT HRESULT WINAPI WrapStaticShellChildPidl(
    PCUIDLIST_RELATIVE source, PIDLIST_RELATIVE* wrapped)
{ return WrapChildPidl(source, wrapped); }

extern "C" ED_PROBE_EXPORT HRESULT WINAPI UnwrapStaticShellChildPidl(
    PCUIDLIST_RELATIVE wrapped, PIDLIST_RELATIVE* original)
{
    if (!wrapped || !original) return E_POINTER;
    *original = nullptr;
    PCUIDLIST_RELATIVE view = nullptr;
    auto result = UnwrapChildPidlView(wrapped, &view);
    if (FAILED(result) || view == wrapped) return FAILED(result) ? result : E_INVALIDARG;
    const auto payloadSize = ILGetSize(view);
    auto* copy = static_cast<BYTE*>(CoTaskMemAlloc(payloadSize));
    if (!copy) return E_OUTOFMEMORY;
    CopyMemory(copy, view, payloadSize);
    *original = reinterpret_cast<PITEMID_CHILD>(copy);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellColumnComparisonCount()
{
    return InterlockedCompareExchange(&g_staticColumnComparisons, 0, 0);
}

extern "C" ED_PROBE_EXPORT void WINAPI ResetStaticShellColumnProbeCounters()
{
    InterlockedExchange(&g_staticColumnComparisons, 0);
    InterlockedExchange(&g_allComparisons, 0);
    InterlockedExchange(&g_lastCompareColumn, -1);
    InterlockedExchange(&g_customDetailsEx, 0);
    InterlockedExchange(&g_standardSizeDetailsEx, 0);
    InterlockedExchange(&g_standardSizeComparisons, 0);
    InterlockedExchange(&g_standardSizeDetailsOf, 0);
    InterlockedExchange(&g_customDetailsOf, 0);
    InterlockedExchange(&g_customItemDetailsOf, 0);
    InterlockedExchange(&g_customHeaderDetailsOf, 0);
    InterlockedExchange(&g_documentByteCountComparisons, 0);
    InterlockedExchange(&g_documentByteCountDetailsEx, 0);
    InterlockedExchange(&g_documentByteCountDetailsOf, 0);
    InterlockedExchange(&g_mapColumnToScidRequests, 0);
    InterlockedExchange(&g_defaultColumnRequests, 0);
    InterlockedExchange(&g_propertyStoreQueryInterfaceRequests, 0);
    InterlockedExchange(&g_persistClassIdRequests, 0);
    InterlockedExchange(&g_persistRootRequests, 0);
    InterlockedExchange(&g_propertyStoreBindToObjectRequests, 0);
    InterlockedExchange(&g_propertyStoreBindToStorageRequests, 0);
    InterlockedExchange(&g_propertyStoreUiObjectRequests, 0);
    InterlockedExchange(&g_legacyDetailsCallbackRequests, 0);
    InterlockedExchange(&g_legacyDetailsCallbackHeaderRequests, 0);
    InterlockedExchange(&g_legacyDetailsCallbackItemRequests, 0);
    InterlockedExchange(&g_legacyDetailsCallbackLastColumn, -1);
    InterlockedExchange(&g_sortCallbackColumnClickRequests, 0);
    InterlockedExchange(&g_sortCallbackLastColumn, -1);
    InterlockedExchange(&g_sortCallbackGetDefaultsRequests, 0);
    InterlockedExchange(&g_propertyStoreFactoryBindRequests, 0);
    InterlockedExchange(&g_propertyStoreFactoryCreateRequests, 0);
    InterlockedExchange(&g_propertyStoreValueRequests, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreFactoryBindRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreFactoryBindRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreFactoryCreateRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreFactoryCreateRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreValueRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreValueRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT void WINAPI SetStaticShellPropertyStoreFactoryProbe(BOOL enabled)
{
    InterlockedExchange(&g_propertyStoreFactoryProbeEnabled, enabled ? 1 : 0);
}

extern "C" ED_PROBE_EXPORT void WINAPI SetStaticShellLegacyDetailsCallbackProbe(BOOL enabled)
{
    InterlockedExchange(&g_legacyDetailsCallbackEnabled, enabled ? 1 : 0);
}
extern "C" ED_PROBE_EXPORT void WINAPI SetStaticShellSortCallbackProbe(BOOL enabled)
{
    InterlockedExchange(&g_sortCallbackProbeEnabled, enabled ? 1 : 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellSortCallbackColumnClickCount()
{
    return InterlockedCompareExchange(&g_sortCallbackColumnClickRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellSortCallbackLastColumn()
{
    return InterlockedCompareExchange(&g_sortCallbackLastColumn, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellSortCallbackGetDefaultsCount()
{
    return InterlockedCompareExchange(&g_sortCallbackGetDefaultsRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellLegacyDetailsCallbackRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellLegacyDetailsCallbackHeaderRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackHeaderRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellLegacyDetailsCallbackItemRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackItemRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellLegacyDetailsCallbackLastColumn()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackLastColumn, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetAllShellColumnComparisonCount()
{
    return InterlockedCompareExchange(&g_allComparisons, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetLastShellColumnComparisonIndex()
{
    return InterlockedCompareExchange(&g_lastCompareColumn, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetCustomShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_customDetailsEx, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetStandardSizeShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_standardSizeDetailsEx, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetStandardSizeShellComparisonCount()
{
    return InterlockedCompareExchange(&g_standardSizeComparisons, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetStandardSizeShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_standardSizeDetailsOf, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetCustomShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customDetailsOf, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetCustomShellItemDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customItemDetailsOf, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetCustomShellHeaderDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customHeaderDetailsOf, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetDocumentByteCountShellComparisonCount()
{
    return InterlockedCompareExchange(&g_documentByteCountComparisons, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetDocumentByteCountShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_documentByteCountDetailsEx, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetDocumentByteCountShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_documentByteCountDetailsOf, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetMapColumnToScidRequestCount()
{
    return InterlockedCompareExchange(&g_mapColumnToScidRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetDefaultColumnRequestCount()
{
    return InterlockedCompareExchange(&g_defaultColumnRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT void WINAPI SetDefaultColumnSortProbe(BOOL enabled)
{
    InterlockedExchange(&g_defaultColumnSortProbeEnabled, enabled ? 1 : 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreQueryInterfaceRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreQueryInterfaceRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreBindToObjectRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreBindToObjectRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreBindToStorageRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreBindToStorageRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetPropertyStoreUiObjectRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreUiObjectRequests, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellPersistClassRequestCount()
{
    return InterlockedCompareExchange(&g_persistClassIdRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT LONG WINAPI GetStaticShellPersistRootRequestCount()
{
    return InterlockedCompareExchange(&g_persistRootRequests, 0, 0);
}
extern "C" ED_PROBE_EXPORT UINT WINAPI GetStaticShellDetailsSize()
{
    return static_cast<UINT>(sizeof(SHELLDETAILS));
}
extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateStaticShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}

// Isolated A/B control only: ordinary Shell-folder methods still delegate, but no raw inner interface escapes.
extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateStrictShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateClassIdentityShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, true, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateWrappedPidlShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, false, false, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" ED_PROBE_EXPORT void WINAPI FailNextWrappedEnumeratorCacheInsertForProbe()
{
    InterlockedExchange(&g_failNextWrappedEnumeratorCacheInsert, 1);
}
extern "C" ED_PROBE_EXPORT HRESULT WINAPI VerifyStaticShellViewFolder(
    IShellView* view, IShellFolder2* adapter, BOOL* matches)
{
    if (!view || !adapter || !matches) return E_POINTER;
    *matches = FALSE;
    IFolderView2* folderView = nullptr;
    IShellFolder2* viewFolder = nullptr;
    IUnknown* adapterIdentity = nullptr;
    IUnknown* viewIdentity = nullptr;
    HRESULT hr = view->QueryInterface(IID_PPV_ARGS(&folderView));
    if (SUCCEEDED(hr)) hr = folderView->GetFolder(IID_PPV_ARGS(&viewFolder));
    if (SUCCEEDED(hr)) hr = adapter->QueryInterface(IID_PPV_ARGS(&adapterIdentity));
    if (SUCCEEDED(hr)) hr = viewFolder->QueryInterface(IID_PPV_ARGS(&viewIdentity));
    if (SUCCEEDED(hr)) *matches = adapterIdentity == viewIdentity;
    if (viewIdentity) viewIdentity->Release();
    if (adapterIdentity) adapterIdentity->Release();
    if (viewFolder) viewFolder->Release();
    if (folderView) folderView->Release();
    return hr;
}

namespace
{
    struct SyntheticFileIdentity
    {
        ULONGLONG volumeSerial = 0;
        BYTE fileId[16]{};
        bool available = false;
    };

    HRESULT GetExtendedFilesystemPath(const std::wstring& path, std::wstring* extendedPath)
    {
        if (!extendedPath) return E_POINTER;
        constexpr auto backslash = static_cast<wchar_t>(0x5C);
        try
        {
            if (path.rfind(L"\\\\?\\", 0) == 0)
                *extendedPath = path;
            else if (path.size() >= 2 && (path[0] == backslash || path[0] == L'/') &&
                (path[1] == backslash || path[1] == L'/'))
                *extendedPath = L"\\\\?\\UNC\\" + path.substr(2);
            else if (path.size() >= 3 && path[1] == L':' && (path[2] == backslash || path[2] == L'/'))
                *extendedPath = L"\\\\?\\" + path;
            else
                *extendedPath = path;
        }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        return S_OK;
    }

    // Minimal, unregistered namespace used only to ask DefView whether it will
    // call IShellFolder2::CompareIDs for the standard Size column.
    struct SyntheticChild
    {
        USHORT cb;
        USHORT id;
        ULONGLONG size;
        DWORD attributes;
        WCHAR name[MAX_PATH];
        ULONGLONG fileCount;
        ULONGLONG folderCount;
        ULONGLONG parentBytes;
        SyntheticFileIdentity identity;
    };

    struct SyntheticFileIdentityLess
    {
        bool operator()(const SyntheticFileIdentity& left, const SyntheticFileIdentity& right) const noexcept
        {
            if (left.volumeSerial != right.volumeSerial) return left.volumeSerial < right.volumeSerial;
            return memcmp(left.fileId, right.fileId, sizeof(left.fileId)) < 0;
        }
    };

    struct SyntheticRecursiveSizeSnapshot
    {
        SyntheticFileIdentity identity;
        ULONGLONG bytes = 0;
        ULONGLONG files = 0;
        ULONGLONG folders = 0;
        ULONGLONG unreadable = 0;
        ULONGLONG lastAccess = 0;
        bool sizeAvailable = true;
        bool countsAvailable = true;
    };

    bool TryGetSyntheticFileIdentity(PCWSTR path, SyntheticFileIdentity* identity)
    {
        if (!path || !identity) return false;
        std::wstring extendedPath;
        if (FAILED(GetExtendedFilesystemPath(path, &extendedPath))) return false;
        const auto file = CreateFileW(extendedPath.c_str(), FILE_READ_ATTRIBUTES,
            FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        FILE_ID_INFO information{};
        const bool available = GetFileInformationByHandleEx(file, FileIdInfo, &information, sizeof(information)) != FALSE;
        CloseHandle(file);
        if (!available) return false;
        identity->volumeSerial = information.VolumeSerialNumber;
        CopyMemory(identity->fileId, information.FileId.Identifier, sizeof(identity->fileId));
        identity->available = true;
        return true;
    }

    struct ShellParsingNameLess
    {
        bool operator()(const std::wstring& left, const std::wstring& right) const noexcept
        {
            return CompareStringOrdinal(left.data(), static_cast<int>(left.size()),
                right.data(), static_cast<int>(right.size()), TRUE) == CSTR_LESS_THAN;
        }
    };

    class SyntheticRecursiveSizeSnapshotStore final
    {
    public:
        using SnapshotMap = std::map<std::wstring, SyntheticRecursiveSizeSnapshot, ShellParsingNameLess>;
        enum class ScanReservation { Cached, Acquired, InProgress, Failed };

        explicit SyntheticRecursiveSizeSnapshotStore(size_t maximumSnapshots = 256) noexcept
            : _maximumSnapshots(maximumSnapshots) {}

        ULONG AddRef() { return InterlockedIncrement(&_references); }
        ULONG Release()
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT Set(PCWSTR parsingName, const SyntheticFileIdentity& identity, ULONGLONG bytes,
            ULONGLONG files = 0, ULONGLONG folders = 0, ULONGLONG unreadable = 0,
            bool sizeAvailable = true, bool countsAvailable = true)
        {
            if (!parsingName || !*parsingName || !identity.available) return E_INVALIDARG;
            try
            {
                std::lock_guard<std::mutex> lock(_mutex);
                const auto inProgress = _scansInProgress.find(parsingName);
                // An item-change notification can arrive after a worker starts scanning;
                // never let that pre-notification result repopulate the invalidated cache.
                if (inProgress != _scansInProgress.end() && inProgress->second) return S_FALSE;
                _sizes[parsingName] = { identity, bytes, files, folders, unreadable, NextAccessSequence(), sizeAvailable, countsAvailable };
                TrimToCapacity();
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            return S_OK;
        }
        HRESULT SetForProviderGeneration(LONG generation, PCWSTR parsingName, const SyntheticFileIdentity& identity,
            ULONGLONG bytes, ULONGLONG files = 0, ULONGLONG folders = 0, ULONGLONG unreadable = 0,
            bool sizeAvailable = true, bool countsAvailable = true)
        {
            // Serialize commit with provider replacement, including the cache clear, not only with its callback.
            AcquireSRWLockShared(&g_folderSizeProviderLock);
            const auto result = FolderSizeScanCancelled(nullptr, generation)
                ? HRESULT_FROM_WIN32(ERROR_CANCELLED)
                : Set(parsingName, identity, bytes, files, folders, unreadable, sizeAvailable, countsAvailable);
            ReleaseSRWLockShared(&g_folderSizeProviderLock);
            return result;
        }
        HRESULT Rename(PCWSTR oldParsingName, PCWSTR newParsingName, const SyntheticFileIdentity& identity)
        {
            InvalidateManagedFolderSize(oldParsingName);
            InvalidateManagedFolderSize(newParsingName);
            if (!oldParsingName || !*oldParsingName || !newParsingName || !*newParsingName || !identity.available)
                return E_INVALIDARG;
            try
            {
                std::wstring newName(newParsingName);
                std::lock_guard<std::mutex> lock(_mutex);
                const auto old = _sizes.find(oldParsingName);
                if (old == _sizes.end()) return S_FALSE;
                const SyntheticFileIdentityLess less{};
                if (less(old->second.identity, identity) || less(identity, old->second.identity))
                {
                    _sizes.erase(old);
                    return S_FALSE;
                }
                if (!ShellParsingNameLess{}(old->first, newName) &&
                    !ShellParsingNameLess{}(newName, old->first)) return S_OK;

                const auto snapshot = old->second;
                _sizes.insert_or_assign(std::move(newName), snapshot);
                _sizes.erase(old);
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            return S_OK;
        }
        size_t RemoveSubtree(PCWSTR parsingName)
        {
            InvalidateManagedFolderSize(parsingName);
            if (!parsingName || !*parsingName) return 0;
            std::wstring root;
            try { root.assign(parsingName); }
            catch (const std::bad_alloc&) { return 0; }
            std::lock_guard<std::mutex> lock(_mutex);
            size_t removed = 0;
            for (auto entry = _sizes.begin(); entry != _sizes.end();)
            {
                const auto& candidate = entry->first;
                const auto rootLength = static_cast<int>(root.size());
                const bool prefixMatches = candidate.size() >= root.size() &&
                    CompareStringOrdinal(candidate.data(), rootLength, root.data(), rootLength, TRUE) == CSTR_EQUAL;
                const bool rootHasSeparator = !root.empty() && (root.back() == L'\\' || root.back() == L'/');
                const bool samePath = prefixMatches && candidate.size() == root.size();
                const bool descendant = prefixMatches && candidate.size() > root.size() &&
                    (rootHasSeparator || candidate[root.size()] == L'\\' || candidate[root.size()] == L'/');
                if (samePath || descendant)
                {
                    entry = _sizes.erase(entry);
                    ++removed;
                }
                else ++entry;
            }
            for (auto& entry : _scansInProgress)
            {
                const auto& candidate = entry.first;
                const auto rootLength = static_cast<int>(root.size());
                const bool prefixMatches = candidate.size() >= root.size() &&
                    CompareStringOrdinal(candidate.data(), rootLength, root.data(), rootLength, TRUE) == CSTR_EQUAL;
                const bool rootHasSeparator = !root.empty() && (root.back() == L'\\' || root.back() == L'/');
                const bool samePath = prefixMatches && candidate.size() == root.size();
                const bool descendant = prefixMatches && candidate.size() > root.size() &&
                    (rootHasSeparator || candidate[root.size()] == L'\\' || candidate[root.size()] == L'/');
                if (samePath || descendant) entry.second = true;
            }
            return removed;
        }
        size_t InvalidatePath(PCWSTR changedParsingName, bool includeSubtree, size_t* invalidatedReservations = nullptr)
        {
            InvalidateManagedFolderSize(changedParsingName);
            if (!changedParsingName || !*changedParsingName) return 0;
            std::wstring changed;
            try { changed.assign(changedParsingName); }
            catch (const std::bad_alloc&) { return 0; }
            std::lock_guard<std::mutex> lock(_mutex);
            const auto matchesChangedPath = [&](const std::wstring& candidate)
            {
                const auto prefixMatches = [](const std::wstring& left, const std::wstring& right, size_t length)
                {
                    return CompareStringOrdinal(left.data(), static_cast<int>(length),
                        right.data(), static_cast<int>(length), TRUE) == CSTR_EQUAL;
                };
                const bool samePath = candidate.size() == changed.size() &&
                    prefixMatches(candidate, changed, candidate.size());
                const bool candidateIsAncestor = candidate.size() < changed.size() &&
                    prefixMatches(candidate, changed, candidate.size()) &&
                    (candidate.back() == L'\\' || candidate.back() == L'/' ||
                        changed[candidate.size()] == L'\\' || changed[candidate.size()] == L'/');
                const bool candidateIsDescendant = includeSubtree && candidate.size() > changed.size() &&
                    prefixMatches(candidate, changed, changed.size()) &&
                    (changed.back() == L'\\' || changed.back() == L'/' ||
                        candidate[changed.size()] == L'\\' || candidate[changed.size()] == L'/');
                return samePath || candidateIsAncestor || candidateIsDescendant;
            };
            size_t removed = 0;
            for (auto entry = _sizes.begin(); entry != _sizes.end();)
            {
                if (matchesChangedPath(entry->first))
                {
                    entry = _sizes.erase(entry);
                    ++removed;
                }
                else ++entry;
            }
            size_t invalidated = 0;
            for (auto& entry : _scansInProgress)
            {
                if (!matchesChangedPath(entry.first)) continue;
                entry.second = true;
                ++invalidated;
            }
            if (invalidatedReservations) *invalidatedReservations = invalidated;
            return removed;
        }
        bool InvalidateReservedPathExact(PCWSTR parsingName)
        {
            if (!parsingName || !*parsingName) return false;
            std::wstring exact;
            try { exact.assign(parsingName); }
            catch (const std::bad_alloc&) { return false; }
            std::lock_guard<std::mutex> lock(_mutex);
            const auto reservation = _scansInProgress.find(exact);
            if (reservation == _scansInProgress.end()) return false;
            // Keep this NativeProbe seam exact-key and atomic so the probe cannot repeat broad subtree feedback.
            reservation->second = true;
            _sizes.erase(exact);
            return true;
        }
        bool InvalidateSnapshotPathExact(PCWSTR parsingName)
        {
            if (!parsingName || !*parsingName) return false;
            std::wstring exact;
            try { exact.assign(parsingName); }
            catch (const std::bad_alloc&) { return false; }
            std::lock_guard<std::mutex> lock(_mutex);
            return _sizes.erase(exact) != 0;
        }
        bool TryGet(const std::wstring& parsingName, const SyntheticFileIdentity& identity, ULONGLONG* bytes,
            ULONGLONG* files = nullptr, ULONGLONG* folders = nullptr, ULONGLONG* unreadable = nullptr,
            bool* sizeAvailable = nullptr, bool* countsAvailable = nullptr) const
        {
            if (!identity.available || !bytes) return false;
            std::lock_guard<std::mutex> lock(_mutex);
            const auto found = _sizes.find(parsingName);
            if (found == _sizes.end())
            {
                ++_lookupMisses;
                return false;
            }
            const SyntheticFileIdentityLess less{};
            if (less(found->second.identity, identity) || less(identity, found->second.identity))
            {
                // A reused parsing name now identifies another directory; discard the unreachable old snapshot.
                _sizes.erase(found);
                ++_lookupMisses;
                return false;
            }
            found->second.lastAccess = NextAccessSequence();
            ++_lookupHits;
            *bytes = found->second.bytes;
            if (files) *files = found->second.files;
            if (folders) *folders = found->second.folders;
            if (unreadable) *unreadable = found->second.unreadable;
            if (sizeAvailable) *sizeAvailable = found->second.sizeAvailable;
            if (countsAvailable) *countsAvailable = found->second.countsAvailable;
            return true;
        }
        void ClearForProviderChange()
        {
            std::lock_guard<std::mutex> lock(_mutex);
            _sizes.clear();
            for (auto& reservation : _scansInProgress) reservation.second = true;
        }
        ScanReservation TryReserveScan(const std::wstring& parsingName,
            const SyntheticFileIdentity& identity, ULONGLONG* cachedBytes)
        {
            if (!identity.available || !cachedBytes) return ScanReservation::Failed;
            try
            {
                std::lock_guard<std::mutex> lock(_mutex);
                const auto cached = _sizes.find(parsingName);
                if (cached != _sizes.end())
                {
                    const SyntheticFileIdentityLess less{};
                    if (!less(cached->second.identity, identity) && !less(identity, cached->second.identity))
                    {
                        cached->second.lastAccess = NextAccessSequence();
                        ++_lookupHits;
                        *cachedBytes = cached->second.bytes;
                        return ScanReservation::Cached;
                    }
                    _sizes.erase(cached);
                }
                ++_lookupMisses;
                if (_scansInProgress.find(parsingName) != _scansInProgress.end())
                    return ScanReservation::InProgress;
                // This store is shared across folder wrappers, so the reservation coalesces same-path scans across views.
                _scansInProgress.emplace(parsingName, false);
                return ScanReservation::Acquired;
            }
            catch (const std::bad_alloc&) { return ScanReservation::Failed; }
        }
        void ReleaseScan(const std::wstring& parsingName)
        {
            std::lock_guard<std::mutex> lock(_mutex);
            _scansInProgress.erase(parsingName);
        }
        bool HasReservation(const std::wstring& parsingName) const
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return _scansInProgress.find(parsingName) != _scansInProgress.end();
        }
        size_t Count() const
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return _sizes.size();
        }
        size_t PayloadBytes() const
        {
            std::lock_guard<std::mutex> lock(_mutex);
            size_t bytes = 0;
            for (const auto& entry : _sizes)
                bytes += sizeof(entry.first) + sizeof(entry.second) + (entry.first.size() + 1) * sizeof(wchar_t);
            return bytes;
        }
        void ResetLookupMetrics() const
        {
            std::lock_guard<std::mutex> lock(_mutex);
            _lookupHits = 0;
            _lookupMisses = 0;
            _evictions = 0;
            _evictionSearchSteps = 0;
            _evictionSearchTicks = 0;
        }
        ULONGLONG LookupHits() const { std::lock_guard<std::mutex> lock(_mutex); return _lookupHits; }
        ULONGLONG LookupMisses() const { std::lock_guard<std::mutex> lock(_mutex); return _lookupMisses; }
        ULONGLONG Evictions() const { std::lock_guard<std::mutex> lock(_mutex); return _evictions; }
        ULONGLONG EvictionSearchSteps() const { std::lock_guard<std::mutex> lock(_mutex); return _evictionSearchSteps; }
        ULONGLONG EvictionSearchMicroseconds() const
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return static_cast<ULONGLONG>(_evictionSearchTicks) * 1000000ULL /
                static_cast<ULONGLONG>(PerformanceFrequency());
        }
        size_t IndexedHeapAuxiliaryBytesEstimate() const noexcept
        {
            return _maximumSnapshots * (sizeof(size_t) + sizeof(SnapshotMap::iterator));
        }
    private:
        static LONGLONG PerformanceFrequency() noexcept
        {
            static const LONGLONG frequency = []() noexcept
            {
                LARGE_INTEGER value{};
                return QueryPerformanceFrequency(&value) ? value.QuadPart : 1;
            }();
            return frequency;
        }
        ULONGLONG NextAccessSequence() const noexcept { return ++_accessSequence; }
        void TrimToCapacity() const noexcept
        {
            if (_sizes.size() <= _maximumSnapshots) return;
            LARGE_INTEGER started{};
            QueryPerformanceCounter(&started);
            while (_sizes.size() > _maximumSnapshots)
            {
                auto oldest = _sizes.begin();
                ++_evictionSearchSteps;
                for (auto entry = std::next(oldest); entry != _sizes.end(); ++entry)
                {
                    ++_evictionSearchSteps;
                    if (entry->second.lastAccess < oldest->second.lastAccess) oldest = entry;
                }
                _sizes.erase(oldest);
                ++_evictions;
            }
            LARGE_INTEGER finished{};
            QueryPerformanceCounter(&finished);
            _evictionSearchTicks += finished.QuadPart - started.QuadPart;
        }
        volatile LONG _references = 1;
        const size_t _maximumSnapshots;
        mutable std::mutex _mutex;
        mutable SnapshotMap _sizes;
        std::map<std::wstring, bool, ShellParsingNameLess> _scansInProgress;
        mutable ULONGLONG _accessSequence = 0;
        mutable ULONGLONG _lookupHits = 0;
        mutable ULONGLONG _lookupMisses = 0;
        mutable ULONGLONG _evictions = 0;
        mutable ULONGLONG _evictionSearchSteps = 0;
        mutable LONGLONG _evictionSearchTicks = 0;
    };

    constexpr SyntheticChild kSyntheticChildren[] =
    {
        { sizeof(SyntheticChild), 1, 100, 0, L"a-large.bin" },
        { sizeof(SyntheticChild), 2, 2, 0, L"two.bin" },
        { sizeof(SyntheticChild), 3, 10, 0, L"z-ten.bin" }
    };
    volatile LONG g_syntheticCompareCount = 0;
    volatile LONG g_syntheticCompareByColumn[8]{};
    volatile LONG g_syntheticSizeCompareCount = 0;
    volatile LONG g_syntheticDirectoryMetricsDuringCompareCount = 0;
    volatile LONG g_syntheticRecursiveSizeCacheMissesDuringCompareCount = 0;
    volatile LONG g_syntheticDirectoryMetricsCallCount = 0;
    volatile LONG g_syntheticDirectoryMetricsEntryCount = 0;
    volatile LONG g_syntheticOnDemandSizeScanCount = 0;
    volatile LONG64 g_syntheticOnDemandSizeScanMicroseconds = 0;
    volatile LONG g_syntheticItemDetailsOfCount = 0;
    volatile LONG g_syntheticSizeDetailsExCount = 0;
    volatile LONG g_syntheticSizeDetailsExFolderCount = 0;
    volatile LONG g_syntheticSizeDetailsExFileCount = 0;
    volatile LONG g_syntheticSizeDetailsOfItemCount = 0;
    volatile LONG g_syntheticCustomSizeDetailsExCount = 0;
    volatile LONG g_syntheticCustomSizeDetailsExFolderCount = 0;
    volatile LONG g_syntheticCustomSizeDetailsExFileCount = 0;
    volatile LONG g_syntheticPropertyStoreFactoryRequests = 0;
    volatile LONG g_syntheticPropertyStoreRequests = 0;
    volatile LONG g_syntheticOtherUiObjectRequests = 0;
    volatile LONG g_syntheticGetAttributesOfCount = 0;
    volatile LONG g_syntheticFilesystemAttributeCount = 0;
#if defined(EXPLORERDIRS_NAMESPACE_ATTRIBUTES_EXPERIMENT)
    volatile LONG g_syntheticMaskFilesystemAttributes = 1;
#else
    volatile LONG g_syntheticMaskFilesystemAttributes = 0;
#endif
    volatile LONG g_syntheticOwnAttributesForProbe = 0;
    // The app persists PKEY_Size. Advertising a different custom key makes that
    // saved column absent from the native catalog, even though GetDetailsEx accepts it.
    // Keep custom-key comparisons opt-in for isolated probes only.
    volatile LONG g_syntheticCustomSizeColumnForProbe = 0;
    volatile LONG g_syntheticIgnoreRecursiveFolderSizeSortForProbe = 0;
#if defined(EXPLORERDIRS_NATIVEPROBE)
    volatile LONG g_syntheticHumanSizeTextForProbe = 0;
#else
    volatile LONG g_syntheticHumanSizeTextForProbe = 1;
#endif
    volatile LONG g_syntheticSizeDisplayUnitForProbe = 0;
    volatile LONG g_syntheticSizeDisplayDecimalsForProbe = 2;
    volatile LONG g_syntheticSizeDisplayThousandsForProbe = 0;
    volatile LONG g_syntheticSlowCustomSizeColumnForProbe = 0;
    volatile LONG g_syntheticRecursiveSizeScanEnabledForProbe = 1;
#if defined(EXPLORERDIRS_NATIVEPROBE)
    volatile LONG g_syntheticRecursiveSizeScanOnDemandForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeScanForProbe = 0;
#else
    // Product views must never recurse through every folder while enumerating a directory.
    // Request values on demand and let the bounded worker queue fill them asynchronously.
    volatile LONG g_syntheticRecursiveSizeScanOnDemandForProbe = 1;
    volatile LONG g_syntheticAsyncRecursiveSizeScanForProbe = 1;
#endif
    volatile LONG g_syntheticAsyncRecursiveSizeBatchByViewForProbe = 0;
    // NativeProbe-only latency injection for comparing async scan scheduling under slow storage.
    volatile LONG g_syntheticAsyncRecursiveSizeScanDelayMsForProbe = 0;
    // NativeProbe-only per-entry delay makes mid-enumeration cancellation deterministic.
    volatile LONG g_syntheticAsyncRecursiveSizeEntryDelayMsForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeScanCompletedForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeScanFinishedForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeScanStartedForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeActiveWorkersForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizePeakWorkersForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeNotificationsSentForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeItemNotificationsSentForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeDirectoryNotificationsSentForProbe = 0;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
    volatile LONG g_productAsyncItemNotificationAttempts = 0;
    volatile LONG g_productAsyncItemNotificationUnderSyntheticRoot = 0;
    volatile LONG g_productAsyncDirectoryNotificationAttempts = 0;
    // Large sorting probes can opt out of per-notification PIDL string capture while retaining counters.
    volatile LONG g_productNotificationIdentityCaptureEnabled = 1;
    volatile LONG g_productNotificationFlushForProbe = 0;
    volatile LONG g_productNotificationDelayMsForProbe = 0;
    // Notification callbacks must not bind Shell folders merely to produce diagnostic text.
    // Keep bounded immutable PIDL snapshots; resolve names only when the probe reads them.
    class ProductNotificationIdentityCapture
    {
        std::mutex _mutex;
        std::vector<std::vector<BYTE>> _items;
        size_t _payload = 0;
        volatile LONG _dropped = 0;
    public:
        void Record(PCIDLIST_ABSOLUTE pidl)
        {
            if (!pidl) return;
            const auto length = static_cast<size_t>(ILGetSize(pidl));
            try
            {
                std::lock_guard<std::mutex> lock(_mutex);
                if (_items.size() >= 32768 || length > 16 * 1024 * 1024 - _payload)
                {
                    InterlockedIncrement(&_dropped);
                    return;
                }
                const auto* bytes = reinterpret_cast<const BYTE*>(pidl);
                _items.emplace_back(bytes, bytes + length);
                _payload += length;
            }
            catch (const std::bad_alloc&) { InterlockedIncrement(&_dropped); }
        }

        LONG Count()
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return static_cast<LONG>(_items.size());
        }

        LONG Dropped() { return InterlockedCompareExchange(&_dropped, 0, 0); }

        HRESULT Read(LONG index, LPWSTR buffer, UINT capacity)
        {
            if (!buffer || capacity == 0) return E_INVALIDARG;
            buffer[0] = L'\0';
            PIDLIST_ABSOLUTE snapshot = nullptr;
            {
                std::lock_guard<std::mutex> lock(_mutex);
                if (index < 0 || static_cast<size_t>(index) >= _items.size()) return E_BOUNDS;
                snapshot = ILCloneFull(reinterpret_cast<PCIDLIST_ABSOLUTE>(_items[static_cast<size_t>(index)].data()));
            }
            if (!snapshot) return E_OUTOFMEMORY;
            LPWSTR parsingName = nullptr;
            const auto resolved = SHGetNameFromIDList(snapshot, SIGDN_DESKTOPABSOLUTEPARSING, &parsingName);
            CoTaskMemFree(snapshot);
            const auto result = FAILED(resolved) ? resolved :
                (parsingName ? StringCchCopyW(buffer, capacity, parsingName) : E_FAIL);
            CoTaskMemFree(parsingName);
            return result;
        }
    };
    ProductNotificationIdentityCapture g_productAsyncItemNotificationIdentities;
    volatile LONG g_productViewItemNotificationCallbacks = 0;
    volatile LONG g_productViewNotificationsUseSyntheticRoot = 1;
    volatile LONG g_productViewGetNotifyCalls = 0;
    volatile LONG g_productViewGetNotifyEventMask = 0;
    ProductNotificationIdentityCapture g_productViewItemNotificationIdentities;
    std::mutex g_productViewNotifyRootMutex;
    std::wstring g_productViewNotifyRootIdentity;

    void RecordProductAsyncItemNotificationIdentity(PCIDLIST_ABSOLUTE itemPidl)
    {
        if (InterlockedCompareExchange(&g_productNotificationIdentityCaptureEnabled, 0, 0))
            g_productAsyncItemNotificationIdentities.Record(itemPidl);
    }

    void RecordProductViewItemNotificationIdentity(PCIDLIST_ABSOLUTE itemPidl)
    {
        if (InterlockedCompareExchange(&g_productNotificationIdentityCaptureEnabled, 0, 0))
            g_productViewItemNotificationIdentities.Record(itemPidl);
    }

#endif
    // Product notifications target the completed folder row so Shell can refresh that value directly.
#if defined(EXPLORERDIRS_NATIVEPROBE)
    // NativeProbe-only A/B default preserves the parent-directory baseline unless explicitly enabled.
    volatile LONG g_syntheticAsyncRecursiveSizeItemNotificationForProbe = 0;
#else
    volatile LONG g_syntheticAsyncRecursiveSizeItemNotificationForProbe = 1;
#endif
    volatile LONG g_syntheticAsyncRecursiveSizeNotificationInvalidatedReservationsForProbe = 0;
    volatile LONG g_syntheticTraceNotificationPidlsForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeQueueRejectedScanCountForProbe = 0;
    volatile LONG g_syntheticAsyncRecursiveSizeBatchRequestedEntriesForProbe = 0;
    HANDLE g_syntheticAsyncRecursiveSizeScanGateForProbe = nullptr;
    std::mutex g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe;
    HANDLE g_syntheticAsyncRecursiveSizeCommitGateForProbe = nullptr;
    HANDLE g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe = nullptr;
    std::wstring g_syntheticAsyncRecursiveSizeCommitGatePathForProbe;
    volatile LONG g_syntheticExtendedMetadataColumnsForProbe = 0;
    volatile LONG g_syntheticRelativePercentColumnTypeForProbe = SHCOLSTATE_TYPE_INT;
    volatile LONG64 g_syntheticChildSizeOverride = -1;

    class SyntheticAsyncRecursiveSizeQueue
    {
    public:
        static constexpr size_t kWorkerCount = 2;
        // The synthetic view probe permits 500 sibling folders in each of four panes.
        // Keep the queue bounded while allowing its complete 4 * (500 + child) demand.
        static constexpr size_t kMaximumQueuedTasks = 2048;
        static constexpr DWORD kShellUpdateCoalesceMilliseconds = 200;

        SyntheticAsyncRecursiveSizeQueue()
        {
            try
            {
                _workers.reserve(kWorkerCount);
                for (size_t index = 0; index < kWorkerCount; ++index)
                    _workers.emplace_back([this] { WorkerLoop(); });
            }
            catch (...)
            {
                {
                    std::lock_guard<std::mutex> lock(_mutex);
                    _stopping = true;
                }
                _ready.notify_all();
                for (auto& worker : _workers) if (worker.joinable()) worker.join();
                throw;
            }
        }

        ~SyntheticAsyncRecursiveSizeQueue()
        {
            {
                std::lock_guard<std::mutex> lock(_mutex);
                _stopping = true;
            }
            _ready.notify_all();
            for (auto& worker : _workers) if (worker.joinable()) worker.join();
        }

        HRESULT Enqueue(std::function<void()>&& task, bool recursiveSizeScan = false)
        {
            std::lock_guard<std::mutex> lock(_mutex);
            if (_stopping) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
            if (_tasks.size() >= kMaximumQueuedTasks)
            {
                if (recursiveSizeScan) ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeQueueRejectedScanCountForProbe);
                return HRESULT_FROM_WIN32(ERROR_NOT_ENOUGH_QUOTA);
            }
            try { _tasks.push(std::move(task)); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            _ready.notify_one();
            return S_OK;
        }

    private:
        void WorkerLoop()
        {
            for (;;)
            {
                std::function<void()> task;
                {
                    std::unique_lock<std::mutex> lock(_mutex);
                    _ready.wait(lock, [this] { return _stopping || !_tasks.empty(); });
                    if (_stopping && _tasks.empty()) return;
                    task = std::move(_tasks.front());
                    _tasks.pop();
                }
                const auto active = InterlockedIncrement(&g_syntheticAsyncRecursiveSizeActiveWorkersForProbe);
                auto peak = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0, 0);
                while (active > peak)
                {
                    const auto observed = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, active, peak);
                    if (observed == peak) break;
                    peak = observed;
                }
                try { task(); }
                catch (...) { TraceSyntheticSortStage("synthetic-on-demand-size: queued-worker-task-threw"); }
                InterlockedDecrement(&g_syntheticAsyncRecursiveSizeActiveWorkersForProbe);
            }
        }

        std::mutex _mutex;
        std::condition_variable _ready;
        std::queue<std::function<void()>> _tasks;
        std::vector<std::thread> _workers;
        bool _stopping = false;
    };

    SyntheticAsyncRecursiveSizeQueue& GetSyntheticAsyncRecursiveSizeQueue()
    {
        static SyntheticAsyncRecursiveSizeQueue queue;
        return queue;
    }

    std::wstring FormatSyntheticSizeText(ULONGLONG bytes)
    {
        constexpr ULONGLONG unit = 1024;
        constexpr const wchar_t* units[] = { L"B", L"KB", L"MB", L"GB", L"TB", L"PB", L"EB" };
        const auto displayUnit = InterlockedCompareExchange(&g_syntheticSizeDisplayUnitForProbe, 0, 0);
        const auto configuredDecimalPlaces = InterlockedCompareExchange(&g_syntheticSizeDisplayDecimalsForProbe, 2, 2);
        const auto decimalPlaces = configuredDecimalPlaces < 0 ? 0L : configuredDecimalPlaces > 3 ? 3L : configuredDecimalPlaces;
        const bool useThousandsSeparator = InterlockedCompareExchange(&g_syntheticSizeDisplayThousandsForProbe, 0, 0) != 0;
        size_t unitIndex = 0;
        ULONGLONG divisor = 1;
        if (displayUnit == 0)
        {
            while (unitIndex + 1 < ARRAYSIZE(units) && bytes / divisor >= unit)
            {
                divisor *= unit;
                ++unitIndex;
            }
        }
        else
        {
            const auto fixedUnit = displayUnit < 1 ? 1L : displayUnit > static_cast<LONG>(ARRAYSIZE(units))
                ? static_cast<LONG>(ARRAYSIZE(units)) : displayUnit;
            unitIndex = static_cast<size_t>(fixedUnit - 1);
            for (size_t i = 0; i < unitIndex; ++i) divisor *= unit;
        }

        std::wostringstream formatted;
        if (unitIndex == 0)
        {
            formatted << bytes;
        }
        else
        {
            formatted << std::fixed << std::setprecision(decimalPlaces)
                << (static_cast<double>(bytes) / static_cast<double>(divisor));
        }
        auto number = formatted.str();
        const auto decimalPoint = number.find(L'.');
        if (decimalPoint != std::wstring::npos)
        {
            while (!number.empty() && number.back() == L'0') number.pop_back();
            if (!number.empty() && number.back() == L'.') number.pop_back();
        }

        WCHAR decimalSeparator[8] = L".";
        WCHAR thousandsSeparator[8] = L",";
        GetLocaleInfoEx(LOCALE_NAME_USER_DEFAULT, LOCALE_SDECIMAL, decimalSeparator, ARRAYSIZE(decimalSeparator));
        GetLocaleInfoEx(LOCALE_NAME_USER_DEFAULT, LOCALE_STHOUSAND, thousandsSeparator, ARRAYSIZE(thousandsSeparator));
        if (decimalPoint != std::wstring::npos && wcscmp(decimalSeparator, L".") != 0)
            number.replace(number.find(L'.'), 1, decimalSeparator);
        if (useThousandsSeparator && thousandsSeparator[0] != L'\0')
        {
            const auto integerEnd = number.find(decimalSeparator);
            const auto digitsEnd = integerEnd == std::wstring::npos ? number.size() : integerEnd;
            for (auto position = static_cast<std::ptrdiff_t>(digitsEnd) - 3; position > 0; position -= 3)
                number.insert(static_cast<size_t>(position), thousandsSeparator);
        }
        return number + L" " + units[unitIndex];
    }
    // Exclude synchronous property callbacks caused by the probe's own
    // ListView text reads from notification-strategy counters.
    thread_local BOOL g_syntheticSizeMetricsObservation = FALSE;
    thread_local BOOL g_syntheticCompareCallbackActive = FALSE;
    struct SyntheticCompareCallbackScope
    {
        BOOL previous;
        SyntheticCompareCallbackScope() : previous(g_syntheticCompareCallbackActive)
        {
            g_syntheticCompareCallbackActive = TRUE;
        }
        ~SyntheticCompareCallbackScope() { g_syntheticCompareCallbackActive = previous; }
    };
    constexpr PROPERTYKEY kSyntheticRecursiveSizeKey =
    { { 0x7E1F84B7, 0xC052, 0x4E51, { 0x8A, 0x3C, 0x86, 0x92, 0x54, 0xA6, 0xB9, 0x10 } }, 1 };
    constexpr PROPERTYKEY kSyntheticRelativeSizeKey =
    { { 0x7E1F84B7, 0xC052, 0x4E51, { 0x8A, 0x3C, 0x86, 0x92, 0x54, 0xA6, 0xB9, 0x10 } }, 2 };
    volatile LONG g_syntheticEnumObjectsCount = 0;
    volatile LONG g_syntheticEnumeratedItems = 0;
    volatile LONG g_syntheticClassFactoryCreateCount = 0;
    volatile LONG g_syntheticMapColumnToScidCount = 0;
    volatile LONG g_syntheticMapColumnToScidByColumn[4] = {};
    constexpr LONG kSyntheticMapColumnTraceCapacity = 256;
    struct SyntheticMapColumnTraceEntry
    {
        volatile LONG sequence = 0;
        UINT column = 0;
        SHCOLUMNID key{};
        HRESULT result = E_PENDING;
    };
    SyntheticMapColumnTraceEntry g_syntheticMapColumnTrace[kSyntheticMapColumnTraceCapacity]{};
    volatile LONG g_syntheticMapColumnTraceCount = 0;
    constexpr LONG kSyntheticColumnMetadataTraceCapacity = 128;
    struct SyntheticColumnMetadataTraceEntry
    {
        volatile LONG sequence = 0;
        UINT kind = 0; // 1 = header detail, 2 = default column state.
        UINT column = 0;
        SHCOLSTATEF state{};
        HRESULT result = E_PENDING;
        WCHAR text[64]{};
    };
    SyntheticColumnMetadataTraceEntry g_syntheticColumnMetadataTrace[kSyntheticColumnMetadataTraceCapacity]{};
    volatile LONG g_syntheticColumnMetadataTraceCount = 0;
    std::wstring g_syntheticSortBackingPath;

#if !defined(EXPLORERDIRS_NATIVEPROBE)
    bool ReadRegisteredNamespaceString(PCWSTR key, PCWSTR name, std::wstring* text)
    {
        DWORD bytes = 0;
        if (RegGetValueW(HKEY_CURRENT_USER, key, name, RRF_RT_REG_SZ | RRF_NOEXPAND,
            nullptr, nullptr, &bytes) != ERROR_SUCCESS || bytes < sizeof(WCHAR) || bytes > 65536 || bytes % sizeof(WCHAR)) return false;
        try
        {
            std::vector<WCHAR> buffer(bytes / sizeof(WCHAR));
            if (RegGetValueW(HKEY_CURRENT_USER, key, name, RRF_RT_REG_SZ | RRF_NOEXPAND,
                nullptr, buffer.data(), &bytes) != ERROR_SUCCESS || buffer.back() != L'\0') return false;
            text->assign(buffer.data());
            return !text->empty();
        }
        catch (const std::bad_alloc&) { return false; }
    }
    bool TryGetRegisteredNamespaceRoot(REFCLSID classId, std::wstring* path)
    {
        WCHAR className[40]{};
        WCHAR key[128]{};
        if (!StringFromGUID2(classId, className, ARRAYSIZE(className)) ||
            FAILED(StringCchPrintfW(key, ARRAYSIZE(key), L"Software\\Classes\\CLSID\\%s", className))) return false;
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&TryGetRegisteredNamespaceRoot), &module)) return false;
        try
        {
            std::vector<WCHAR> modulePath(32768);
            const auto length = GetModuleFileNameW(module, modulePath.data(), static_cast<DWORD>(modulePath.size()));
            if (!length || length >= modulePath.size()) return false;
            std::wstring owner, adapter, root;
            if (!ReadRegisteredNamespaceString(key, nullptr, &owner) || owner != L"Explorer Dirs profile Shell namespace" ||
                !ReadRegisteredNamespaceString(key, L"ExplorerDirsAdapterPath", &adapter) ||
                CompareStringOrdinal(adapter.c_str(), -1, modulePath.data(), -1, TRUE) != CSTR_EQUAL ||
                !ReadRegisteredNamespaceString(key, L"ExplorerDirsProfileRoot", &root) || PathIsRelativeW(root.c_str())) return false;
            *path = std::move(root);
            return true;
        }
        catch (const std::bad_alloc&) { return false; }
    }
#endif
    bool TryGetSyntheticRootBackingPath(REFCLSID classId, std::wstring* path)
    {
        if (!path) return false;
        std::lock_guard<std::mutex> lock(g_syntheticRootConfigurationMutex);
        const auto found = g_syntheticRootBackingPaths.find(classId);
        if (found == g_syntheticRootBackingPaths.end())
        {
#if !defined(EXPLORERDIRS_NATIVEPROBE)
            // Shell notification/metadata consumers load the registered DLL in another process.
            // They cannot inherit ConfigureShellNamespaceRoot's application-local map.
            return TryGetRegisteredNamespaceRoot(classId, path);
#else
            return false;
#endif
        }
        try { *path = found->second; }
        catch (const std::bad_alloc&) { return false; }
        return !path->empty();
    }

    void RecordSyntheticColumnMetadata(UINT kind, UINT column, SHCOLSTATEF state, HRESULT result, const WCHAR* text)
    {
#if defined(EXPLORERDIRS_NATIVEPROBE)
        const auto traceIndex = InterlockedIncrement(&g_syntheticColumnMetadataTraceCount) - 1;
        if (traceIndex >= kSyntheticColumnMetadataTraceCapacity) return;
        auto& trace = g_syntheticColumnMetadataTrace[traceIndex];
        trace.kind = kind;
        trace.column = column;
        trace.state = state;
        trace.result = result;
        if (text) StringCchCopyW(trace.text, ARRAYSIZE(trace.text), text);
        InterlockedExchange(&trace.sequence, traceIndex + 1);
#else
        (void)kind;
        (void)column;
        (void)state;
        (void)result;
        (void)text;
#endif
    }

    const SyntheticChild* SyntheticData(PCUITEMID_CHILD item)
    {
        if (!item || item->mkid.cb != sizeof(SyntheticChild)) return nullptr;
        const auto* child = reinterpret_cast<const SyntheticChild*>(item);
        return child->id != 0 ? child : nullptr;
    }

    constexpr DWORD kSyntheticRootPathPidlMagic = 0x54525044; // "DPRT"; isolated root-path identity marker.
    constexpr size_t kSyntheticRootPathChunkCharacters = 1024;
#pragma pack(push, 1)
    struct SyntheticRootPathPidlItem
    {
        USHORT cb;
        DWORD magic;
        UINT segmentIndex;
        UINT segmentCount;
        WCHAR text[1];
    };
#pragma pack(pop)
    constexpr size_t kSyntheticRootPathPidlHeaderSize = offsetof(SyntheticRootPathPidlItem, text);

    bool IsSyntheticRootPathMagic(DWORD magic)
    {
        return magic == kSyntheticRootPathPidlMagic
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
            || magic == 0x32525044 // DPR2: experiment-only immutable size-sort policy.
#endif
            ;
    }

    size_t SyntheticRootPathTextOffset(const SyntheticRootPathPidlItem* item)
    {
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
        if (item->magic == 0x32525044) return kSyntheticRootPathPidlHeaderSize + sizeof(DWORD);
#else
        (void)item;
#endif
        return kSyntheticRootPathPidlHeaderSize;
    }

    std::wstring EncodeSyntheticRootPathParsingToken(PCWSTR path)
    {
        constexpr WCHAR prefix[] = L"edpath:";
        constexpr WCHAR digits[] = L"0123456789ABCDEF";
        std::wstring token(prefix);
        for (const auto* current = path; current && *current; ++current)
        {
            const auto value = static_cast<USHORT>(*current);
            token.push_back(digits[(value >> 12) & 0xF]);
            token.push_back(digits[(value >> 8) & 0xF]);
            token.push_back(digits[(value >> 4) & 0xF]);
            token.push_back(digits[value & 0xF]);
        }
        return token;
    }

    HRESULT DecodeSyntheticRootPathParsingToken(PCWSTR token, std::wstring* path)
    {
        constexpr WCHAR prefix[] = L"edpath:";
        if (!token || !path || _wcsnicmp(token, prefix, ARRAYSIZE(prefix) - 1) != 0) return S_FALSE;
        const auto length = wcslen(token + ARRAYSIZE(prefix) - 1);
        if (!length || length % 4 != 0) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        try
        {
            path->clear();
            path->reserve(length / 4);
            const auto nibble = [](WCHAR value) -> int
            {
                if (value >= L'0' && value <= L'9') return value - L'0';
                if (value >= L'A' && value <= L'F') return value - L'A' + 10;
                if (value >= L'a' && value <= L'f') return value - L'a' + 10;
                return -1;
            };
            const auto* current = token + ARRAYSIZE(prefix) - 1;
            while (*current)
            {
                USHORT value = 0;
                for (size_t index = 0; index < 4; ++index)
                {
                    const auto digit = nibble(current[index]);
                    if (digit < 0) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    value = static_cast<USHORT>((value << 4) | digit);
                }
                if (value == 0) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                path->push_back(static_cast<WCHAR>(value));
                current += 4;
            }
        }
        catch (const std::bad_alloc&) { path->clear(); return E_OUTOFMEMORY; }
        return path->empty() ? HRESULT_FROM_WIN32(ERROR_INVALID_DATA) : S_OK;
    }

    HRESULT CreateSyntheticRootPathPidl(PCWSTR path, PIDLIST_ABSOLUTE* value)
    {
        if (!path || !*path || !value) return E_INVALIDARG;
        *value = nullptr;
        const auto pathLength = wcslen(path);
        const auto segmentCount = (pathLength + kSyntheticRootPathChunkCharacters - 1) /
            kSyntheticRootPathChunkCharacters;
        if (!segmentCount || segmentCount > 64) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        size_t byteCount = sizeof(USHORT);
        for (size_t index = 0; index < segmentCount; ++index)
        {
            const auto start = index * kSyntheticRootPathChunkCharacters;
            const auto characters = (std::min)(kSyntheticRootPathChunkCharacters, pathLength - start);
            const auto itemBytes = kSyntheticRootPathPidlHeaderSize + characters * sizeof(WCHAR);
            if (itemBytes > USHRT_MAX || byteCount > SIZE_MAX - itemBytes)
                return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
            byteCount += itemBytes;
        }
        auto* bytes = static_cast<BYTE*>(CoTaskMemAlloc(byteCount));
        if (!bytes) return E_OUTOFMEMORY;
        auto* cursor = bytes;
        for (size_t index = 0; index < segmentCount; ++index)
        {
            const auto start = index * kSyntheticRootPathChunkCharacters;
            const auto characters = (std::min)(kSyntheticRootPathChunkCharacters, pathLength - start);
            const auto itemBytes = kSyntheticRootPathPidlHeaderSize + characters * sizeof(WCHAR);
            SyntheticRootPathPidlItem item{};
            item.cb = static_cast<USHORT>(itemBytes);
            item.magic = kSyntheticRootPathPidlMagic;
            item.segmentIndex = static_cast<UINT>(index);
            item.segmentCount = static_cast<UINT>(segmentCount);
            CopyMemory(cursor, &item, kSyntheticRootPathPidlHeaderSize);
            CopyMemory(cursor + kSyntheticRootPathPidlHeaderSize, path + start, characters * sizeof(WCHAR));
            cursor += itemBytes;
        }
        ZeroMemory(cursor, sizeof(USHORT));
        *value = reinterpret_cast<PIDLIST_ABSOLUTE>(bytes);
        return S_OK;
    }

    HRESULT ReadSyntheticRootPathFromPidl(PCIDLIST_ABSOLUTE value, std::wstring* path, bool includeChildren = false)
    {
        if (!value || !path) return E_POINTER;
        path->clear();
        for (auto* current = reinterpret_cast<PCUIDLIST_RELATIVE>(value); current->mkid.cb != 0; current = ILNext(current))
        {
            if (current->mkid.cb < kSyntheticRootPathPidlHeaderSize) continue;
            const auto* first = reinterpret_cast<const SyntheticRootPathPidlItem*>(current);
            if (!IsSyntheticRootPathMagic(first->magic)) continue;
            if (first->segmentIndex != 0 || first->segmentCount == 0 || first->segmentCount > 64)
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            try
            {
                auto* segment = current;
                for (UINT index = 0; index < first->segmentCount; ++index)
                {
                    if (segment->mkid.cb < kSyntheticRootPathPidlHeaderSize)
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    const auto* header = reinterpret_cast<const SyntheticRootPathPidlItem*>(segment);
                    const auto textOffset = SyntheticRootPathTextOffset(header);
                    if (segment->mkid.cb <= textOffset) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    const auto textBytes = segment->mkid.cb - textOffset;
                    if (header->magic != first->magic || header->segmentIndex != index ||
                        header->segmentCount != first->segmentCount || textBytes == 0 || textBytes % sizeof(WCHAR) != 0)
                        return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    const auto characters = textBytes / sizeof(WCHAR);
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
                    if (header->magic == 0x32525044)
                    {
                        DWORD policy = 0;
                        CopyMemory(&policy, reinterpret_cast<const BYTE*>(header) + kSyntheticRootPathPidlHeaderSize, sizeof(policy));
                        DWORD firstPolicy = 0;
                        CopyMemory(&firstPolicy, reinterpret_cast<const BYTE*>(first) + kSyntheticRootPathPidlHeaderSize, sizeof(firstPolicy));
                        if (policy > 3 || policy != firstPolicy) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                    }
#endif
                    path->append(reinterpret_cast<const WCHAR*>(reinterpret_cast<const BYTE*>(header) + textOffset), characters);
                    segment = ILNext(segment);
                }
                if (includeChildren)
                {
                    // A bound absolute PIDL can contain real child segments after the encoded root.
                    // Resolve the complete physical folder, rather than reopening the encoded ancestor.
                    for (; !ILIsEmpty(segment); segment = ILNext(segment))
                    {
                        const auto* child = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(segment));
                        if (!child) return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                        if (!path->empty() && path->back() != L'\\') path->push_back(L'\\');
                        path->append(child->name);
                    }
                }
            }
            catch (const std::bad_alloc&) { path->clear(); return E_OUTOFMEMORY; }
            return path->empty() || path->find(L'\0') != std::wstring::npos
                ? HRESULT_FROM_WIN32(ERROR_INVALID_DATA) : S_OK;
        }
        return S_FALSE;
    }

    ULONGLONG SyntheticSize(const SyntheticChild* child)
    {
        if (child && InterlockedCompareExchange(&g_syntheticCustomSizeColumnForProbe, 0, 0) &&
            _wcsicmp(child->name, L"child") == 0)
        {
            const auto overrideSize = InterlockedCompareExchange64(&g_syntheticChildSizeOverride, -1, -1);
            if (overrideSize >= 0) return static_cast<ULONGLONG>(overrideSize);
        }
        return child ? child->size : 0;
    }

    std::wstring SyntheticContentsText(ULONGLONG files, ULONGLONG folders)
    {
        return std::to_wstring(files) + (files == 1 ? L" file, " : L" files, ") +
            std::to_wstring(folders) + (folders == 1 ? L" folder" : L" folders");
    }

    std::wstring SyntheticContentsText(const SyntheticChild* child)
    {
        if (!child) return {};
        return SyntheticContentsText(child->fileCount, child->folderCount);
    }

    struct SyntheticDirectoryMetrics
    {
        ULONGLONG bytes;
        ULONGLONG files;
        ULONGLONG folders;
        ULONGLONG unreadable;
        bool sizeAvailable = true;
        bool countsAvailable = true;
    };

    HRESULT GetSyntheticDirectoryMetrics(const std::wstring& directory, SyntheticDirectoryMetrics* metrics,
        volatile LONG* cancellationFlag = nullptr, LONG providerGeneration = -1)
    {
        ED_PROBE_METRIC_INCREMENT(g_syntheticDirectoryMetricsCallCount);
        if (g_syntheticCompareCallbackActive)
            ED_PROBE_METRIC_INCREMENT(g_syntheticDirectoryMetricsDuringCompareCount);
        if (!metrics) return E_POINTER;
        *metrics = {};
        if (providerGeneration == -1) providerGeneration = InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0);
        if (FolderSizeScanCancelled(cancellationFlag, providerGeneration))
            return HRESULT_FROM_WIN32(ERROR_CANCELLED);
        std::wstring extendedDirectory, pattern;
        auto pathResult = GetExtendedFilesystemPath(directory, &extendedDirectory);
        if (FAILED(pathResult)) return pathResult;
        try { pattern = extendedDirectory + L"\\*"; }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }

        WIN32_FIND_DATAW entry{};
        const auto search = FindFirstFileW(pattern.c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE)
        {
            const auto error = GetLastError();
            if (error == ERROR_ACCESS_DENIED)
            {
                metrics->unreadable = 1;
                return S_OK;
            }
            return HRESULT_FROM_WIN32(error);
        }
        HRESULT result = S_OK;
        bool hasEntry = true;
        while (hasEntry)
        {
            if (FolderSizeScanCancelled(cancellationFlag, providerGeneration))
            {
                result = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                break;
            }
            if (wcscmp(entry.cFileName, L".") != 0 && wcscmp(entry.cFileName, L"..") != 0)
            {
                ED_PROBE_METRIC_INCREMENT(g_syntheticDirectoryMetricsEntryCount);
                const auto entryDelayMs = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeEntryDelayMsForProbe, 0, 0);
                if (entryDelayMs > 0) Sleep(static_cast<DWORD>(entryDelayMs));
                if (FolderSizeScanCancelled(cancellationFlag, providerGeneration))
                {
                    result = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                    break;
                }
                ULONGLONG entrySize = (static_cast<ULONGLONG>(entry.nFileSizeHigh) << 32) | entry.nFileSizeLow;
                if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
                {
                    if (entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)
                    {
                        // The shared scanner does not follow reparse directories and reports them as skipped.
                        if (metrics->unreadable == ~static_cast<ULONGLONG>(0))
                        {
                            result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                            break;
                        }
                        ++metrics->unreadable;
                    }
                    else
                    {
                        std::wstring childPath;
                        try { childPath = directory + L"\\" + entry.cFileName; }
                        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; break; }
                        SyntheticDirectoryMetrics nested{};
                        result = GetSyntheticDirectoryMetrics(childPath, &nested, cancellationFlag, providerGeneration);
                        if (result == HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED))
                        {
                            if (metrics->unreadable == ~static_cast<ULONGLONG>(0))
                            {
                                result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                                break;
                            }
                            ++metrics->unreadable;
                            result = S_OK;
                            nested = {};
                        }
                        if (FAILED(result)) break;
                        entrySize = nested.bytes;
                        if (~static_cast<ULONGLONG>(0) - metrics->files < nested.files ||
                            ~static_cast<ULONGLONG>(0) - metrics->folders <= nested.folders ||
                            ~static_cast<ULONGLONG>(0) - metrics->unreadable < nested.unreadable)
                        {
                            result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                            break;
                        }
                        metrics->files += nested.files;
                        metrics->folders += nested.folders + 1;
                        metrics->unreadable += nested.unreadable;
                    }
                }
                else if (!(entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (metrics->files == ~static_cast<ULONGLONG>(0))
                    {
                        result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                        break;
                    }
                    ++metrics->files;
                }
                if (~static_cast<ULONGLONG>(0) - metrics->bytes < entrySize)
                {
                    result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                    break;
                }
                metrics->bytes += entrySize;
            }
            hasEntry = FindNextFileW(search, &entry) != FALSE;
            if (!hasEntry)
            {
                const DWORD error = GetLastError();
                if (error == ERROR_ACCESS_DENIED)
                {
                    if (metrics->unreadable == ~static_cast<ULONGLONG>(0))
                        result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                    else ++metrics->unreadable;
                }
                else if (error != ERROR_NO_MORE_FILES) result = HRESULT_FROM_WIN32(error);
            }
            if (FAILED(result)) break;
        }
        FindClose(search);
        if (FAILED(result)) *metrics = {};
        return result;
    }

    HRESULT GetConfiguredDirectoryMetrics(const std::wstring& directory, SyntheticDirectoryMetrics* metrics,
        volatile LONG* cancellationFlag = nullptr, LONG expectedGeneration = -1)
    {
        if (!metrics) return E_POINTER;
        *metrics = {};
        const auto generation = expectedGeneration == -1 ? InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0) : expectedGeneration;
        if (FolderSizeScanCancelled(cancellationFlag, generation))
            return HRESULT_FROM_WIN32(ERROR_CANCELLED);
        AcquireSRWLockShared(&g_folderSizeProviderLock);
        if (FolderSizeScanCancelled(cancellationFlag, generation))
        {
            ReleaseSRWLockShared(&g_folderSizeProviderLock);
            return HRESULT_FROM_WIN32(ERROR_CANCELLED);
        }
        const auto callback = reinterpret_cast<FolderSizeProviderCallback>(
            InterlockedCompareExchangePointer(&g_folderSizeProviderCallback, nullptr, nullptr));
        if (callback)
        {
            DWORD flags = 0;
            const auto result = callback(directory.c_str(), cancellationFlag, &metrics->bytes,
                &metrics->files, &metrics->folders, &metrics->unreadable, &flags);
            ReleaseSRWLockShared(&g_folderSizeProviderLock);
            if (FolderSizeScanCancelled(cancellationFlag, generation)) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
            if (result != E_NOTIMPL)
            {
                metrics->sizeAvailable = (flags & 1) != 0;
                metrics->countsAvailable = (flags & 2) != 0;
                return result;
            }
        }
        else ReleaseSRWLockShared(&g_folderSizeProviderLock);
        return GetSyntheticDirectoryMetrics(directory, metrics, cancellationFlag, generation);
    }

    HRESULT EnumerateBackingChildren(const std::wstring& backingPath, SHCONTF flags,
        std::vector<SyntheticChild>* children)
    {
        if (!children) return E_POINTER;
        children->clear();
        if (backingPath.empty()) return S_OK;
        std::wstring pattern;
        try { pattern = backingPath + L"\\*"; }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        WIN32_FIND_DATAW entry{};
        const auto search = FindFirstFileW(pattern.c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        HRESULT result = S_OK;
        bool hasEntry = true;
        try
        {
            while (hasEntry)
            {
                const bool isFolder = (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                const bool isHidden = (entry.dwFileAttributes & FILE_ATTRIBUTE_HIDDEN) != 0;
                const bool isSuperHidden = isHidden && (entry.dwFileAttributes & FILE_ATTRIBUTE_SYSTEM) != 0;
                if ((isFolder && !(flags & SHCONTF_FOLDERS)) || (!isFolder && !(flags & SHCONTF_NONFOLDERS)) ||
                    (isSuperHidden && !(flags & SHCONTF_INCLUDESUPERHIDDEN)) ||
                    (isHidden && !(flags & (SHCONTF_INCLUDEHIDDEN | SHCONTF_INCLUDESUPERHIDDEN))) ||
                    wcscmp(entry.cFileName, L".") == 0 || wcscmp(entry.cFileName, L"..") == 0 ||
                    wcslen(entry.cFileName) >= ARRAYSIZE(SyntheticChild::name))
                {
                    hasEntry = FindNextFileW(search, &entry) != FALSE;
                    if (!hasEntry)
                    {
                        const DWORD error = GetLastError();
                        if (error != ERROR_NO_MORE_FILES) result = HRESULT_FROM_WIN32(error);
                    }
                    if (FAILED(result)) break;
                    continue;
                }
                SyntheticChild child{};
                child.cb = sizeof(SyntheticChild);
                child.id = static_cast<USHORT>(children->size() + 1);
                child.size = (static_cast<ULONGLONG>(entry.nFileSizeHigh) << 32) | entry.nFileSizeLow;
                if (isFolder && !isSuperHidden && !(entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT))
                {
                    std::wstring childPath;
                    try { childPath = backingPath + L"\\" + entry.cFileName; }
                    catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; break; }
                    TryGetSyntheticFileIdentity(childPath.c_str(), &child.identity);
                    if (InterlockedCompareExchange(&g_syntheticRecursiveSizeScanEnabledForProbe, 0, 0) &&
                        !InterlockedCompareExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0, 0))
                    {
                        SyntheticDirectoryMetrics metrics{};
                        result = GetSyntheticDirectoryMetrics(childPath, &metrics);
                        if (FAILED(result)) break;
                        child.size = metrics.bytes;
                        child.fileCount = metrics.files;
                        child.folderCount = metrics.folders;
                    }
                }
                child.attributes = entry.dwFileAttributes;
                wcscpy_s(child.name, entry.cFileName);
                children->push_back(child);
                hasEntry = FindNextFileW(search, &entry) != FALSE;
                if (!hasEntry)
                {
                    const DWORD error = GetLastError();
                    if (error != ERROR_NO_MORE_FILES) result = HRESULT_FROM_WIN32(error);
                }
                if (FAILED(result)) break;
            }
        }
        catch (...)
        {
            FindClose(search);
            throw;
        }
        FindClose(search);
        if (FAILED(result))
        {
            children->clear();
            return result;
        }
        ULONGLONG parentBytes = 0;
        for (const auto& child : *children)
        {
            if (~static_cast<ULONGLONG>(0) - parentBytes < child.size)
            {
                children->clear();
                return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
            }
            parentBytes += child.size;
        }
        for (auto& child : *children) child.parentBytes = parentBytes;
        std::sort(children->begin(), children->end(), [](const SyntheticChild& left, const SyntheticChild& right)
        {
            return _wcsicmp(left.name, right.name) < 0;
        });
        for (size_t index = 0; index < children->size(); ++index)
            (*children)[index].id = static_cast<USHORT>(index + 1);
        return S_OK;
    }

    PIDLIST_RELATIVE CloneSyntheticChild(const SyntheticChild& child)
    {
        const auto bytes = sizeof(SyntheticChild) + sizeof(USHORT);
        auto* copy = static_cast<BYTE*>(CoTaskMemAlloc(bytes));
        if (!copy) return nullptr;
        CopyMemory(copy, &child, sizeof(child));
        ZeroMemory(copy + sizeof(child), sizeof(USHORT));
        return reinterpret_cast<PITEMID_CHILD>(copy);
    }

    HRESULT TransferSyntheticSelectionWithoutUi(IDataObject* data, const std::wstring& backingPath, bool move)
    {
        if (!data) return E_INVALIDARG;
        const auto destinationPath = backingPath + (move ? L"\\move-destination" : L"\\copy-destination");
        if (!CreateDirectoryW(destinationPath.c_str(), nullptr)) return HRESULT_FROM_WIN32(GetLastError());

        PIDLIST_ABSOLUTE destinationPidl = nullptr;
        auto result = SHParseDisplayName(destinationPath.c_str(), nullptr, &destinationPidl, 0, nullptr);
        IShellItem* destination = nullptr;
        if (SUCCEEDED(result)) result = SHCreateItemFromIDList(destinationPidl, IID_PPV_ARGS(&destination));
        if (destinationPidl) CoTaskMemFree(destinationPidl);

        IFileOperation* operation = nullptr;
        if (SUCCEEDED(result))
            result = CoCreateInstance(CLSID_FileOperation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(&operation));
        if (SUCCEEDED(result))
            result = operation->SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI);
        if (SUCCEEDED(result))
            result = move ? operation->MoveItems(data, destination) : operation->CopyItems(data, destination);
        if (SUCCEEDED(result)) result = operation->PerformOperations();
        if (SUCCEEDED(result))
        {
            BOOL aborted = FALSE;
            result = operation->GetAnyOperationsAborted(&aborted);
            if (SUCCEEDED(result) && aborted) result = E_ABORT;
        }
        if (operation) operation->Release();
        if (destination) destination->Release();
        if (FAILED(result)) return result;

        const auto verifyCopiedContents = [&destinationPath](PCWSTR name, const char* expected)
        {
            const auto path = destinationPath + L"\\" + name;
            const auto file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
                OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (file == INVALID_HANDLE_VALUE) return false;
            char actual[16]{};
            DWORD bytesRead = 0;
            const auto expectedLength = static_cast<DWORD>(strlen(expected));
            const bool matches = ReadFile(file, actual, sizeof(actual), &bytesRead, nullptr) &&
                bytesRead == expectedLength && memcmp(actual, expected, expectedLength) == 0;
            CloseHandle(file);
            return matches;
        };
        const bool sourceRootExists = GetFileAttributesW((backingPath + L"\\root.txt").c_str()) != INVALID_FILE_ATTRIBUTES;
        const bool sourceSecondExists = GetFileAttributesW((backingPath + L"\\second.txt").c_str()) != INVALID_FILE_ATTRIBUTES;
        const bool sourceStateCorrect = move ? !sourceRootExists && !sourceSecondExists : sourceRootExists && sourceSecondExists;
        return sourceStateCorrect && verifyCopiedContents(L"root.txt", "root") &&
            verifyCopiedContents(L"second.txt", "second") ? S_OK : E_FAIL;
    }

    HRESULT CopySyntheticSelectionCollisionWithoutUi(IDataObject* data, const std::wstring& backingPath)
    {
        if (!data) return E_INVALIDARG;
        const auto destinationPath = backingPath + L"\\collision-destination";
        if (!CreateDirectoryW(destinationPath.c_str(), nullptr)) return HRESULT_FROM_WIN32(GetLastError());
        const auto existingPath = destinationPath + L"\\root.txt";
        const auto existing = CreateFileW(existingPath.c_str(), GENERIC_WRITE, 0, nullptr,
            CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (existing == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        constexpr char existingContents[] = "keep-existing";
        DWORD bytesWritten = 0;
        const bool wroteExisting = WriteFile(existing, existingContents, sizeof(existingContents) - 1,
            &bytesWritten, nullptr) && bytesWritten == sizeof(existingContents) - 1;
        const DWORD writeError = wroteExisting ? ERROR_SUCCESS : GetLastError();
        CloseHandle(existing);
        if (!wroteExisting) return HRESULT_FROM_WIN32(writeError ? writeError : ERROR_WRITE_FAULT);

        PIDLIST_ABSOLUTE destinationPidl = nullptr;
        auto result = SHParseDisplayName(destinationPath.c_str(), nullptr, &destinationPidl, 0, nullptr);
        IShellItem* destination = nullptr;
        if (SUCCEEDED(result)) result = SHCreateItemFromIDList(destinationPidl, IID_PPV_ARGS(&destination));
        if (destinationPidl) CoTaskMemFree(destinationPidl);

        IFileOperation* operation = nullptr;
        if (SUCCEEDED(result))
            result = CoCreateInstance(CLSID_FileOperation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(&operation));
        if (SUCCEEDED(result))
            result = operation->SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_RENAMEONCOLLISION);
        if (SUCCEEDED(result)) result = operation->CopyItems(data, destination);
        if (SUCCEEDED(result)) result = operation->PerformOperations();
        if (SUCCEEDED(result))
        {
            BOOL aborted = FALSE;
            result = operation->GetAnyOperationsAborted(&aborted);
            if (SUCCEEDED(result) && aborted) result = E_ABORT;
        }
        if (operation) operation->Release();
        if (destination) destination->Release();
        if (FAILED(result)) return result;

        const auto existingFile = CreateFileW(existingPath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (existingFile == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        char actualExisting[32]{};
        DWORD existingBytes = 0;
        const bool preserved = ReadFile(existingFile, actualExisting, sizeof(actualExisting), &existingBytes, nullptr) &&
            existingBytes == static_cast<DWORD>(sizeof(existingContents) - 1) &&
            memcmp(actualExisting, existingContents, sizeof(existingContents) - 1) == 0;
        CloseHandle(existingFile);
        if (!preserved) return E_FAIL;

        const auto copiedSecond = destinationPath + L"\\second.txt";
        const auto second = CreateFileW(copiedSecond.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (second == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        char secondContents[16]{};
        DWORD secondBytes = 0;
        const bool copied = ReadFile(second, secondContents, sizeof(secondContents), &secondBytes, nullptr) &&
            secondBytes == 6 && memcmp(secondContents, "second", 6) == 0;
        CloseHandle(second);
        if (!copied) return E_FAIL;

        WIN32_FIND_DATAW entry{};
        const auto search = FindFirstFileW((destinationPath + L"\\root*").c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        bool foundCollisionCopy = false;
        do
        {
            if (!(entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && _wcsicmp(entry.cFileName, L"root.txt") != 0)
            {
                foundCollisionCopy = true;
                break;
            }
        } while (FindNextFileW(search, &entry));
        FindClose(search);
        return foundCollisionCopy ? S_OK : E_FAIL;
    }

    HRESULT DeleteSyntheticCopiedSelectionWithoutUi(IShellFolder2* root, const std::wstring& backingPath)
    {
        if (!root) return E_INVALIDARG;
        PIDLIST_RELATIVE destinationChild = nullptr;
        IShellFolder2* destinationFolder = nullptr;
        IEnumIDList* enumerator = nullptr;
        PITEMID_CHILD selected[2]{};
        IDataObject* data = nullptr;
        IFileOperation* operation = nullptr;

        WCHAR destinationName[] = L"copy-destination";
        auto result = root->ParseDisplayName(nullptr, nullptr, destinationName, nullptr, &destinationChild, nullptr);
        if (SUCCEEDED(result) && !destinationChild) result = E_UNEXPECTED;
        if (SUCCEEDED(result))
            result = root->BindToObject(destinationChild, nullptr, IID_PPV_ARGS(&destinationFolder));
        if (SUCCEEDED(result) && !destinationFolder) result = E_UNEXPECTED;
        if (SUCCEEDED(result))
            result = destinationFolder->EnumObjects(nullptr, SHCONTF_NONFOLDERS, &enumerator);
        if (SUCCEEDED(result) && !enumerator) result = E_UNEXPECTED;
        if (SUCCEEDED(result))
        {
            for (ULONG index = 0; index < ARRAYSIZE(selected); ++index)
            {
                ULONG fetched = 0;
                result = enumerator->Next(1, &selected[index], &fetched);
                const auto* child = SyntheticData(selected[index]);
                const auto* expected = index == 0 ? L"root.txt" : L"second.txt";
                if (result != S_OK || fetched != 1 || !child || _wcsicmp(child->name, expected) != 0)
                {
                    if (SUCCEEDED(result)) result = E_FAIL;
                    break;
                }
            }
        }
        if (SUCCEEDED(result))
        {
            ULONG fetched = 0;
            PITEMID_CHILD extra = nullptr;
            result = enumerator->Next(1, &extra, &fetched);
            if (extra) CoTaskMemFree(extra);
            if (result == S_FALSE && fetched == 0) result = S_OK;
            else if (SUCCEEDED(result)) result = E_FAIL;
        }
        if (SUCCEEDED(result))
        {
            PCUITEMID_CHILD selectedForData[] = { selected[0], selected[1] };
            result = destinationFolder->GetUIObjectOf(nullptr, ARRAYSIZE(selectedForData), selectedForData,
                IID_IDataObject, nullptr, reinterpret_cast<void**>(&data));
        }
        if (SUCCEEDED(result) && !data) result = E_UNEXPECTED;
        if (SUCCEEDED(result))
            result = CoCreateInstance(CLSID_FileOperation, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(&operation));
        if (SUCCEEDED(result))
            result = operation->SetOperationFlags(FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI);
        if (SUCCEEDED(result)) result = operation->DeleteItems(data);
        if (SUCCEEDED(result)) result = operation->PerformOperations();
        if (SUCCEEDED(result))
        {
            BOOL aborted = FALSE;
            result = operation->GetAnyOperationsAborted(&aborted);
            if (SUCCEEDED(result) && aborted) result = E_ABORT;
        }

        if (operation) operation->Release();
        if (data) data->Release();
        if (enumerator) enumerator->Release();
        if (destinationFolder) destinationFolder->Release();
        if (destinationChild) CoTaskMemFree(destinationChild);
        for (auto* child : selected) if (child) CoTaskMemFree(child);
        if (FAILED(result)) return result;

        const auto destinationPath = backingPath + L"\\copy-destination\\";
        return GetFileAttributesW((destinationPath + L"root.txt").c_str()) == INVALID_FILE_ATTRIBUTES &&
            GetFileAttributesW((destinationPath + L"second.txt").c_str()) == INVALID_FILE_ATTRIBUTES
            ? S_OK : E_FAIL;
    }

    class SyntheticChildEnumerator final : public IEnumIDList
    {
    public:
        explicit SyntheticChildEnumerator(std::vector<SyntheticChild> children, ULONG index = 0)
            : _children(std::move(children)), _index(index) { InterlockedIncrement(&g_syntheticLiveObjects); }
        ~SyntheticChildEnumerator() { InterlockedDecrement(&g_syntheticLiveObjects); }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IEnumIDList) return E_NOINTERFACE;
            *value = static_cast<IEnumIDList*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT STDMETHODCALLTYPE Next(ULONG count, PITEMID_CHILD* items, ULONG* fetched) override
        {
            if (!items || (count != 1 && !fetched)) return E_POINTER;
            if (fetched) *fetched = 0;
            ULONG made = 0;
            while (made < count && _index < _children.size())
            {
                items[made] = CloneSyntheticChild(_children[_index]);
                if (!items[made])
                {
                    for (ULONG index = 0; index < made; ++index) CoTaskMemFree(items[index]);
                    return E_OUTOFMEMORY;
                }
                ++made;
                ++_index;
            }
            if (fetched) *fetched = made;
            if (!g_syntheticSizeMetricsObservation)
                ED_PROBE_METRIC_ADD(g_syntheticEnumeratedItems, static_cast<LONG>(made));
            return made == count ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Skip(ULONG count) override
        {
            const auto available = static_cast<ULONG>(_children.size());
            const auto remaining = _index < available ? available - _index : 0;
            const auto skipped = count < remaining ? count : remaining;
            _index += skipped;
            return skipped == count ? S_OK : S_FALSE;
        }
        HRESULT STDMETHODCALLTYPE Reset() override { _index = 0; return S_OK; }
        HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** value) override
        {
            if (!value) return E_POINTER;
            try { *value = new (std::nothrow) SyntheticChildEnumerator(_children, _index); }
            catch (const std::bad_alloc&) { *value = nullptr; }
            return *value ? S_OK : E_OUTOFMEMORY;
        }
    private:
        volatile LONG _references = 1;
        std::vector<SyntheticChild> _children;
        ULONG _index;
    };

#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
    // Per-view capability: a process-global flag would couple tabs visiting the same path.
    struct __declspec(uuid("37A49964-E858-4A39-A411-D552E136921B")) IExplorerDirsSizeSortPolicy : public IUnknown
    {
        virtual HRESULT STDMETHODCALLTYPE SetMixedSizeSort(BOOL mixed) = 0;
    };

#endif
    class SyntheticSortFolder final : public IShellFolder2, public IPersistFolder2, public IShellFolderViewCB
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
        , public IExplorerDirsSizeSortPolicy
#endif
    {
    public:
        explicit SyntheticSortFolder(SyntheticRecursiveSizeSnapshotStore* recursiveSizeStore = nullptr,
            REFCLSID classId = g_syntheticFolderClassId, std::wstring configuredBackingPath = {})
            : _classId(classId), _configuredBackingPath(std::move(configuredBackingPath)),
              _recursiveSizeStore(recursiveSizeStore ? recursiveSizeStore : new (std::nothrow) SyntheticRecursiveSizeSnapshotStore(
                static_cast<size_t>(InterlockedCompareExchange(&g_syntheticRecursiveSizeCacheCapacityForProbe, 0, 0))))
        {
            if (recursiveSizeStore) _recursiveSizeStore->AddRef();
            InterlockedIncrement(&g_syntheticLiveObjects);
        }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid == IID_IUnknown || iid == IID_IShellFolder || iid == IID_IShellFolder2)
                *value = static_cast<IShellFolder2*>(this);
            else if (iid == IID_IPersist || iid == IID_IPersistFolder || iid == IID_IPersistFolder2)
                *value = static_cast<IPersistFolder2*>(this);
            else if (iid == IID_IShellFolderViewCB)
                *value = static_cast<IShellFolderViewCB*>(this);
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
            else if (iid == __uuidof(IExplorerDirsSizeSortPolicy))
                *value = static_cast<IExplorerDirsSizeSortPolicy*>(this);
#endif
            else return E_NOINTERFACE;
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
        HRESULT STDMETHODCALLTYPE SetMixedSizeSort(BOOL mixed) override
        {
            InterlockedExchange(&_mixedSizeSort, mixed ? 1 : 0);
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
            RecordProductCompareDiagnostic(0xFFFF, L"<set-policy>", mixed ? 1 : 0, L"", 0, 0, false, this);
#endif
            return S_OK;
        }

        int CurrentSizeSortDirection() const
        {
            // Identity serialization and comparison can run during lazy view initialization.
            // Never call back into the view here: GetSortColumns reentered GetCurFolder
            // and overflowed the stack in the encoded-root private Desktop probe.
            return _hasEncodedSizeSortPolicy ? _encodedSizeSortDirection : 1;
        }

#endif
        HRESULT SetRecursiveSizeSnapshotForProbe(PCUITEMID_CHILD item, PCWSTR parsingName,
            const SyntheticFileIdentity& expectedIdentity, ULONGLONG bytes, ULONGLONG files = 0,
            ULONGLONG folders = 0, ULONGLONG unreadable = 0)
        {
            const auto* child = SyntheticData(item);
            const SyntheticFileIdentityLess less{};
            if (!child || !parsingName || !*parsingName || !_recursiveSizeStore ||
                !(child->attributes & FILE_ATTRIBUTE_DIRECTORY) || !expectedIdentity.available ||
                less(child->identity, expectedIdentity) || less(expectedIdentity, child->identity)) return E_INVALIDARG;
            std::wstring shellParsingName;
            auto result = GetAbsoluteParsingName(child, &shellParsingName);
            if (FAILED(result)) return result;
            if (CompareStringOrdinal(shellParsingName.c_str(), -1, parsingName, -1, TRUE) != CSTR_EQUAL) return E_INVALIDARG;
            return _recursiveSizeStore->Set(parsingName, child->identity, bytes, files, folders, unreadable);
        }
        size_t RecursiveSizeSnapshotCountForProbe() const
        {
            return _recursiveSizeStore ? _recursiveSizeStore->Count() : 0;
        }
        bool TryGetRecursiveSizeSnapshotForProbe(PCWSTR parsingName,
            const SyntheticFileIdentity& identity, ULONGLONG* bytes, ULONGLONG* files = nullptr,
            ULONGLONG* folders = nullptr, ULONGLONG* unreadable = nullptr) const
        {
            return parsingName && _recursiveSizeStore &&
                _recursiveSizeStore->TryGet(parsingName, identity, bytes, files, folders, unreadable);
        }
        HRESULT PublishRecursiveSizeSnapshotForChildProbe(PCWSTR childName, ULONGLONG bytes,
            ULONGLONG files, ULONGLONG folders, ULONGLONG unreadable)
        {
            if (!childName || !*childName || !_recursiveSizeStore) return E_INVALIDARG;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return result;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            if (SUCCEEDED(result))
                result = SetRecursiveSizeSnapshotForProbe(childItem, parsingName.c_str(), child->identity,
                    bytes, files, folders, unreadable);
            CoTaskMemFree(childItem);
            return result;
        }
        HRESULT ReadRecursiveSizeSnapshotForChildProbe(PCWSTR childName, ULONGLONG* bytes,
            ULONGLONG* files, ULONGLONG* folders, ULONGLONG* unreadable)
        {
            if (!childName || !*childName || !bytes || !files || !folders || !unreadable || !_recursiveSizeStore)
                return E_INVALIDARG;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return result;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            if (SUCCEEDED(result) && !_recursiveSizeStore->TryGet(parsingName, child->identity,
                bytes, files, folders, unreadable)) result = HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
            CoTaskMemFree(childItem);
            return result;
        }
        BOOL HasRecursiveSizeSnapshotForChildProbe(PCWSTR childName)
        {
            if (!childName || !*childName || !_recursiveSizeStore) return FALSE;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return FALSE;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            ULONGLONG bytes = 0;
            const bool exists = SUCCEEDED(result) && _recursiveSizeStore->TryGet(parsingName, child->identity, &bytes);
            CoTaskMemFree(childItem);
            return exists ? TRUE : FALSE;
        }
        BOOL HasRecursiveSizeScanReservationForChildProbe(PCWSTR childName)
        {
            if (!childName || !*childName || !_recursiveSizeStore) return FALSE;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return FALSE;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            const bool reserved = SUCCEEDED(result) && _recursiveSizeStore->HasReservation(parsingName);
            CoTaskMemFree(childItem);
            return reserved ? TRUE : FALSE;
        }
        // Expose queue ownership so the teardown probe can distinguish a gated scan from a cache hit.
        BOOL HasAsyncRecursiveSizeScanPendingForChildProbe(PCWSTR childName)
        {
            if (!childName || !*childName) return FALSE;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return FALSE;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            bool pending = false;
            if (SUCCEEDED(result))
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                pending = _asyncSizeScansPending.find(parsingName) != _asyncSizeScansPending.end();
            }
            CoTaskMemFree(childItem);
            return pending ? TRUE : FALSE;
        }
        HRESULT InvalidateRecursiveSizeScanForChildProbe(PCWSTR childName)
        {
            if (!childName || !*childName || !_recursiveSizeStore) return E_INVALIDARG;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return result;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            const bool invalidated = SUCCEEDED(result) && _recursiveSizeStore->InvalidateReservedPathExact(parsingName.c_str());
            CoTaskMemFree(childItem);
            return FAILED(result) ? result : (invalidated ? S_OK : S_FALSE);
        }
        HRESULT InvalidateRecursiveSizeSnapshotForChildProbe(PCWSTR childName)
        {
            if (!childName || !*childName || !_recursiveSizeStore) return E_INVALIDARG;
            PITEMID_CHILD childItem = nullptr;
            auto result = ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(childName), nullptr, &childItem, nullptr);
            if (FAILED(result)) return result;
            const auto* child = SyntheticData(childItem);
            std::wstring parsingName;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) result = E_INVALIDARG;
            else result = GetAbsoluteParsingName(child, &parsingName);
            const bool invalidated = SUCCEEDED(result) && _recursiveSizeStore->InvalidateSnapshotPathExact(parsingName.c_str());
            CoTaskMemFree(childItem);
            return FAILED(result) ? result : (invalidated ? S_OK : S_FALSE);
        }
        HRESULT STDMETHODCALLTYPE GetClassID(CLSID* value) override
        {
            if (!value) return E_POINTER;
            *value = _classId;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE value) override
        {
            TraceSyntheticSortStage("synthetic-sort: Initialize-enter");
            if (!value) return E_INVALIDARG;
            if (!_recursiveSizeStore) return E_OUTOFMEMORY;
            auto* copy = ILCloneFull(value);
            if (!copy) return E_OUTOFMEMORY;
            std::wstring backingPath;
            auto result = ReadSyntheticRootPathFromPidl(value, &backingPath, true);
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
            _hasEncodedSizeSortPolicy = false;
            for (auto* part = reinterpret_cast<PCUIDLIST_RELATIVE>(value); !ILIsEmpty(part); part = ILNext(part))
            {
                if (part->mkid.cb < kSyntheticRootPathPidlHeaderSize + sizeof(DWORD)) continue;
                const auto* header = reinterpret_cast<const SyntheticRootPathPidlItem*>(part);
                if (header->magic != 0x32525044) continue;
                DWORD policy = 0;
                CopyMemory(&policy, reinterpret_cast<const BYTE*>(header) + kSyntheticRootPathPidlHeaderSize, sizeof(policy));
                InterlockedExchange(&_mixedSizeSort, (policy & 1) ? 1 : 0);
                _encodedSizeSortDirection = (policy & 2) ? -1 : 1;
                _hasEncodedSizeSortPolicy = true;
                break;
            }
#endif
            if (result == S_FALSE)
            {
                try { backingPath = _configuredBackingPath.empty() ? g_syntheticSortBackingPath : _configuredBackingPath; }
                catch (const std::bad_alloc&) { CoTaskMemFree(copy); return E_OUTOFMEMORY; }
                if (backingPath.empty()) { CoTaskMemFree(copy); return E_UNEXPECTED; }
            }
            else if (FAILED(result)) { CoTaskMemFree(copy); return result; }
            PIDLIST_ABSOLUTE backingPidl = nullptr;
            TraceSyntheticSortStage("synthetic-sort: backing-SHParseDisplayName-enter");
            result = SHParseDisplayName(backingPath.c_str(), nullptr, &backingPidl, 0, nullptr);
            TraceSyntheticSortStage("synthetic-sort: backing-SHParseDisplayName-return");
            if (FAILED(result)) { CoTaskMemFree(copy); return result; }
            auto* backingRoot = ILCloneFull(backingPidl);
            if (!backingRoot) { CoTaskMemFree(backingPidl); CoTaskMemFree(copy); return E_OUTOFMEMORY; }
            IShellFolder* parent = nullptr;
            PCUITEMID_CHILD child = nullptr;
            TraceSyntheticSortStage("synthetic-sort: backing-SHBindToParent-enter");
            result = SHBindToParent(backingPidl, IID_PPV_ARGS(&parent), &child);
            TraceSyntheticSortStage("synthetic-sort: backing-SHBindToParent-return");
            if (FAILED(result)) { CoTaskMemFree(backingRoot); CoTaskMemFree(backingPidl); CoTaskMemFree(copy); return result; }
            IShellFolder2* backingFolder = nullptr;
            TraceSyntheticSortStage("synthetic-sort: backing-folder-BindToObject-enter");
            result = parent->BindToObject(child, nullptr, IID_PPV_ARGS(&backingFolder));
            parent->Release();
            CoTaskMemFree(backingPidl);
            TraceSyntheticSortStage("synthetic-sort: backing-folder-BindToObject-return");
            if (FAILED(result)) { CoTaskMemFree(backingRoot); CoTaskMemFree(copy); return result; }
            if (_root) CoTaskMemFree(_root);
            _root = copy;
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
            if (_viewNotificationRoot) CoTaskMemFree(_viewNotificationRoot);
            _viewNotificationRoot = nullptr;
#endif
            if (_backingRoot) CoTaskMemFree(_backingRoot);
            _backingRoot = backingRoot;
            if (_inner) _inner->Release();
            _inner = backingFolder;
            try { _backingPath = std::move(backingPath); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetCurFolder(PIDLIST_ABSOLUTE* value) override
        {
            if (!value) return E_POINTER;
            if (!_root) return E_UNEXPECTED;
            *value = ILCloneFull(_root);
            if (!*value) return E_OUTOFMEMORY;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE MessageSFVCB(UINT message, WPARAM wParam, LPARAM lParam) override
        {
            ED_SIZE_TRACE("view-callback", this, nullptr, message, static_cast<DWORD>(lParam), 0);
            if (message == SFVM_GETNOTIFY) TraceSyntheticSortStage("synthetic-sort: SFVM_GETNOTIFY");
            if (message == SFVM_FSNOTIFY)
            {
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                InterlockedIncrement(&g_productViewItemNotificationCallbacks);
                if (wParam)
                {
                    auto* callbackPidls = reinterpret_cast<PCIDLIST_ABSOLUTE*>(wParam);
                    RecordProductViewItemNotificationIdentity(callbackPidls[0]);
                    const auto callbackEvent = static_cast<LONG>(lParam) & ~SHCNE_INTERRUPT;
                    if (callbackEvent == SHCNE_RENAMEITEM || callbackEvent == SHCNE_RENAMEFOLDER)
                        RecordProductViewItemNotificationIdentity(callbackPidls[1]);
                }
#endif
                char notification[160]{};
                sprintf_s(notification, "synthetic-sort: SFVM_FSNOTIFY; wParam=0x%p; lParam=0x%llX",
                    reinterpret_cast<void*>(wParam), static_cast<unsigned long long>(static_cast<UINT_PTR>(lParam)));
                TraceSyntheticSortStage(notification);
                const auto event = static_cast<LONG>(lParam) & ~SHCNE_INTERRUPT;
                if (wParam && InterlockedCompareExchange(&g_syntheticTraceNotificationPidlsForProbe, 0, 0))
                {
                    auto* notifiedPidls = reinterpret_cast<PCIDLIST_ABSOLUTE*>(wParam);
                    TraceSyntheticNotificationPidl(event, 0, notifiedPidls[0]);
                    if (event == SHCNE_RENAMEITEM || event == SHCNE_RENAMEFOLDER)
                        TraceSyntheticNotificationPidl(event, 1, notifiedPidls[1]);
                }
                const bool affectsRecursiveSize = event == SHCNE_CREATE || event == SHCNE_DELETE ||
                    event == SHCNE_MKDIR || event == SHCNE_RMDIR || event == SHCNE_RENAMEITEM ||
                    event == SHCNE_RENAMEFOLDER || event == SHCNE_UPDATEITEM || event == SHCNE_UPDATEDIR;
                const bool removesFolderSubtree = event == SHCNE_RMDIR || event == SHCNE_RENAMEFOLDER;
                if (_recursiveSizeStore && wParam && affectsRecursiveSize)
                {
                    auto* pidls = reinterpret_cast<PCIDLIST_ABSOLUTE*>(wParam);
                    const auto invalidate = [&](PCIDLIST_ABSOLUTE pidl)
                    {
                        if (!pidl) return;
                        LPWSTR parsingName = nullptr;
                        if (SUCCEEDED(SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, &parsingName)) && parsingName)
                        {
                            size_t invalidatedReservations = 0;
                            const auto removedSnapshots = _recursiveSizeStore->InvalidatePath(
                                parsingName, removesFolderSubtree, &invalidatedReservations);
                            if (invalidatedReservations)
                                ED_PROBE_METRIC_ADD(g_syntheticAsyncRecursiveSizeNotificationInvalidatedReservationsForProbe, static_cast<LONG>(invalidatedReservations));
                            char invalidationTrace[256]{};
                            sprintf_s(invalidationTrace,
                                "synthetic-sort: SFVM_FSNOTIFY cache-invalidation; removed-snapshots=%llu; invalidated-reservations=%llu",
                                static_cast<unsigned long long>(removedSnapshots),
                                static_cast<unsigned long long>(invalidatedReservations));
                            TraceSyntheticSortStage(invalidationTrace);
                            CoTaskMemFree(parsingName);
                        }
                    };
                    invalidate(pidls[0]);
                    if (event == SHCNE_RENAMEITEM || event == SHCNE_RENAMEFOLDER) invalidate(pidls[1]);
                }
#if defined(EXPLORERDIRS_NATIVEPROBE)
                // Legacy prototype timers depend on its held-view table and can
                // broadcast full-directory updates. Product views forward item IDs
                // directly and must not enter this path during automatic soft updates.
                if (InterlockedCompareExchange(&g_syntheticTranslateChildNotificationsForProbe, 0, 0) &&
                    ShouldTranslateSyntheticChildNotification(static_cast<LONG>(lParam)))
                {
                    if (lParam == SHCNE_ATTRIBUTES)
                        ED_PROBE_METRIC_INCREMENT(g_syntheticAttributeNotificationTranslationCount);
                    const auto scheduleResult = ScheduleBackingDirectoryUpdate();
                    if (FAILED(scheduleResult)) TraceSyntheticSortStage("synthetic-sort: child-notification-parent-update-schedule-failed");
                }
#endif
                // E_NOTIMPL lets DefView continue its default filesystem update handling.
                return E_NOTIMPL;
            }
            if (message == SFVM_SETISFV)
            {
                TraceSyntheticSortStage("synthetic-sort: SFVM_SETISFV");
                return lParam ? S_OK : E_POINTER;
            }
            if (message == SFVM_WINDOWCREATED)
            {
                TraceSyntheticSortStage("synthetic-sort: SFVM_WINDOWCREATED");
#if defined(EXPLORERDIRS_SIZE_TRACE_EXPERIMENT)
                TraceSizeViewChild(reinterpret_cast<HWND>(wParam), reinterpret_cast<LPARAM>(this));
                EnumChildWindows(reinterpret_cast<HWND>(wParam), TraceSizeViewChild, reinterpret_cast<LPARAM>(this));
#endif
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
                return AttachBackingNotifications(reinterpret_cast<HWND>(wParam));
#endif
            }
            if (message != SFVM_GETNOTIFY) return E_NOTIMPL;
            if (!wParam || !lParam || !_backingRoot) return E_POINTER;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
            auto* notifyRoot =
                InterlockedCompareExchange(&g_productViewNotificationsUseSyntheticRoot, 0, 0) && _root
                    ? _root : _backingRoot;
            *reinterpret_cast<PIDLIST_ABSOLUTE*>(wParam) = notifyRoot;
            auto* eventMask = reinterpret_cast<LONG*>(lParam);
            *eventMask = SHCNE_ALLEVENTS;
            InterlockedIncrement(&g_productViewGetNotifyCalls);
            InterlockedExchange(&g_productViewGetNotifyEventMask, *eventMask);
            LPWSTR parsingName = nullptr;
            if (SUCCEEDED(SHGetNameFromIDList(notifyRoot, SIGDN_DESKTOPABSOLUTEPARSING, &parsingName)) && parsingName)
            {
                try
                {
                    std::lock_guard<std::mutex> lock(g_productViewNotifyRootMutex);
                    g_productViewNotifyRootIdentity.assign(parsingName);
                }
                catch (...) {}
                CoTaskMemFree(parsingName);
            }
#else
            // Completed size values use provider item identities. Physical events
            // arrive through the separate registration attached to the view window.
#if !defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
            *reinterpret_cast<PIDLIST_ABSOLUTE*>(wParam) = _backingRoot;
#else
            // DefView matches direct children of its monitored folder. A broad profile
            // observer can receive notifications while the native view fails to apply
            // descendants of encoded directory roots. Real list probes require the
            // current directory scope, rather than only proving observer delivery.
            if (!_viewNotificationRoot)
                _viewNotificationRoot = ILCloneFull(_root);
            if (!_viewNotificationRoot) return E_OUTOFMEMORY;
            *reinterpret_cast<PIDLIST_ABSOLUTE*>(wParam) = _viewNotificationRoot;
#endif
            *reinterpret_cast<LONG*>(lParam) = SHCNE_ALLEVENTS;
#endif
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND owner, IBindCtx* bind, LPWSTR name, ULONG* eaten,
            PIDLIST_RELATIVE* item, ULONG* attributes) override
        {
            if (!item) return E_POINTER;
            *item = nullptr;
            if (!_inner || !name) return E_INVALIDARG;
            // The root-identity probe accepts an absolute filesystem path as one namespace child.
            // This keeps the physical folder identity in the child PIDL instead of global CLSID state.
            const bool hasDriveRoot = name[0] && name[1] == L':' &&
                (name[2] == L'\\' || name[2] == L'/');
            const bool hasUncRoot = (name[0] == L'\\' && name[1] == L'\\') ||
                (name[0] == L'/' && name[1] == L'/');
            std::wstring pathFromToken;
            // Decode only the root token. Shell absolute parsing names can append real child segments.
            const auto* tokenSeparator = _wcsnicmp(name, L"edpath:", 7) == 0 ? wcschr(name, L'\\') : nullptr;
            std::wstring rootToken;
            try { if (tokenSeparator) rootToken.assign(name, tokenSeparator - name); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            auto tokenResult = DecodeSyntheticRootPathParsingToken(tokenSeparator ? rootToken.c_str() : name, &pathFromToken);
            if (FAILED(tokenResult)) return tokenResult;
            if (tokenResult == S_OK || hasDriveRoot || hasUncRoot)
            {
                const auto* physicalPath = tokenResult == S_OK ? pathFromToken.c_str() : name;
                PIDLIST_ABSOLUTE backingPidl = nullptr;
                SFGAOF parsedAttributes = 0;
                auto pathResult = SHParseDisplayName(physicalPath, bind, &backingPidl, SFGAO_FOLDER, &parsedAttributes);
                if (FAILED(pathResult)) return pathResult;
                CoTaskMemFree(backingPidl);
                if (!(parsedAttributes & SFGAO_FOLDER)) return HRESULT_FROM_WIN32(ERROR_DIRECTORY);
                PIDLIST_ABSOLUTE pathItem = nullptr;
                pathResult = CreateSyntheticRootPathPidl(physicalPath, &pathItem);
                if (FAILED(pathResult)) return pathResult;
                if (tokenSeparator)
                {
                    IShellFolder2* nested = nullptr;
                    PIDLIST_RELATIVE children = nullptr;
                    pathResult = BindToObject(pathItem, bind, IID_PPV_ARGS(&nested));
                    if (SUCCEEDED(pathResult)) pathResult = nested->ParseDisplayName(owner, bind,
                        const_cast<LPWSTR>(tokenSeparator + 1), nullptr, &children, attributes);
                    auto* combined = SUCCEEDED(pathResult) ? ILCombine(pathItem, children) : nullptr;
                    if (children) CoTaskMemFree(children);
                    if (nested) nested->Release();
                    CoTaskMemFree(pathItem);
                    if (FAILED(pathResult)) return pathResult;
                    if (!combined) return E_OUTOFMEMORY;
                    pathItem = combined;
                }
                *item = reinterpret_cast<PIDLIST_RELATIVE>(pathItem);
                if (eaten) *eaten = static_cast<ULONG>(wcslen(name));
                if (attributes && !tokenSeparator) *attributes = SFGAO_FOLDER | SFGAO_HASSUBFOLDER;
                return S_OK;
            }
            IShellFolder2* backingFolder = _inner;
            backingFolder->AddRef();
            std::wstring currentBackingPath;
            try { currentBackingPath = _backingPath; }
            catch (const std::bad_alloc&) { backingFolder->Release(); return E_OUTOFMEMORY; }
            std::vector<SyntheticChild> children;
            PITEMID_CHILD original = nullptr;
            auto result = S_OK;
            try
            {
                const auto nameLength = wcslen(name);
                if (nameLength == 0) result = E_INVALIDARG;
                size_t start = 0;
                while (SUCCEEDED(result) && start < nameLength)
                {
                    auto end = start;
                    while (end < nameLength && name[end] != L'\\' && name[end] != L'/') ++end;
                    if (end == start)
                    {
                        result = E_INVALIDARG;
                        break;
                    }
                    const std::wstring componentName(name + start, end - start);
                    const bool hasNext = end < nameLength;
                    if (hasNext && end + 1 == nameLength)
                    {
                        result = E_INVALIDARG;
                        break;
                    }
                    result = backingFolder->ParseDisplayName(owner, bind,
                        const_cast<LPWSTR>(componentName.c_str()), nullptr, &original,
                        hasNext ? nullptr : attributes);
                    if (FAILED(result) || !original)
                    {
                        if (SUCCEEDED(result)) result = E_UNEXPECTED;
                        char message[192]{};
                        sprintf_s(message, "synthetic-folder-tree: backing path component parse failed hr=0x%08X", static_cast<unsigned>(result));
                        TraceSyntheticSortStage(message);
                        break;
                    }
                    if (!ILIsEmpty(ILNext(reinterpret_cast<PCUIDLIST_RELATIVE>(original))))
                    {
                        result = E_UNEXPECTED;
                        break;
                    }
                    SyntheticChild child{};
                    result = DescribeBackingChild(backingFolder, currentBackingPath,
                        reinterpret_cast<PCUITEMID_CHILD>(original), &child);
                    if (FAILED(result))
                    {
                        char message[128]{};
                        sprintf_s(message, "synthetic-folder-tree: path component description failed hr=0x%08X", static_cast<unsigned>(result));
                        TraceSyntheticSortStage(message);
                        break;
                    }
                    children.push_back(child);
                    if (hasNext)
                    {
                        if (!(child.attributes & FILE_ATTRIBUTE_DIRECTORY))
                        {
                            result = HRESULT_FROM_WIN32(ERROR_PATH_NOT_FOUND);
                            break;
                        }
                        const auto componentBytes = original->mkid.cb + sizeof(USHORT);
                        auto* singleChild = static_cast<BYTE*>(CoTaskMemAlloc(componentBytes));
                        if (!singleChild)
                        {
                            result = E_OUTOFMEMORY;
                            break;
                        }
                        CopyMemory(singleChild, original, original->mkid.cb);
                        ZeroMemory(singleChild + original->mkid.cb, sizeof(USHORT));
                        IShellFolder2* nestedFolder = nullptr;
                        result = backingFolder->BindToObject(
                            reinterpret_cast<PCUIDLIST_RELATIVE>(singleChild), bind, IID_PPV_ARGS(&nestedFolder));
                        CoTaskMemFree(singleChild);
                        if (FAILED(result))
                        {
                            char message[128]{};
                            sprintf_s(message, "synthetic-folder-tree: path component bind failed hr=0x%08X", static_cast<unsigned>(result));
                            TraceSyntheticSortStage(message);
                            break;
                        }
                        try
                        {
                            currentBackingPath += L"\\";
                            currentBackingPath += child.name;
                        }
                        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; nestedFolder->Release(); break; }
                        backingFolder->Release();
                        backingFolder = nestedFolder;
                    }
                    CoTaskMemFree(original);
                    original = nullptr;
                    start = end + 1;
                }
            }
            catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; }
            if (original) CoTaskMemFree(original);
            backingFolder->Release();
            if (FAILED(result)) return result;
            if (children.empty()) return E_INVALIDARG;

            const auto bytes = children.size() * sizeof(SyntheticChild) + sizeof(USHORT);
            if (bytes > USHRT_MAX) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
            auto* composite = static_cast<BYTE*>(CoTaskMemAlloc(bytes));
            if (!composite) return E_OUTOFMEMORY;
            auto* cursor = composite;
            for (const auto& child : children)
            {
                CopyMemory(cursor, &child, sizeof(child));
                cursor += sizeof(child);
            }
            ZeroMemory(cursor, sizeof(USHORT));
            *item = reinterpret_cast<PITEMID_CHILD>(composite);
            if (eaten) *eaten = static_cast<ULONG>(wcslen(name));
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE EnumObjects(HWND, SHCONTF flags, IEnumIDList** value) override
        {
            ED_SIZE_TRACE("view-enumeration", this, nullptr, flags, 0, 0);
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticEnumObjectsCount);
            if (!_inner) return E_UNEXPECTED;
            std::vector<SyntheticChild> children;
            HRESULT result = S_OK;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
            const auto enumStartMilliseconds = GetTickCount64();
            InterlockedIncrement(&g_productSyntheticEnumStarted);
#endif
            try { result = EnumerateBackingChildren(_backingPath, flags, &children); }
            catch (const std::bad_alloc&)
            {
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                RecordProductSyntheticEnum(enumStartMilliseconds, 0);
#endif
                return E_OUTOFMEMORY;
            }
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
            RecordProductSyntheticEnum(enumStartMilliseconds, children.size());
#endif
            if (FAILED(result)) return result;
            if (_recursiveSizeStore &&
                InterlockedCompareExchange(&g_syntheticTranslateChildNotificationsForProbe, 0, 0) &&
                InterlockedCompareExchange(&g_syntheticRecursiveSizeScanEnabledForProbe, 0, 0) &&
                !InterlockedCompareExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0, 0))
            {
                // DefView may keep an existing row PIDL after re-enumeration; publish fresh metrics so that row reads the updated snapshot.
                for (const auto& child : children)
                {
                    if (!(child.attributes & FILE_ATTRIBUTE_DIRECTORY) ||
                        (child.attributes & FILE_ATTRIBUTE_REPARSE_POINT) || !child.identity.available) continue;
                    std::wstring parsingName;
                    if (SUCCEEDED(GetAbsoluteParsingName(&child, &parsingName)))
                        (void)_recursiveSizeStore->Set(parsingName.c_str(), child.identity,
                            child.size, child.fileCount, child.folderCount);
                }
            }
            *value = new (std::nothrow) SyntheticChildEnumerator(std::move(children));
            return *value ? S_OK : E_OUTOFMEMORY;
        }
        HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!_inner) return E_UNEXPECTED;
            std::wstring pathRoot;
            const auto rootPathResult = ReadSyntheticRootPathFromPidl(
                reinterpret_cast<PCIDLIST_ABSOLUTE>(item), &pathRoot);
            if (rootPathResult == S_OK)
            {
                auto* absoluteRoot = ILCombine(_root, item);
                if (!absoluteRoot) return E_OUTOFMEMORY;
                auto* nested = new (std::nothrow) SyntheticSortFolder(_recursiveSizeStore, _classId);
                if (!nested) { CoTaskMemFree(absoluteRoot); return E_OUTOFMEMORY; }
                auto result = nested->Initialize(absoluteRoot);
                CoTaskMemFree(absoluteRoot);
                if (SUCCEEDED(result)) result = nested->QueryInterface(iid, value);
                nested->Release();
                return result;
            }
            if (FAILED(rootPathResult)) return rootPathResult;
            const auto* child = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(item));
            if (!child) return E_INVALIDARG;
            const bool traceNested = _wcsicmp(child->name, L"nested") == 0;
            if (traceNested) TraceSyntheticFolderStage("BindToObject-nested-enter", _backingPath, S_OK);
            PITEMID_CHILD original = nullptr;
            auto result = _inner->ParseDisplayName(nullptr, bind, const_cast<LPWSTR>(child->name), nullptr, &original, nullptr);
            if (FAILED(result)) return result;
            SFGAOF itemAttributes = SFGAO_FOLDER;
            result = _inner->GetAttributesOf(1, const_cast<PCUITEMID_CHILD_ARRAY>(&original), &itemAttributes);
            if (FAILED(result) || !(itemAttributes & SFGAO_FOLDER))
            {
                CoTaskMemFree(original);
                return FAILED(result) ? result : E_NOINTERFACE;
            }
            const auto* remaining = ILNext(item);
            const bool hasRemaining = !ILIsEmpty(remaining);
            if (iid != IID_IUnknown && iid != IID_IShellFolder && iid != IID_IShellFolder2)
            {
                if (!hasRemaining)
                {
                    result = _inner->BindToObject(original, bind, iid, value);
                    CoTaskMemFree(original);
                    return result;
                }
            }

            IShellFolder2* backingChild = nullptr;
            result = _inner->BindToObject(original, bind, IID_PPV_ARGS(&backingChild));
            if (FAILED(result)) { CoTaskMemFree(original); return result; }
            auto* firstChild = CloneSyntheticChild(*child);
            if (!firstChild)
            {
                CoTaskMemFree(original);
                backingChild->Release();
                return E_OUTOFMEMORY;
            }
            auto* nestedRoot = ILCombine(_root, reinterpret_cast<PCUIDLIST_RELATIVE>(firstChild));
            CoTaskMemFree(firstChild);
            auto* nestedBackingRoot = ILCombine(_backingRoot, original);
            CoTaskMemFree(original);
            if (!nestedRoot || !nestedBackingRoot)
            {
                if (nestedRoot) CoTaskMemFree(nestedRoot);
                if (nestedBackingRoot) CoTaskMemFree(nestedBackingRoot);
                backingChild->Release();
                return E_OUTOFMEMORY;
            }
            auto* nested = new (std::nothrow) SyntheticSortFolder(_recursiveSizeStore, _classId);
            if (!nested)
            {
                CoTaskMemFree(nestedRoot);
                CoTaskMemFree(nestedBackingRoot);
                backingChild->Release();
                return E_OUTOFMEMORY;
            }
            nested->_root = nestedRoot;
            nested->_backingRoot = nestedBackingRoot;
            nested->_inner = backingChild;
            try { nested->_backingPath = _backingPath + L"\\" + child->name; }
            catch (const std::bad_alloc&) { nested->Release(); return E_OUTOFMEMORY; }
            result = hasRemaining
                ? nested->BindToObject(remaining, bind, iid, value)
                : nested->QueryInterface(iid, value);
            if (traceNested) TraceSyntheticFolderStage("BindToObject-nested-return", nested->_backingPath, result);
            nested->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!_inner) return E_UNEXPECTED;
            const auto* child = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(item));
            if (!child) return E_INVALIDARG;
            const auto* remaining = ILNext(item);
            if (!ILIsEmpty(remaining))
            {
                auto* firstChild = CloneSyntheticChild(*child);
                if (!firstChild) return E_OUTOFMEMORY;
                IShellFolder2* nestedFolder = nullptr;
                auto result = BindToObject(reinterpret_cast<PCUIDLIST_RELATIVE>(firstChild), bind,
                    IID_PPV_ARGS(&nestedFolder));
                CoTaskMemFree(firstChild);
                if (SUCCEEDED(result)) result = nestedFolder->BindToStorage(remaining, bind, iid, value);
                if (nestedFolder) nestedFolder->Release();
                return result;
            }
            PITEMID_CHILD original = nullptr;
            auto result = _inner->ParseDisplayName(nullptr, bind, const_cast<LPWSTR>(child->name), nullptr, &original, nullptr);
            if (SUCCEEDED(result)) result = _inner->BindToStorage(original, bind, iid, value);
            if (original) CoTaskMemFree(original);
            return result;
        }
        HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM column, PCUIDLIST_RELATIVE left, PCUIDLIST_RELATIVE right) override
        {
            SyntheticCompareCallbackScope compareScope;
            (void)compareScope;
            if (!left || !right) return E_INVALIDARG;
            const auto isPathMarker = [](PCUIDLIST_RELATIVE value)
            {
                return value->mkid.cb >= kSyntheticRootPathPidlHeaderSize &&
                    IsSyntheticRootPathMagic(reinterpret_cast<const SyntheticRootPathPidlItem*>(value)->magic);
            };
            if (isPathMarker(left) || isPathMarker(right))
            {
                // Root markers are namespace children too. Rejecting even marker==marker
                // breaks Shell's canonical identity comparisons before ordinary rows are reached.
                std::wstring leftPath, rightPath;
                const auto leftResult = ReadSyntheticRootPathFromPidl(left, &leftPath, true);
                const auto rightResult = ReadSyntheticRootPathFromPidl(right, &rightPath, true);
                if (FAILED(leftResult)) return leftResult;
                if (FAILED(rightResult)) return rightResult;
                const auto comparison = leftResult != rightResult ? (leftResult == S_OK ? -1 : 1) :
                    _wcsicmp(leftPath.c_str(), rightPath.c_str());
                return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL,
                    comparison < 0 ? static_cast<USHORT>(-1) : comparison > 0 ? 1 : 0);
            }
            const auto* leftChild = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(left));
            const auto* rightChild = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(right));
            if (!leftChild || !rightChild) return E_INVALIDARG;
            ED_PROBE_METRIC_INCREMENT(g_syntheticCompareCount);
            const auto compareColumn = LOWORD(column);
            const auto customCount = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) ? 4u : 3u;
            if (compareColumn >= customCount)
            {
                UINT backingColumn = 0;
                auto result = BackingColumnOrdinal(compareColumn, &backingColumn);
                if (FAILED(result)) return result;
                PITEMID_CHILD backingLeft = nullptr, backingRight = nullptr;
                result = BackingChildPidl(left, &backingLeft);
                if (SUCCEEDED(result)) result = BackingChildPidl(right, &backingRight);
                if (SUCCEEDED(result)) result = _inner->CompareIDs(
                    (column & ~static_cast<LPARAM>(0xFFFF)) | backingColumn, backingLeft, backingRight);
                if (backingLeft) CoTaskMemFree(backingLeft);
                if (backingRight) CoTaskMemFree(backingRight);
                return result;
            }
            if (compareColumn < ARRAYSIZE(g_syntheticCompareByColumn))
                ED_PROBE_METRIC_INCREMENT(g_syntheticCompareByColumn[compareColumn]);
            short comparison = 0;
            switch (LOWORD(column))
            {
            case 0: comparison = static_cast<short>(_wcsicmp(leftChild->name, rightChild->name)); break;
            case 1:
            {
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                const auto compareSequence = InterlockedIncrement64(&g_productRecursiveCompareCount);
                LARGE_INTEGER compareStart{};
                const bool sampleCompareDuration = (compareSequence & 63) == 0;
                if (sampleCompareDuration) QueryPerformanceCounter(&compareStart);
#endif
                if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticSizeCompareCount);
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
                const bool leftFolder = (leftChild->attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                const bool rightFolder = (rightChild->attributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
                if (!InterlockedCompareExchange(&_mixedSizeSort, 0, 0) && leftFolder != rightFolder)
                {
                    // Shell reverses CompareIDs for descending; compensate only the category
                    // boundary so folders remain first while each category follows the requested direction.
                    comparison = static_cast<short>((leftFolder ? -1 : 1) * CurrentSizeSortDirection());
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                    RecordProductCompareDiagnostic(column, leftChild->name, 0, rightChild->name, 0, comparison, true, this);
#endif
                    break;
                }
#endif
                const bool ignoreFolderSize =
                    InterlockedCompareExchange(&g_syntheticIgnoreRecursiveFolderSizeSortForProbe, 0, 0) != 0;
                // Q-Dir exposes an option to ignore recursive folder sizes for sorting while keeping them displayable.
                const auto leftSize = ignoreFolderSize && (leftChild->attributes & FILE_ATTRIBUTE_DIRECTORY)
                    ? 0 : SizeForItem(reinterpret_cast<PCUITEMID_CHILD>(left));
                const auto rightSize = ignoreFolderSize && (rightChild->attributes & FILE_ATTRIBUTE_DIRECTORY)
                    ? 0 : SizeForItem(reinterpret_cast<PCUITEMID_CHILD>(right));
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                if (sampleCompareDuration)
                {
                    LARGE_INTEGER compareEnd{};
                    QueryPerformanceCounter(&compareEnd);
                    const auto elapsedTicks = compareEnd.QuadPart - compareStart.QuadPart;
                    InterlockedIncrement64(&g_productRecursiveCompareTimingSamples);
                    InterlockedAdd64(&g_productRecursiveCompareSampledTicks, elapsedTicks);
                    auto maximumTicks = InterlockedCompareExchange64(&g_productRecursiveCompareMaximumSampleTicks, 0, 0);
                    while (maximumTicks < elapsedTicks)
                    {
                        const auto previous = InterlockedCompareExchange64(&g_productRecursiveCompareMaximumSampleTicks,
                            elapsedTicks, maximumTicks);
                        if (previous == maximumTicks) break;
                        maximumTicks = previous;
                    }
                }
#endif
                comparison = leftSize < rightSize ? -1 : leftSize > rightSize ? 1 : 0;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
                RecordProductCompareDiagnostic(column, leftChild->name, leftSize, rightChild->name, rightSize, comparison, false, this,
                    _hasEncodedSizeSortPolicy ? static_cast<int>(InterlockedCompareExchange(&_mixedSizeSort, 0, 0)) : -1);
#else
                RecordProductCompareDiagnostic(column, leftChild->name, leftSize, rightChild->name, rightSize, comparison, false, this);
#endif
#endif
                break;
            }
            case 2: comparison = 0; break;
            case 3:
                if (!InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0)) return E_INVALIDARG;
            {
                const auto leftPercent = RelativePercentForItem(reinterpret_cast<PCUITEMID_CHILD>(left));
                const auto rightPercent = RelativePercentForItem(reinterpret_cast<PCUITEMID_CHILD>(right));
                comparison = leftPercent < rightPercent ? -1 : leftPercent > rightPercent ? 1 : 0;
                break;
            }
            default: return E_INVALIDARG;
            }
            if (comparison != 0) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, static_cast<USHORT>(comparison));

            // Shell navigation may compare a multi-level relative PIDL at its parent folder.
            // Comparing only its first synthetic item made a folder and its descendant indistinguishable there.
            const auto* leftRemaining = ILNext(left);
            const auto* rightRemaining = ILNext(right);
            const bool leftHasRemaining = !ILIsEmpty(leftRemaining);
            const bool rightHasRemaining = !ILIsEmpty(rightRemaining);
            if (!leftHasRemaining || !rightHasRemaining)
            {
                if (leftHasRemaining == rightHasRemaining) return S_OK;
                return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, leftHasRemaining ? 1 : static_cast<USHORT>(-1));
            }

            auto* firstChild = CloneSyntheticChild(*leftChild);
            if (!firstChild) return E_OUTOFMEMORY;
            IShellFolder2* nestedFolder = nullptr;
            auto result = BindToObject(reinterpret_cast<PCUIDLIST_RELATIVE>(firstChild), nullptr,
                IID_PPV_ARGS(&nestedFolder));
            CoTaskMemFree(firstChild);
            if (FAILED(result)) return result;
            result = nestedFolder->CompareIDs(column, leftRemaining, rightRemaining);
            nestedFolder->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE CreateViewObject(HWND owner, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IShellView)
            {
                // The custom view owns row presentation; background commands and folder drop handling belong to the backing folder.
                return (iid == IID_IContextMenu || iid == IID_IDropTarget) && _inner
                    ? _inner->CreateViewObject(owner, iid, value)
                    : E_NOINTERFACE;
            }
            SFV_CREATE create{};
            create.cbSize = sizeof(create);
            create.pshf = this;
            create.psfvcb = static_cast<IShellFolderViewCB*>(this);
            TraceSyntheticFolderStage("CreateViewObject-IShellView-enter", _backingPath, S_OK);
            const auto result = SHCreateShellFolderView(&create, reinterpret_cast<IShellView**>(value));
            TraceSyntheticFolderStage(*value ? "CreateViewObject-IShellView-return-view" : "CreateViewObject-IShellView-return-null",
                _backingPath, result);
            if (SUCCEEDED(result) && *value && !_shellView)
            {
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
                // Borrow the view: it owns this callback. Keeping both alive here
                // formed a reference cycle and exhausted the prototype's 32 slots.
                _shellView = static_cast<IShellView*>(*value);
                InterlockedExchange(&_asyncScansCancelled, 0);
                InterlockedExchange(&_asyncNotificationsEnabled, 1);
#else
                bool held = false;
                for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
                {
                    auto* existing = InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], this, nullptr);
                    if (!existing || existing == this)
                    {
                        AddRef();
                        _shellView = static_cast<IShellView*>(*value);
                        _shellView->AddRef();
                        InterlockedExchange(&_asyncScansCancelled, 0);
                        InterlockedExchange(&_asyncNotificationsEnabled, 1);
                        held = true;
                        break;
                    }
                }
                if (!held) TraceSyntheticSortStage("synthetic-sort: view-hold-capacity-exhausted");
#endif
            }
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT count, PCUITEMID_CHILD_ARRAY items, SFGAOF* attributes) override
        {
            if (!attributes) return E_POINTER;
            if (count == 0) { *attributes = SFGAO_FOLDER | SFGAO_HASSUBFOLDER; return S_OK; }
            if (!_inner) return E_UNEXPECTED;
            // IShellItem asks the namespace parent to classify path-marker PIDLs during session restore.
            if (count == 1)
            {
                std::wstring path;
                const auto pathResult = ReadSyntheticRootPathFromPidl(
                    reinterpret_cast<PCIDLIST_ABSOLUTE>(items[0]), &path, true);
                if (pathResult == S_OK)
                {
                    PIDLIST_ABSOLUTE pathPidl = nullptr;
                    SFGAOF pathAttributes = 0;
                    const auto parseResult = SHParseDisplayName(path.c_str(), nullptr, &pathPidl,
                        *attributes & (SFGAO_FOLDER | SFGAO_FILESYSTEM), &pathAttributes);
                    if (pathPidl) CoTaskMemFree(pathPidl);
                    if (FAILED(parseResult)) return parseResult;
                    *attributes &= pathAttributes;
                    if (InterlockedCompareExchange(&g_syntheticMaskFilesystemAttributes, 0, 0))
                        *attributes &= ~(SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR);
                    ED_SIZE_TRACE("root-attributes", this, nullptr, 0, *attributes, 0);
                    return S_OK;
                }
                if (FAILED(pathResult)) return pathResult;
            }
            if (InterlockedCompareExchange(&g_syntheticOwnAttributesForProbe, 0, 0))
            {
                const SFGAOF requested = *attributes;
                SFGAOF supported = static_cast<SFGAOF>(~0u);
                for (UINT index = 0; index < count; ++index)
                {
                    const auto* child = SyntheticData(items[index]);
                    if (!child) return E_INVALIDARG;
                    const SFGAOF itemAttributes = (child->attributes & FILE_ATTRIBUTE_DIRECTORY)
                        ? SFGAO_FOLDER | SFGAO_HASSUBFOLDER
                        : SFGAO_STREAM;
                    supported &= itemAttributes;
                }
                *attributes = requested & supported;
                ED_PROBE_METRIC_INCREMENT(g_syntheticGetAttributesOfCount);
                return S_OK;
            }
            std::vector<PITEMID_CHILD> original;
            try { original.resize(count, nullptr); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            HRESULT result = S_OK;
            bool traceNestedChild = false;
            const auto requestedAttributes = *attributes;
            for (UINT index = 0; index < count; ++index)
            {
                const auto* child = SyntheticData(items[index]);
                if (!child) { result = E_INVALIDARG; break; }
                traceNestedChild = traceNestedChild || _wcsicmp(child->name, L"nested") == 0;
                ULONG eaten = 0;
                result = _inner->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(child->name), &eaten, &original[index], nullptr);
                if (FAILED(result)) break;
            }
            if (SUCCEEDED(result))
            {
                result = _inner->GetAttributesOf(count, const_cast<PCUITEMID_CHILD_ARRAY>(original.data()), attributes);
                if (SUCCEEDED(result))
                {
                    ED_PROBE_METRIC_INCREMENT(g_syntheticGetAttributesOfCount);
                    if (*attributes & (SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR))
                        ED_PROBE_METRIC_INCREMENT(g_syntheticFilesystemAttributeCount);
                    if (InterlockedCompareExchange(&g_syntheticMaskFilesystemAttributes, 0, 0))
                        *attributes &= ~(SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR);
                }
            }
            if (traceNestedChild) TraceSyntheticFolderAttributes(_backingPath, requestedAttributes, *attributes, result);
            ED_SIZE_TRACE("item-attributes", this, count == 1 && SyntheticData(items[0]) ? SyntheticData(items[0])->name : nullptr,
                requestedAttributes, *attributes, static_cast<ULONG>(result));
            for (auto item : original) if (item) CoTaskMemFree(item);
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND owner, UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, UINT* reserved, void** value) override
        {
            if (iid == IID_IPropertyStoreFactory) ED_PROBE_METRIC_INCREMENT(g_syntheticPropertyStoreFactoryRequests);
            else if (iid == IID_IPropertyStore) ED_PROBE_METRIC_INCREMENT(g_syntheticPropertyStoreRequests);
            else ED_PROBE_METRIC_INCREMENT(g_syntheticOtherUiObjectRequests);
            ED_SIZE_TRACE("ui-object", this, nullptr, iid.Data1, count, 0);
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!_inner || count == 0 || !items) return E_NOINTERFACE;
            std::vector<PITEMID_CHILD> original;
            try { original.resize(count, nullptr); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            HRESULT result = S_OK;
            for (UINT index = 0; index < count; ++index)
            {
                const auto* child = SyntheticData(items[index]);
                if (!child) { result = E_INVALIDARG; break; }
                ULONG eaten = 0;
                result = _inner->ParseDisplayName(owner, nullptr, const_cast<LPWSTR>(child->name), &eaten, &original[index], nullptr);
                if (FAILED(result)) break;
            }
            if (SUCCEEDED(result)) result = _inner->GetUIObjectOf(owner, count, const_cast<PCUITEMID_CHILD_ARRAY>(original.data()), iid, reserved, value);
            for (auto* child : original) if (child) CoTaskMemFree(child);
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD item, SHGDNF flags, STRRET* name) override
        {
            if (!name) return E_POINTER;
            std::wstring rootPath;
            const auto rootPathResult = ReadSyntheticRootPathFromPidl(
                reinterpret_cast<PCIDLIST_ABSOLUTE>(item), &rootPath);
            if (rootPathResult == S_OK)
            {
                std::wstring displayText;
                try
                {
                    if (flags & SHGDN_FORPARSING)
                    {
                        auto token = EncodeSyntheticRootPathParsingToken(rootPath.c_str());
                        if (flags & SHGDN_INFOLDER) displayText = std::move(token);
                        else
                        {
                            WCHAR classIdText[64]{};
                            if (!StringFromGUID2(_classId, classIdText, ARRAYSIZE(classIdText))) return E_FAIL;
                            displayText = L"::";
                            displayText += classIdText;
                            displayText += L"\\";
                            displayText += token;
                        }
                    }
                    else if (flags & SHGDN_INFOLDER)
                    {
                        const auto separator = rootPath.find_last_of(L"\\/");
                        displayText = separator == std::wstring::npos ? rootPath : rootPath.substr(separator + 1);
                        if (displayText.empty()) displayText = rootPath;
                    }
                    else displayText = rootPath;
                }
                catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
                name->uType = STRRET_WSTR;
                const auto bytes = (displayText.size() + 1) * sizeof(WCHAR);
                name->pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
                if (!name->pOleStr) return E_OUTOFMEMORY;
                CopyMemory(name->pOleStr, displayText.c_str(), bytes);
                return S_OK;
            }
            if (FAILED(rootPathResult)) return rootPathResult;
            const auto* child = SyntheticData(item);
            if (!child) return E_INVALIDARG;
            std::wstring childDisplayName;
            try { childDisplayName = child->name; }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            if ((flags & SHGDN_FORPARSING) && !(flags & SHGDN_INFOLDER))
            {
                // Desktop-absolute parsing must include this provider parent, not only the leaf.
                // Otherwise Shell cannot identify a nested folder in navigation callbacks or bind saved selections.
                LPWSTR parentName = nullptr;
                auto result = SHGetNameFromIDList(_root, SIGDN_DESKTOPABSOLUTEPARSING, &parentName);
                if (FAILED(result)) return result;
                if (!parentName) return E_FAIL;
                try { childDisplayName = std::wstring(parentName) + L"\\" + child->name; }
                catch (const std::bad_alloc&) { CoTaskMemFree(parentName); return E_OUTOFMEMORY; }
                CoTaskMemFree(parentName);
            }
            name->uType = STRRET_WSTR;
            const auto bytes = (childDisplayName.size() + 1) * sizeof(WCHAR);
            name->pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
            if (!name->pOleStr) return E_OUTOFMEMORY;
            CopyMemory(name->pOleStr, childDisplayName.c_str(), bytes);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE SetNameOf(HWND owner, PCUITEMID_CHILD item, LPCWSTR name, SHGDNF flags,
            PITEMID_CHILD* renamed) override
        {
            if (!renamed) return E_POINTER;
            *renamed = nullptr;
            if (!_inner) return E_UNEXPECTED;
            const auto* child = SyntheticData(item);
            if (!child) return E_INVALIDARG;
            std::wstring oldParsingName;
            const bool hasOldDirectorySnapshot = _recursiveSizeStore &&
                (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                SUCCEEDED(GetAbsoluteParsingName(child, &oldParsingName));
            PITEMID_CHILD original = nullptr;
            auto result = _inner->ParseDisplayName(owner, nullptr, const_cast<LPWSTR>(child->name), nullptr, &original, nullptr);
            if (FAILED(result)) return result;
            PITEMID_CHILD rawRenamed = nullptr;
            result = _inner->SetNameOf(owner, original, name, flags, &rawRenamed);
            CoTaskMemFree(original);
            if (FAILED(result)) return result;
            SyntheticChild description{};
            result = DescribeBackingChild(_inner, _backingPath, rawRenamed, &description);
            if (SUCCEEDED(result))
            {
                if (hasOldDirectorySnapshot)
                {
                    std::wstring newParsingName;
                    if (SUCCEEDED(GetAbsoluteParsingName(&description, &newParsingName)))
                        (void)_recursiveSizeStore->Rename(oldParsingName.c_str(), newParsingName.c_str(), description.identity);
                }
                *renamed = CloneSyntheticChild(description);
                if (!*renamed) result = E_OUTOFMEMORY;
            }
            CoTaskMemFree(rawRenamed);
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID*) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch**) override { return E_NOTIMPL; }
        HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD, ULONG* sort, ULONG* display) override
        {
            if (!sort || !display) return E_POINTER;
            *sort = 0;
            *display = 0;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column, SHCOLSTATEF* state) override
        {
            if (!state)
            {
                RecordSyntheticColumnMetadata(2, column, {}, E_POINTER, nullptr);
                return E_POINTER;
            }
            const bool extended = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) != 0;
            if (column > (extended ? 3u : 2u))
            {
                UINT backingColumn = 0;
                auto result = BackingColumnOrdinal(column, &backingColumn);
                if (SUCCEEDED(result)) result = _inner->GetDefaultColumnState(backingColumn, state);
                RecordSyntheticColumnMetadata(2, column, SUCCEEDED(result) ? *state : SHCOLSTATEF{}, result, nullptr);
                return result;
            }
            const auto relativePercentType = static_cast<SHCOLSTATEF>(
                InterlockedCompareExchange(&g_syntheticRelativePercentColumnTypeForProbe, 0, 0));
            *state = SHCOLSTATE_ONBYDEFAULT | (column == 1 ? SHCOLSTATE_TYPE_INT :
                column == 3 ? relativePercentType : SHCOLSTATE_TYPE_STR);
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
            // Keep Shell from applying an independent folder partition before our size policy.
            if (column == 1) *state |= SHCOLSTATE_NOSORTBYFOLDERNESS;
#endif
            if (column == 1 && InterlockedCompareExchange(&g_syntheticCustomSizeColumnForProbe, 0, 0) &&
                InterlockedCompareExchange(&g_syntheticSlowCustomSizeColumnForProbe, 0, 0))
                *state |= SHCOLSTATE_SLOW;
            RecordSyntheticColumnMetadata(2, column, *state, S_OK, nullptr);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* key, VARIANT* value) override
        {
            if (key && *key == kSizeKey)
            {
                if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticSizeDetailsExCount);
                const auto* child = SyntheticData(item);
                if (child && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticSizeDetailsExFolderCount);
                }
                else if (child)
                {
                    if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticSizeDetailsExFileCount);
                }
            }
            if (key && *key == kSyntheticRecursiveSizeKey)
            {
                if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticCustomSizeDetailsExCount);
                const auto* child = SyntheticData(item);
                if (child && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticCustomSizeDetailsExFolderCount);
                }
                else if (child)
                {
                    if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticCustomSizeDetailsExFileCount);
                }
            }
            if (!key || !value) return E_INVALIDARG;
            const auto* child = SyntheticData(item);
            if (!child) return E_INVALIDARG;
            VariantInit(value);
            ED_SIZE_TRACE("details-ex-key", this, child->name, key->pid, key->fmtid.Data1, child->size);
            if (*key == kSyntheticRelativeSizeKey &&
                InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0))
            {
                value->vt = VT_R8;
                value->dblVal = RelativePercentForItem(item);
            }
            else if (*key == kSizeKey || *key == kSyntheticRecursiveSizeKey)
            {
                const auto* sizeChild = SyntheticData(item);
                // Disabling totals must hide directory values even when a PIDL or snapshot contains an older size.
                // Files retain their ordinary size property; no directory scan is needed for this result.
                if ((sizeChild->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                    !InterlockedCompareExchange(&g_folderSizeEnabled, 0, 0)) return S_OK;
                // Shell may request only standard PKEY_Size, without calling GetDetailsOf first.
                if (InterlockedCompareExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0, 0) &&
                    sizeChild && (sizeChild->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                    !(sizeChild->attributes & FILE_ATTRIBUTE_REPARSE_POINT))
                {
                    if (InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 0, 0))
                        ScheduleRecursiveSizeSnapshot(sizeChild);
                    else
                    {
                        const auto scanResult = EnsureRecursiveSizeSnapshot(sizeChild);
                        if (FAILED(scanResult)) return scanResult;
                    }
                }
                value->vt = VT_UI8;
                value->ullVal = SizeForItem(item);
                if (sizeChild && (sizeChild->attributes & FILE_ATTRIBUTE_DIRECTORY) && _recursiveSizeStore)
                {
                    std::wstring parsingName;
                    ULONGLONG cachedBytes = 0;
                    bool available = true;
                    bool cached = TryGetLocalRecursiveSize(sizeChild, &cachedBytes, nullptr, &available);
                    if (!cached) cached = SUCCEEDED(GetAbsoluteParsingName(sizeChild, &parsingName)) &&
                        _recursiveSizeStore->TryGet(parsingName, sizeChild->identity, &cachedBytes, nullptr, nullptr, nullptr, &available);
                    // Before the first async result, the PIDL's placeholder is not a real zero-byte total.
                    // Keep synchronous probe behavior, but expose pending and unavailable async values as empty.
                    if ((cached && !available) || (!cached &&
                        InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 0, 0))) VariantClear(value);
                }
            }
            else
            {
                PITEMID_CHILD original = nullptr;
                auto result = BackingChildPidl(item, &original);
                if (SUCCEEDED(result)) result = _inner->GetDetailsEx(original, key, value);
                if (original) CoTaskMemFree(original);
                return result;
            }
            ED_SIZE_TRACE("details-ex-value", this, child->name, value->vt, child->attributes,
                value->vt == VT_UI8 ? value->ullVal : 0);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override
        {
            if (item)
            {
                if (!g_syntheticSizeMetricsObservation) ED_PROBE_METRIC_INCREMENT(g_syntheticItemDetailsOfCount);
                if (column == 1 && !g_syntheticSizeMetricsObservation)
                    ED_PROBE_METRIC_INCREMENT(g_syntheticSizeDetailsOfItemCount);
            }
#if defined(EXPLORERDIRS_SIZE_TRACE_EXPERIMENT)
            const auto* traceChild = SyntheticData(item);
            ED_SIZE_TRACE("details-of-key", this, traceChild ? traceChild->name : nullptr, column,
                traceChild ? traceChild->attributes : 0, traceChild ? traceChild->size : 0);
#endif
            const bool extended = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) != 0;
            if (details && column > (extended ? 3u : 2u))
            {
                UINT backingColumn = 0;
                auto result = BackingColumnOrdinal(column, &backingColumn);
                if (FAILED(result)) return result;
                if (!item) return _inner->GetDetailsOf(nullptr, backingColumn, details);
                PITEMID_CHILD original = nullptr;
                result = BackingChildPidl(item, &original);
                if (SUCCEEDED(result)) result = _inner->GetDetailsOf(original, backingColumn, details);
                if (SUCCEEDED(result))
                {
                    // STRRET_OFFSET may reference the temporary backing PIDL. Materialize it
                    // before releasing that PIDL; the Shell caller owns the resulting string.
                    LPWSTR text = nullptr;
                    result = StrRetToStrW(&details->str, original, &text);
                    if (SUCCEEDED(result)) { details->str.uType = STRRET_WSTR; details->str.pOleStr = text; }
                }
                if (original) CoTaskMemFree(original);
                return result;
            }
            if (!details || column > (extended ? 3u : 2u))
            {
                if (!item) RecordSyntheticColumnMetadata(1, column, {}, E_INVALIDARG, nullptr);
                return E_INVALIDARG;
            }
            const auto* child = SyntheticData(item);
            std::wstring text;
            if (!item)
            {
                text = column == 0 ? L"Name" : column == 1 ?
                    (InterlockedCompareExchange(&g_syntheticCustomSizeColumnForProbe, 0, 0) ? L"Recursive Size" : L"Size") :
                    column == 2 ? L"Type" : L"Relative %";
                details->fmt = column == 1 || column == 3 ? LVCFMT_RIGHT : LVCFMT_LEFT;
                details->cxChar = column == 0 ? 24 : 12;
            }
            else
            {
                if (!child) return E_INVALIDARG;
                if (column == 1 && (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                    !InterlockedCompareExchange(&g_folderSizeEnabled, 0, 0))
                {
                    details->fmt = LVCFMT_RIGHT;
                    details->cxChar = 12;
                    details->str.uType = STRRET_WSTR;
                    details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(sizeof(WCHAR)));
                    if (!details->str.pOleStr) return E_OUTOFMEMORY;
                    *details->str.pOleStr = L'\0';
                    return S_OK;
                }
                const bool recursiveContentsColumn = column == 2 && extended &&
                    (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                    !(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT);
                if ((column == 1 || recursiveContentsColumn) &&
                    InterlockedCompareExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0, 0) &&
                    (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                    !(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT))
                {
                    if (InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 0, 0))
                        ScheduleRecursiveSizeSnapshot(child);
                    else
                    {
                        const auto scanResult = EnsureRecursiveSizeSnapshot(child);
                        if (FAILED(scanResult)) return scanResult;
                    }
                }
                bool asyncValuePending = false;
                bool sizeAvailable = true;
                if ((column == 1 || recursiveContentsColumn) &&
                    InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 0, 0) &&
                    (child->attributes & FILE_ATTRIBUTE_DIRECTORY) && !(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT))
                {
                    std::wstring parsingName;
                    ULONGLONG cachedBytes = 0;
                    asyncValuePending = FAILED(GetAbsoluteParsingName(child, &parsingName)) || !_recursiveSizeStore ||
                        !_recursiveSizeStore->TryGet(parsingName, child->identity, &cachedBytes, nullptr, nullptr, nullptr, &sizeAvailable);
                }
                text = column == 0 ? child->name : column == 1 ?
                    (asyncValuePending ? L"" : !sizeAvailable ? L"\x2014" : InterlockedCompareExchange(&g_syntheticHumanSizeTextForProbe, 0, 0)
                        ? RecursiveSizeTextForItem(item) : std::to_wstring(SizeForItem(item))) :
                    column == 2 ? ((extended && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                        ? (asyncValuePending ? L"" : ContentsForItem(item))
                        : ((child->attributes & FILE_ATTRIBUTE_DIRECTORY) ? L"File folder" : L"File")) :
                    std::to_wstring(RelativePercentForItem(item)) + L"%";
                if (column == 2 && !recursiveContentsColumn && _inner)
                {
                    PITEMID_CHILD original = nullptr;
                    if (SUCCEEDED(BackingChildPidl(item, &original)))
                    {
                        VARIANT type{}, converted{};
                        VariantInit(&type); VariantInit(&converted);
                        if (SUCCEEDED(_inner->GetDetailsEx(original, &PKEY_ItemTypeText, &type)) &&
                            SUCCEEDED(VariantChangeType(&converted, &type, 0, VT_BSTR)) && converted.bstrVal)
                            text.assign(converted.bstrVal, SysStringLen(converted.bstrVal));
                        VariantClear(&converted); VariantClear(&type); CoTaskMemFree(original);
                    }
                }
                details->fmt = column == 1 || column == 3 ? LVCFMT_RIGHT : LVCFMT_LEFT;
                details->cxChar = 16;
            }
            details->str.uType = STRRET_WSTR;
            const auto bytes = (text.size() + 1) * sizeof(WCHAR);
            details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
            if (!details->str.pOleStr)
            {
                if (!item) RecordSyntheticColumnMetadata(1, column, {}, E_OUTOFMEMORY, nullptr);
                return E_OUTOFMEMORY;
            }
            CopyMemory(details->str.pOleStr, text.c_str(), bytes);
            if (!item) RecordSyntheticColumnMetadata(1, column, {}, S_OK, text.c_str());
            ED_SIZE_TRACE("details-of-text", this, text.c_str(), column, 0, text.size());
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT column, SHCOLUMNID* key) override
        {
            ED_PROBE_METRIC_INCREMENT(g_syntheticMapColumnToScidCount);
            if (column < ARRAYSIZE(g_syntheticMapColumnToScidByColumn))
            ED_PROBE_METRIC_INCREMENT(g_syntheticMapColumnToScidByColumn[column]);
            HRESULT result = S_OK;
            SHCOLUMNID mapped{};
            if (!key) result = E_POINTER;
            else if (column == 0) mapped = PKEY_ItemNameDisplay;
            else if (column == 1 && InterlockedCompareExchange(&g_syntheticCustomSizeColumnForProbe, 0, 0))
                mapped = kSyntheticRecursiveSizeKey;
            else if (column == 1) mapped = PKEY_Size;
            else if (column == 2) mapped = PKEY_ItemTypeText;
            else if (column == 3 && InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0))
                mapped = kSyntheticRelativeSizeKey;
            else
            {
                UINT backingColumn = 0;
                result = BackingColumnOrdinal(column, &backingColumn);
                if (SUCCEEDED(result)) result = _inner->MapColumnToSCID(backingColumn, &mapped);
            }
            if (SUCCEEDED(result)) *key = mapped;
            const auto traceIndex = InterlockedIncrement(&g_syntheticMapColumnTraceCount) - 1;
            if (traceIndex < kSyntheticMapColumnTraceCapacity)
            {
                auto& trace = g_syntheticMapColumnTrace[traceIndex];
                trace.column = column;
                trace.key = mapped;
                trace.result = result;
                InterlockedExchange(&trace.sequence, traceIndex + 1);
            }
            return result;
        }
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
        static UINT SizeDisplayMessage()
        {
            static const UINT message = RegisterWindowMessageW(L"ExplorerDirs.SizeDisplayReady.v1");
            return message;
        }
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
        bool IsSizeDisplayColumn(int subItem)
        {
            if (!_shellView || subItem < 0) return false;
            IColumnManager* manager = nullptr;
            if (FAILED(_shellView->QueryInterface(IID_PPV_ARGS(&manager)))) return false;
            UINT count = 0;
            auto result = manager->GetColumnCount(CM_ENUM_VISIBLE, &count);
            std::vector<PROPERTYKEY> columns;
            try { if (SUCCEEDED(result) && count <= 512) columns.resize(count); }
            catch (const std::bad_alloc&) { manager->Release(); return false; }
            if (columns.empty()) { manager->Release(); return false; }
            result = manager->GetColumns(CM_ENUM_VISIBLE, columns.data(), count);
            manager->Release();
            return SUCCEEDED(result) && static_cast<UINT>(subItem) < count && columns[subItem] == PKEY_Size;
        }
        bool ReadSizeDisplayRow(int index, std::wstring* name, std::wstring* text)
        {
            if (!_shellView || index < 0) return false;
            IFolderView* view = nullptr;
            if (FAILED(_shellView->QueryInterface(IID_PPV_ARGS(&view)))) return false;
            PITEMID_CHILD item = nullptr;
            const auto itemResult = view->Item(index, &item);
            view->Release();
            if (FAILED(itemResult) || !item) return false;
            const auto* child = SyntheticData(item);
            bool read = false;
            if (child && (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                !(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT))
            {
                SHELLDETAILS details{};
                if (SUCCEEDED(GetDetailsOf(item, 1, &details)))
                {
                    LPWSTR value = nullptr;
                    if (SUCCEEDED(StrRetToStrW(&details.str, item, &value)) && value)
                    {
                        try { name->assign(child->name); text->assign(value); read = true; }
                        catch (const std::bad_alloc&) {}
                        CoTaskMemFree(value);
                    }
                }
            }
            CoTaskMemFree(item);
            return read;
        }
        void FillSizeDisplayText(HWND window, NMLVDISPINFOW* display, const LVITEMW& request)
        {
            if (!_shellView || !display || !request.pszText ||
                request.pszText == LPSTR_TEXTCALLBACKW || request.cchTextMax <= 0 ||
                !IsSizeDisplayColumn(request.iSubItem)) return;
            std::wstring name, text;
            if (!ReadSizeDisplayRow(request.iItem, &name, &text) || _notificationWindow != window) return;
            // Read cached/asynchronous values only. The Shell retains row ownership,
            // selection, hit testing and all ordinary file-item text.
            // The default handler may substitute its own string pointer. Write only
            // into the caller's original buffer, never into Shell-owned text storage.
            StringCchCopyNW(request.pszText, request.cchTextMax, text.c_str(), request.cchTextMax - 1);
            display->item.pszText = request.pszText;
            display->item.cchTextMax = request.cchTextMax;
            display->item.mask &= ~LVIF_DI_SETITEM;
            if (_sizeDisplayRows.size() >= 256 && !_sizeDisplayRows.count(display->item.iItem))
                _sizeDisplayRows.erase(_sizeDisplayRows.begin());
            _sizeDisplayRows.insert_or_assign(display->item.iItem, std::make_pair(std::move(name), std::move(text)));
            ED_SIZE_TRACE("display-text-filled", this, display->item.pszText, display->item.iItem, display->item.iSubItem, 0);
        }
        void RefreshSizeDisplayRows(HWND window)
        {
            if (_refreshingSizeDisplay || _notificationWindow != window || !_shellView) return;
            _refreshingSizeDisplay = true;
            try
            {
                auto rows = _sizeDisplayRows;
                for (const auto& entry : rows)
                {
                    std::wstring name, text;
                    if (_notificationWindow != window) break;
                    if (!ReadSizeDisplayRow(entry.first, &name, &text) || name != entry.second.first)
                    { _sizeDisplayRows.erase(entry.first); continue; }
                    if (_notificationWindow != window) break;
                    if (text == entry.second.second) continue;
                    _sizeDisplayRows.insert_or_assign(entry.first, std::make_pair(name, text));
                    if (_sizeDisplayList && IsWindow(_sizeDisplayList) && IsChild(window, _sizeDisplayList))
                        SendMessageW(_sizeDisplayList, LVM_REDRAWITEMS, entry.first, entry.first);
                }
            }
            catch (const std::bad_alloc&) {}
            _refreshingSizeDisplay = false;
        }
#endif
        static UINT BackingNotificationMessage()
        {
            static const UINT message = RegisterWindowMessageW(L"ExplorerDirs.BackingFolderChange.v1");
            return message;
        }
        HRESULT AttachBackingNotifications(HWND window)
        {
            if (!window || !_backingRoot || !_root) return E_INVALIDARG;
            if (_notificationWindow) return _notificationWindow == window ? S_FALSE : E_UNEXPECTED;
            if (!BackingNotificationMessage() || !SetWindowSubclass(window, BackingNotificationProc,
                reinterpret_cast<UINT_PTR>(this), reinterpret_cast<DWORD_PTR>(this))) return E_FAIL;
            // This reference belongs to the HWND, not to the Shell view COM object.
            AddRef();
            _notificationWindow = window;
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
            _sizeDisplayWindow.store(window);
#endif
            SHChangeNotifyEntry entry{ _backingRoot, FALSE };
            _backingNotificationRegistration = SHChangeNotifyRegister(window,
                SHCNRF_ShellLevel | SHCNRF_InterruptLevel | SHCNRF_NewDelivery,
                SHCNE_ALLEVENTS, BackingNotificationMessage(), 1, &entry);
            ED_SIZE_TRACE("physical-view-subscription", this, nullptr, _backingNotificationRegistration, 0, 0);
            if (!_backingNotificationRegistration)
            {
                _notificationWindow = nullptr;
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
                _sizeDisplayWindow.store(nullptr);
#endif
                RemoveWindowSubclass(window, BackingNotificationProc, reinterpret_cast<UINT_PTR>(this));
                Release();
                return E_FAIL;
            }
            return S_OK;
        }
        PIDLIST_ABSOLUTE ProviderNotificationIdentity(PCIDLIST_ABSOLUTE physical, LONG event) const
        {
            if (!physical || !_root || !_backingRoot) return nullptr;
            if (ILIsEqual(physical, _backingRoot)) return ILCloneFull(_root);
            if (!ILIsParent(_backingRoot, physical, TRUE)) return nullptr;
            LPWSTR path = nullptr;
            if (FAILED(SHGetNameFromIDList(physical, SIGDN_FILESYSPATH, &path)) || !path) return nullptr;
            SyntheticChild child{};
            child.cb = sizeof(child);
            child.id = 1;
            const auto copied = StringCchCopyW(child.name, ARRAYSIZE(child.name), PathFindFileNameW(path));
            const auto kind = event & ~SHCNE_INTERRUPT;
            child.attributes = kind == SHCNE_MKDIR || kind == SHCNE_RMDIR || kind == SHCNE_RENAMEFOLDER
                ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            WIN32_FIND_DATAW data{};
            const auto search = FindFirstFileW(path, &data);
            if (search != INVALID_HANDLE_VALUE)
            {
                FindClose(search);
                child.attributes = data.dwFileAttributes;
                child.size = (static_cast<ULONGLONG>(data.nFileSizeHigh) << 32) | data.nFileSizeLow;
                if (child.attributes & FILE_ATTRIBUTE_DIRECTORY) TryGetSyntheticFileIdentity(path, &child.identity);
            }
            CoTaskMemFree(path);
            // Deleted items have no filesystem metadata, but their name still identifies the row.
            if (FAILED(copied) || !*child.name) return nullptr;
            auto* relative = CloneSyntheticChild(child);
            if (!relative) return nullptr;
            auto* absolute = ILCombine(_root, relative);
            CoTaskMemFree(relative);
            return absolute;
        }
        void ForwardBackingNotification(PCIDLIST_ABSOLUTE first, PCIDLIST_ABSOLUTE second, LONG event)
        {
            ED_SIZE_TRACE("physical-view-event", this, nullptr, event, 0, 0);
            const auto kind = event & ~SHCNE_INTERRUPT;
            if (!first || !_notificationWindow) return;
            // Retain the existing cache invalidation behavior for physical paths.
            PCIDLIST_ABSOLUTE physical[] = { first, second };
            MessageSFVCB(SFVM_FSNOTIFY, reinterpret_cast<WPARAM>(physical), kind);
            if (!_notificationWindow) return;
            auto* translatedFirst = ProviderNotificationIdentity(first, kind);
            auto* translatedSecond = second ? ProviderNotificationIdentity(second, kind) : nullptr;
            if (translatedFirst && (!second || translatedSecond))
            {
                ED_SIZE_TRACE("provider-view-forwarded", this, nullptr, kind, 0, 0);
                SHChangeNotify(kind, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, translatedFirst, translatedSecond);
            }
            else if (ILIsEqual(first, _backingRoot) || ILIsParent(_backingRoot, first, FALSE))
                SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, _root, nullptr);
            if (translatedFirst) CoTaskMemFree(translatedFirst);
            if (translatedSecond) CoTaskMemFree(translatedSecond);
        }
        static LRESULT CALLBACK BackingNotificationProc(HWND window, UINT message, WPARAM wParam,
            LPARAM lParam, UINT_PTR subclassId, DWORD_PTR data)
        {
            auto* self = reinterpret_cast<SyntheticSortFolder*>(data);
            if (message == WM_NCDESTROY)
            {
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
                self->_sizeDisplayWindow.store(nullptr);
                self->_sizeDisplayRows.clear();
                self->_sizeDisplayList = nullptr;
#endif
                self->_notificationWindow = nullptr;
                const auto registration = self->_backingNotificationRegistration;
                self->_backingNotificationRegistration = 0;
                if (registration) SHChangeNotifyDeregister(registration);
                RemoveWindowSubclass(window, BackingNotificationProc, subclassId);
                self->ReleaseViewHold();
                const auto result = DefSubclassProc(window, message, wParam, lParam);
                self->Release();
                return result;
            }
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
            if (message == WM_NOTIFY && lParam)
            {
                auto* header = reinterpret_cast<NMHDR*>(lParam);
                WCHAR className[64]{};
                if (header->code == LVN_GETDISPINFOW &&
                    GetClassNameW(header->hwndFrom, className, ARRAYSIZE(className)) &&
                    wcscmp(className, L"SysListView32") == 0)
                {
                    auto* display = reinterpret_cast<NMLVDISPINFOW*>(lParam);
                    const auto request = display->item;
                    const bool requestedText = (display->item.mask & LVIF_TEXT) != 0;
                    self->AddRef();
                    const auto result = DefSubclassProc(window, message, wParam, lParam);
                    if (requestedText && self->_notificationWindow == window)
                    {
                        self->_sizeDisplayList = header->hwndFrom;
                        try { self->FillSizeDisplayText(window, display, request); }
                        catch (...) { TraceSyntheticSortStage("size-display: text-read-failed"); }
                    }
                    self->Release();
                    return result;
                }
            }
            if (message == SizeDisplayMessage() && wParam == self->_sizeDisplayCookie)
            {
                self->AddRef(); self->RefreshSizeDisplayRows(window); self->Release();
                return 0;
            }
#endif
            if (message == BackingNotificationMessage())
            {
                PIDLIST_ABSOLUTE* pidls = nullptr;
                LONG event = 0;
                const auto lock = SHChangeNotification_Lock(reinterpret_cast<HANDLE>(wParam),
                    static_cast<DWORD>(lParam), &pidls, &event);
                if (lock)
                {
                    auto* first = pidls && pidls[0] ? ILCloneFull(pidls[0]) : nullptr;
                    const auto kind = event & ~SHCNE_INTERRUPT;
                    const bool rename = kind == SHCNE_RENAMEITEM || kind == SHCNE_RENAMEFOLDER;
                    auto* second = rename && pidls && pidls[1] ? ILCloneFull(pidls[1]) : nullptr;
                    SHChangeNotification_Unlock(lock);
                    // Parsing can reenter window destruction; hold the callback until all clones are released.
                    self->AddRef();
                    try
                    {
                        self->ForwardBackingNotification(first, second, event);
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
                        self->RefreshSizeDisplayRows(window);
#endif
                    }
                    catch (...) { TraceSyntheticSortStage("backing-notification: forwarding-failed"); }
                    if (first) CoTaskMemFree(first);
                    if (second) CoTaskMemFree(second);
                    self->Release();
                }
                return 0;
            }
            return DefSubclassProc(window, message, wParam, lParam);
        }
#endif
        void ReleaseViewHold()
        {
            InterlockedExchange(&_asyncNotificationsEnabled, 0);
            InterlockedExchange(&_asyncScansCancelled, 1);
            auto* view = _shellView;
            _shellView = nullptr;
#if !defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
            if (view) view->Release();
#else
            (void)view; // Product views are borrowed, not AddRef'd by their callback.
#endif
        }
        bool CancelPendingRefresh()
        {
            if (InterlockedExchange(&_refreshPending, 0) == 0) return false;
            InterlockedExchange(&_refreshAgain, 0);
            const auto timerId = _refreshTimerId;
            _refreshTimerId = 0;
            if (timerId) KillTimer(nullptr, timerId);
            Release();
            return true;
        }
        bool OwnsShellView(IShellView* view) const { return view && _shellView == view; }
        HRESULT CancelPendingRefreshForProbe() { return CancelPendingRefresh() ? S_OK : S_FALSE; }
        HRESULT ScheduleRefreshForProbe() { return ScheduleRefresh(); }
        HRESULT ScheduleBackingDirectoryUpdateForProbe() { return ScheduleBackingDirectoryUpdate(); }
        HRESULT CancelPendingBackingDirectoryUpdateForProbe() { return CancelPendingBackingDirectoryUpdate() ? S_OK : S_FALSE; }
        bool CancelPendingBackingDirectoryUpdate()
        {
            if (InterlockedExchange(&_directoryUpdatePending, 0) == 0) return false;
            const auto timerId = _directoryUpdateTimerId;
            _directoryUpdateTimerId = 0;
            if (timerId) KillTimer(nullptr, timerId);
            Release();
            return true;
        }
        HRESULT ScheduleBackingDirectoryUpdate()
        {
            if (!_backingRoot) return E_UNEXPECTED;
            if (InterlockedCompareExchange(&_directoryUpdatePending, 1, 0) != 0) return S_FALSE;
            AddRef();
            const auto timerId = SetTimer(nullptr, 0, 1, BackingDirectoryUpdateTimerProc);
            if (!timerId)
            {
                InterlockedExchange(&_directoryUpdatePending, 0);
                Release();
                return HRESULT_FROM_WIN32(GetLastError());
            }
            _directoryUpdateTimerId = timerId;
            ED_PROBE_METRIC_INCREMENT(g_syntheticChildNotificationTranslationQueued);
            TraceSyntheticSortStage("synthetic-sort: child-notification-parent-update-queued");
            return S_OK;
        }
        HRESULT NotifyRefreshForProbe() { return MessageSFVCB(SFVM_FSNOTIFY, 0, 0); }
    private:
        HRESULT BackingColumnOrdinal(UINT column, UINT* backingColumn) const
        {
            if (!backingColumn) return E_POINTER;
            if (!_inner) return E_INVALIDARG;
            const auto customCount = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) ? 4u : 3u;
            if (column < customCount) return E_INVALIDARG;
            const auto requested = column - customCount;
            {
                std::lock_guard<std::mutex> lock(_backingColumnMutex);
                if (_backingColumnMapReady)
                {
                    if (requested >= _backingColumnOrdinals.size()) return E_INVALIDARG;
                    *backingColumn = _backingColumnOrdinals[requested]; return S_OK;
                }
            }
            try
            {
                // Do not hold the map mutex across COM calls, which may pump callbacks.
                std::vector<UINT> columns;
                for (UINT index = 0; index < 1024; ++index)
                {
                    SHCOLUMNID key{};
                    if (FAILED(_inner->MapColumnToSCID(index, &key))) break;
                    if (key == PKEY_ItemNameDisplay || key == PKEY_Size || key == PKEY_ItemTypeText) continue;
                    columns.push_back(index);
                }
                std::lock_guard<std::mutex> lock(_backingColumnMutex);
                if (!_backingColumnMapReady) { _backingColumnOrdinals.swap(columns); _backingColumnMapReady = true; }
                if (requested >= _backingColumnOrdinals.size()) return E_INVALIDARG;
                *backingColumn = _backingColumnOrdinals[requested]; return S_OK;
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }
        HRESULT BackingChildPidl(PCUIDLIST_RELATIVE item, PITEMID_CHILD* original) const
        {
            if (!original) return E_POINTER;
            *original = nullptr;
            if (!_inner || !item || ILIsEmpty(item)) return E_INVALIDARG;
            try
            {
                std::wstring relative;
                for (auto* segment = item; !ILIsEmpty(segment); segment = ILNext(segment))
                {
                    const auto* child = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(segment));
                    if (!child) return E_INVALIDARG;
                    if (!relative.empty()) relative.push_back(L'\\');
                    relative.append(child->name);
                }
                return _inner->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(relative.c_str()), nullptr, original, nullptr);
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }
        bool TryGetLocalRecursiveSize(const SyntheticChild* child, ULONGLONG* bytes,
            ULONGLONG* unreadable = nullptr, bool* available = nullptr) const
        {
            if (!child || !_recursiveSizeStore || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
                !child->identity.available || _backingPath.empty()) return false;
            // Warm display/property reads used to reparse every child through Shell
            // even after sorting had found its cached value. Reuse that exact fast
            // key and identity check; misses still use the canonical Shell fallback.
            thread_local std::wstring parsingName;
            try
            {
                parsingName.assign(_backingPath);
                if (parsingName.back() != L'\\' && parsingName.back() != L'/') parsingName.push_back(L'\\');
                parsingName.append(child->name);
                return _recursiveSizeStore->TryGet(parsingName, child->identity, bytes,
                    nullptr, nullptr, unreadable, available);
            }
            catch (const std::bad_alloc&) { return false; }
        }
        HRESULT GetAbsoluteParsingName(const SyntheticChild* child, std::wstring* parsingName) const
        {
            if (!child || !parsingName || !_inner || !_backingRoot) return E_INVALIDARG;
            PITEMID_CHILD backingChild = nullptr;
            auto result = _inner->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(child->name),
                nullptr, &backingChild, nullptr);
            if (FAILED(result))
            {
                if (backingChild) CoTaskMemFree(backingChild);
                return result;
            }
            auto* absolutePidl = ILCombine(_backingRoot, reinterpret_cast<PCUIDLIST_RELATIVE>(backingChild));
            CoTaskMemFree(backingChild);
            if (!absolutePidl) return E_OUTOFMEMORY;
            LPWSTR name = nullptr;
            result = SHGetNameFromIDList(absolutePidl, static_cast<SIGDN>(0x80028000), &name);
            CoTaskMemFree(absolutePidl);
            if (FAILED(result))
            {
                if (name) CoTaskMemFree(name);
                return result;
            }
            try { parsingName->assign(name); }
            catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; }
            CoTaskMemFree(name);
            return result;
        }
        HRESULT EnsureRecursiveSizeSnapshot(const SyntheticChild* child) const
        {
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
                (child->attributes & FILE_ATTRIBUTE_REPARSE_POINT)) return E_INVALIDARG;
            if (!_recursiveSizeStore) return E_OUTOFMEMORY;
            std::wstring parsingName;
            auto result = GetAbsoluteParsingName(child, &parsingName);
            if (FAILED(result)) return result;
            ULONGLONG cachedBytes = 0;
            if (_recursiveSizeStore->TryGet(parsingName, child->identity, &cachedBytes)) return S_OK;

            std::wstring childPath;
            try { childPath = _backingPath + L"\\" + child->name; }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            SyntheticFileIdentity beforeScan{};
            if (!TryGetSyntheticFileIdentity(childPath.c_str(), &beforeScan))
                return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            const SyntheticFileIdentityLess less{};
            if (!beforeScan.available || less(beforeScan, child->identity) || less(child->identity, beforeScan))
                return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);

            SyntheticDirectoryMetrics metrics{};
            LARGE_INTEGER frequency{}, scanStart{}, scanEnd{};
            QueryPerformanceFrequency(&frequency);
            QueryPerformanceCounter(&scanStart);
            result = GetSyntheticDirectoryMetrics(childPath, &metrics);
            QueryPerformanceCounter(&scanEnd);
            ED_PROBE_METRIC_INCREMENT(g_syntheticOnDemandSizeScanCount);
            if (frequency.QuadPart > 0 && scanEnd.QuadPart >= scanStart.QuadPart)
            {
                const auto elapsedTicks = scanEnd.QuadPart - scanStart.QuadPart;
                const auto elapsedMicroseconds = static_cast<LONG64>(
                    static_cast<long double>(elapsedTicks) * 1000000.0L / static_cast<long double>(frequency.QuadPart));
                ED_PROBE_METRIC_ADD64(g_syntheticOnDemandSizeScanMicroseconds, elapsedMicroseconds);
            }
            if (FAILED(result)) return result;
            SyntheticFileIdentity afterScan{};
            if (!TryGetSyntheticFileIdentity(childPath.c_str(), &afterScan))
                return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            if (!afterScan.available || less(beforeScan, afterScan) || less(afterScan, beforeScan))
                return HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
            return _recursiveSizeStore->Set(parsingName.c_str(), afterScan, metrics.bytes, metrics.files,
                metrics.folders, metrics.unreadable);
        }
        HRESULT ScheduleRecursiveSizeSnapshot(const SyntheticChild* child) const
        {
            ED_SIZE_TRACE("schedule-size", this, child ? child->name : nullptr, 0,
                child ? child->attributes : 0, child ? child->size : 0);
            const auto providerGeneration = InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0);
            if (!InterlockedCompareExchange(&g_folderSizeEnabled, 0, 0)) return S_OK;
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
                (child->attributes & FILE_ATTRIBUTE_REPARSE_POINT)) return E_INVALIDARG;
            if (!_recursiveSizeStore) return E_OUTOFMEMORY;

            if (InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeBatchByViewForProbe, 0, 0))
                return ScheduleRecursiveSizeBatchForView(child);

            ULONGLONG cachedBytes = 0;
            if (TryGetLocalRecursiveSize(child, &cachedBytes)) return S_OK;
            std::wstring parsingName;
            auto result = GetAbsoluteParsingName(child, &parsingName);
            if (FAILED(result)) return result;
            if (_recursiveSizeStore->TryGet(parsingName, child->identity, &cachedBytes)) return S_OK;

            std::wstring childPath;
            try { childPath = _backingPath + L"\\" + child->name; }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            const auto identity = child->identity;
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                try
                {
                    if (!_asyncSizeScansPending.insert(parsingName).second) return S_FALSE;
                }
                catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            }

            auto* relativeNotificationPidl = CloneSyntheticChild(*child);
            if (!relativeNotificationPidl)
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                _asyncSizeScansPending.erase(parsingName);
                return E_OUTOFMEMORY;
            }
            auto* absoluteNotificationPidl = ILCombine(_root, reinterpret_cast<PCUIDLIST_RELATIVE>(relativeNotificationPidl));
            CoTaskMemFree(relativeNotificationPidl);
            if (!absoluteNotificationPidl)
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                _asyncSizeScansPending.erase(parsingName);
                return E_OUTOFMEMORY;
            }
            std::shared_ptr<void> notificationPidl;
            try
            {
                notificationPidl = std::shared_ptr<void>(absoluteNotificationPidl,
                    [](void* pidl) { CoTaskMemFree(pidl); });
            }
            catch (const std::bad_alloc&)
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                _asyncSizeScansPending.erase(parsingName);
                return E_OUTOFMEMORY;
            }

            auto* self = const_cast<SyntheticSortFolder*>(this);
            self->AddRef();
            HRESULT enqueueResult = E_FAIL;
            try
            {
                // Keep the caller's key intact so a rejected enqueue can remove its pending marker.
                std::function<void()> task = [self, childPath = std::move(childPath), parsingName,
                    identity, providerGeneration, notificationPidl = std::move(notificationPidl)]() mutable
                {
                    HRESULT scanResult = E_FAIL;
                    bool scanReserved = false;
                    const auto comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                    if (SUCCEEDED(comResult))
                    {
                        try
                        {
                            scanResult = FolderSizeScanCancelled(&self->_asyncScansCancelled, providerGeneration)
                                ? HRESULT_FROM_WIN32(ERROR_CANCELLED) : S_OK;
                            while (scanResult == S_OK)
                            {
                                ULONGLONG cachedBytes = 0;
                                const auto reservation = self->_recursiveSizeStore->TryReserveScan(parsingName, identity, &cachedBytes);
                                if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Cached) { scanResult = S_FALSE; break; }
                                if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired) { scanReserved = true; break; }
                                if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Failed) { scanResult = E_OUTOFMEMORY; break; }
                                if (FolderSizeScanCancelled(&self->_asyncScansCancelled, providerGeneration))
                                { scanResult = HRESULT_FROM_WIN32(ERROR_CANCELLED); break; }
                                Sleep(5);
                            }
                            auto gate = g_syntheticAsyncRecursiveSizeScanGateForProbe;
                            if (scanResult == S_OK && gate && WaitForSingleObject(static_cast<HANDLE>(gate), 10000) != WAIT_OBJECT_0)
                                scanResult = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                            if (scanResult == S_OK && InterlockedCompareExchange(&self->_asyncScansCancelled, 0, 0))
                                scanResult = HRESULT_FROM_WIN32(ERROR_CANCELLED);

                            const SyntheticFileIdentityLess less{};
                            SyntheticFileIdentity beforeScan{};
                            SyntheticDirectoryMetrics metrics{};
                            if (scanResult == S_OK && !TryGetSyntheticFileIdentity(childPath.c_str(), &beforeScan))
                                scanResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                            if (scanResult == S_OK && (!beforeScan.available || less(beforeScan, identity) || less(identity, beforeScan)))
                                scanResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                            if (scanResult == S_OK)
                            {
                                LARGE_INTEGER frequency{}, scanStart{}, scanEnd{};
                                QueryPerformanceFrequency(&frequency);
                                QueryPerformanceCounter(&scanStart);
                                const auto scanDelayMs = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanDelayMsForProbe, 0, 0);
                                if (scanDelayMs > 0) Sleep(static_cast<DWORD>(scanDelayMs));
                                scanResult = GetConfiguredDirectoryMetrics(childPath, &metrics, &self->_asyncScansCancelled, providerGeneration);
                                QueryPerformanceCounter(&scanEnd);
                                ED_PROBE_METRIC_INCREMENT(g_syntheticOnDemandSizeScanCount);
                                if (frequency.QuadPart > 0 && scanEnd.QuadPart >= scanStart.QuadPart)
                                {
                                    const auto elapsedTicks = scanEnd.QuadPart - scanStart.QuadPart;
                                    const auto elapsedMicroseconds = static_cast<LONG64>(
                                        static_cast<long double>(elapsedTicks) * 1000000.0L / static_cast<long double>(frequency.QuadPart));
                                    ED_PROBE_METRIC_ADD64(g_syntheticOnDemandSizeScanMicroseconds, elapsedMicroseconds);
                                }
                            }
                            SyntheticFileIdentity afterScan{};
                            if (scanResult == S_OK && !TryGetSyntheticFileIdentity(childPath.c_str(), &afterScan))
                                scanResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                            if (scanResult == S_OK && (!afterScan.available || less(beforeScan, afterScan) || less(afterScan, beforeScan)))
                                scanResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                            if (scanResult == S_OK && InterlockedCompareExchange(&self->_asyncScansCancelled, 0, 0))
                                scanResult = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                            if (scanResult == S_OK)
                                scanResult = self->_recursiveSizeStore->SetForProviderGeneration(
                                    providerGeneration, parsingName.c_str(), afterScan, metrics.bytes, metrics.files, metrics.folders,
                                    metrics.unreadable, metrics.sizeAvailable, metrics.countsAvailable);
                            if (scanResult == S_OK)
                                ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanCompletedForProbe);
                            else if (FAILED(scanResult) && scanResult != HRESULT_FROM_WIN32(ERROR_CANCELLED))
                                TraceSyntheticSortStage("synthetic-on-demand-size: asynchronous-scan-failed");
                        }
                        catch (...)
                        {
                            scanResult = E_FAIL;
                            TraceSyntheticSortStage("synthetic-on-demand-size: asynchronous-scan-threw");
                        }
                        CoUninitialize();
                    }
                    else TraceSyntheticSortStage("synthetic-on-demand-size: worker-com-initialize-failed");

                    if (scanReserved) self->_recursiveSizeStore->ReleaseScan(parsingName);
                    {
                        std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                        self->_asyncSizeScansPending.erase(parsingName);
                    }
                    if (scanResult == S_OK)
                        self->ScheduleAsyncViewUpdateNotification(parsingName.c_str(),
                            reinterpret_cast<PCIDLIST_ABSOLUTE>(notificationPidl.get()));
                    ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanFinishedForProbe);
                    self->Release();
                };
                enqueueResult = GetSyntheticAsyncRecursiveSizeQueue().Enqueue(std::move(task), true);
            }
            catch (const std::bad_alloc&) { enqueueResult = E_OUTOFMEMORY; }
            catch (const std::system_error&) { enqueueResult = E_FAIL; }
            if (FAILED(enqueueResult))
            {
                std::lock_guard<std::mutex> lock(_asyncSizeScanMutex);
                _asyncSizeScansPending.erase(parsingName);
                self->Release();
                return enqueueResult;
            }
            return S_OK;
        }
        struct AsyncRecursiveSizeBatchEntry
        {
            std::wstring path;
            std::wstring key;
            SyntheticFileIdentity identity{};
            LONG providerGeneration{};
        };
        struct AsyncRecursiveSizeBatchCompletion
        {
            LONG outstanding{ 1 };
            LONG anySucceeded{};
        };
        void ProcessRecursiveSizeBatchChunk(const std::vector<AsyncRecursiveSizeBatchEntry>& entries,
            const std::shared_ptr<AsyncRecursiveSizeBatchCompletion>& completion) const
        {
            auto* self = const_cast<SyntheticSortFolder*>(this);
            const SyntheticFileIdentityLess less{};
            for (const auto& entry : entries)
            {
                ULONGLONG cachedBytes = 0;
                if (self->_recursiveSizeStore->TryGet(entry.key, entry.identity, &cachedBytes))
                {
                    std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                    self->_asyncSizeScansPending.erase(entry.key);
                    continue;
                }
                bool scanReserved = false;
                auto itemResult = S_OK;
                for (;;)
                {
                    if (FolderSizeScanCancelled(&self->_asyncScansCancelled, entry.providerGeneration))
                    {
                        itemResult = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                        break;
                    }
                    const auto reservation = self->_recursiveSizeStore->TryReserveScan(entry.key, entry.identity, &cachedBytes);
                    if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Cached)
                    {
                        itemResult = S_FALSE;
                        break;
                    }
                    if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired)
                    {
                        scanReserved = true;
                        break;
                    }
                    if (reservation == SyntheticRecursiveSizeSnapshotStore::ScanReservation::Failed)
                    {
                        itemResult = E_OUTOFMEMORY;
                        break;
                    }
                    Sleep(5);
                }
                if (itemResult == S_FALSE)
                {
                    std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                    self->_asyncSizeScansPending.erase(entry.key);
                    continue;
                }
                SyntheticFileIdentity before{}, after{};
                SyntheticDirectoryMetrics metrics{};
                if (SUCCEEDED(itemResult) && (!TryGetSyntheticFileIdentity(entry.path.c_str(), &before) ||
                    !before.available || less(before, entry.identity) || less(entry.identity, before)))
                    itemResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                if (SUCCEEDED(itemResult))
                {
                    LARGE_INTEGER frequency{}, start{}, end{};
                    QueryPerformanceFrequency(&frequency);
                    QueryPerformanceCounter(&start);
                    ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanStartedForProbe);
                    const auto scanDelayMs = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanDelayMsForProbe, 0, 0);
                    if (scanDelayMs > 0) Sleep(static_cast<DWORD>(scanDelayMs));
                    itemResult = GetConfiguredDirectoryMetrics(entry.path, &metrics, &self->_asyncScansCancelled, entry.providerGeneration);
                    QueryPerformanceCounter(&end);
                    ED_PROBE_METRIC_INCREMENT(g_syntheticOnDemandSizeScanCount);
                    if (frequency.QuadPart > 0 && end.QuadPart >= start.QuadPart)
                        ED_PROBE_METRIC_ADD64(g_syntheticOnDemandSizeScanMicroseconds, static_cast<LONG64>(
                            static_cast<long double>(end.QuadPart - start.QuadPart) * 1000000.0L / frequency.QuadPart));
                }
                if (SUCCEEDED(itemResult) && (!TryGetSyntheticFileIdentity(entry.path.c_str(), &after) ||
                    !after.available || less(before, after) || less(after, before)))
                    itemResult = HRESULT_FROM_WIN32(ERROR_FILE_INVALID);
                if (SUCCEEDED(itemResult) && InterlockedCompareExchange(&self->_asyncScansCancelled, 0, 0))
                    itemResult = HRESULT_FROM_WIN32(ERROR_CANCELLED);
                if (SUCCEEDED(itemResult))
                {
                    HANDLE commitGate = nullptr;
                    HANDLE commitGateReached = nullptr;
                    bool isCommitGateTarget = false;
                    {
                        std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
                        commitGate = g_syntheticAsyncRecursiveSizeCommitGateForProbe;
                        commitGateReached = g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe;
                        const auto& target = g_syntheticAsyncRecursiveSizeCommitGatePathForProbe;
                        isCommitGateTarget = !target.empty() && entry.path.size() == target.size() &&
                            CompareStringOrdinal(entry.path.data(), static_cast<int>(entry.path.size()),
                                target.data(), static_cast<int>(target.size()), TRUE) == CSTR_EQUAL;
                    }
                    if (commitGate && commitGateReached && isCommitGateTarget)
                    {
                        SetEvent(commitGateReached);
                        if (WaitForSingleObject(commitGate, 10000) != WAIT_OBJECT_0)
                            itemResult = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                    }
                }
                if (SUCCEEDED(itemResult)) itemResult = self->_recursiveSizeStore->SetForProviderGeneration(
                    entry.providerGeneration, entry.key.c_str(), after, metrics.bytes, metrics.files, metrics.folders, metrics.unreadable,
                    metrics.sizeAvailable, metrics.countsAvailable);
                if (scanReserved) self->_recursiveSizeStore->ReleaseScan(entry.key);
                // S_FALSE means a notification invalidated this in-flight scan before commit;
                // do not advertise it as a fresh value or trigger a redundant view update.
                if (itemResult == S_OK)
                {
                    InterlockedExchange(&completion->anySucceeded, 1);
                    ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanCompletedForProbe);
                }
                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                self->_asyncSizeScansPending.erase(entry.key);
            }
        }
        void CompleteRecursiveSizeBatchChunk(const std::shared_ptr<AsyncRecursiveSizeBatchCompletion>& completion) const
        {
            if (InterlockedDecrement(&completion->outstanding) == 0)
            {
                if (InterlockedCompareExchange(&completion->anySucceeded, 0, 0)) ScheduleAsyncViewUpdateNotification();
                ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanFinishedForProbe);
            }
        }
        HRESULT ScheduleRecursiveSizeBatchForView(const SyntheticChild* child) const
        {
            auto* self = const_cast<SyntheticSortFolder*>(this);
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
                (child->attributes & FILE_ATTRIBUTE_REPARSE_POINT)) return E_INVALIDARG;
            AsyncRecursiveSizeBatchEntry entry;
            entry.providerGeneration = InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0);
            entry.identity = child->identity;
            auto result = GetAbsoluteParsingName(child, &entry.key);
            if (FAILED(result)) return result;
            ULONGLONG cachedBytes = 0;
            if (_recursiveSizeStore->TryGet(entry.key, entry.identity, &cachedBytes)) return S_OK;
            try { entry.path = _backingPath + L"\\" + child->name; }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }

            bool enqueue = false;
            {
                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                try
                {
                    if (!self->_asyncSizeScansPending.insert(entry.key).second) return S_FALSE;
                    self->_asyncSizeScanBatch.push_back(std::move(entry));
                    ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeBatchRequestedEntriesForProbe);
                }
                catch (const std::bad_alloc&)
                {
                    self->_asyncSizeScansPending.erase(entry.key);
                    return E_OUTOFMEMORY;
                }
                enqueue = InterlockedCompareExchange(&self->_asyncParentBatchPending, 1, 0) == 0;
            }
            if (!enqueue) return S_FALSE;

            self->AddRef();
            HRESULT enqueueResult = E_FAIL;
            try
            {
                std::function<void()> task = [self]
                {
                    std::shared_ptr<AsyncRecursiveSizeBatchCompletion> completion;
                    HRESULT scanResult = E_OUTOFMEMORY;
                    try { completion = std::make_shared<AsyncRecursiveSizeBatchCompletion>(); }
                    catch (const std::bad_alloc&) {}
                    const auto comResult = completion ? CoInitializeEx(nullptr, COINIT_MULTITHREADED) : E_OUTOFMEMORY;
                    if (completion && SUCCEEDED(comResult))
                    {
                        auto gate = g_syntheticAsyncRecursiveSizeScanGateForProbe;
                        if (gate && WaitForSingleObject(static_cast<HANDLE>(gate), 10000) != WAIT_OBJECT_0)
                            scanResult = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                        else scanResult = S_OK;
                        if (SUCCEEDED(scanResult)) Sleep(25); // Coalesce Shell's synchronous visible-row requests into one view task.
                        while (SUCCEEDED(scanResult))
                        {
                            std::vector<AsyncRecursiveSizeBatchEntry> entries;
                            {
                                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                                entries.swap(self->_asyncSizeScanBatch);
                            }
                            if (entries.empty())
                            {
                                Sleep(50); // Keep the same queue task through a short gap in Shell's row requests.
                                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                                if (self->_asyncSizeScanBatch.empty())
                                {
                                    InterlockedExchange(&self->_asyncParentBatchPending, 0);
                                    break;
                                }
                                entries.swap(self->_asyncSizeScanBatch);
                            }
                            if (entries.empty()) continue;

                            // A view stays represented by one queue task; one bounded child chunk uses the second shared worker.
                            std::vector<AsyncRecursiveSizeBatchEntry> childEntries;
                            bool childQueued = false;
                            if (entries.size() > 1)
                            {
                                const auto childCount = entries.size() / 2;
                                try
                                {
                                    childEntries.reserve(childCount);
                                    for (size_t index = 0; index < childCount; ++index)
                                        childEntries.push_back(entries[index]);
                                }
                                catch (const std::bad_alloc&) { childEntries.clear(); }
                                if (!childEntries.empty() &&
                                    InterlockedCompareExchange(&self->_asyncChildBatchPending, 1, 0) == 0)
                                {
                                    self->AddRef();
                                    InterlockedIncrement(&completion->outstanding);
                                    HRESULT childEnqueueResult = E_FAIL;
                                    try
                                    {
                                        std::function<void()> childTask = [self, completion, childEntries = std::move(childEntries)]() mutable
                                        {
                                            const auto childComResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                                            if (SUCCEEDED(childComResult))
                                            {
                                                self->ProcessRecursiveSizeBatchChunk(childEntries, completion);
                                                CoUninitialize();
                                            }
                                            else
                                            {
                                                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                                                for (const auto& entry : childEntries) self->_asyncSizeScansPending.erase(entry.key);
                                                TraceSyntheticSortStage("synthetic-on-demand-size: batch-child-com-initialize-failed");
                                            }
                                            InterlockedExchange(&self->_asyncChildBatchPending, 0);
                                            self->CompleteRecursiveSizeBatchChunk(completion);
                                            self->Release();
                                        };
                                        childEnqueueResult = GetSyntheticAsyncRecursiveSizeQueue().Enqueue(std::move(childTask), true);
                                    }
                                    catch (const std::bad_alloc&) { childEnqueueResult = E_OUTOFMEMORY; }
                                    catch (const std::system_error&) { childEnqueueResult = E_FAIL; }
                                    if (SUCCEEDED(childEnqueueResult)) childQueued = true;
                                    else
                                    {
                                        self->Release();
                                        InterlockedDecrement(&completion->outstanding);
                                        InterlockedExchange(&self->_asyncChildBatchPending, 0);
                                        childEntries.clear();
                                    }
                                }
                            }

                            if (childQueued)
                                entries.erase(entries.begin(), entries.begin() + static_cast<std::ptrdiff_t>(entries.size() / 2));
                            self->ProcessRecursiveSizeBatchChunk(entries, completion);
                            if (InterlockedCompareExchange(&self->_asyncScansCancelled, 0, 0))
                            {
                                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                                for (const auto& entry : self->_asyncSizeScanBatch)
                                    self->_asyncSizeScansPending.erase(entry.key);
                                self->_asyncSizeScanBatch.clear();
                                InterlockedExchange(&self->_asyncParentBatchPending, 0);
                                break;
                            }
                        }
                        CoUninitialize();
                    }
                    else
                    {
                        std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                        for (const auto& pendingEntry : self->_asyncSizeScanBatch)
                            self->_asyncSizeScansPending.erase(pendingEntry.key);
                        self->_asyncSizeScanBatch.clear();
                        InterlockedExchange(&self->_asyncParentBatchPending, 0);
                        TraceSyntheticSortStage("synthetic-on-demand-size: batch-worker-com-initialize-failed");
                    }
                    InterlockedExchange(&self->_asyncParentBatchPending, 0);
                    if (completion) self->CompleteRecursiveSizeBatchChunk(completion);
                    else ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeScanFinishedForProbe);
                    self->Release();
                };
                enqueueResult = GetSyntheticAsyncRecursiveSizeQueue().Enqueue(std::move(task), true);
            }
            catch (const std::bad_alloc&) { enqueueResult = E_OUTOFMEMORY; }
            catch (const std::system_error&) { enqueueResult = E_FAIL; }
            if (FAILED(enqueueResult))
            {
                std::lock_guard<std::mutex> lock(self->_asyncSizeScanMutex);
                for (const auto& pendingEntry : self->_asyncSizeScanBatch)
                    self->_asyncSizeScansPending.erase(pendingEntry.key);
                self->_asyncSizeScanBatch.clear();
                InterlockedExchange(&self->_asyncParentBatchPending, 0);
                self->Release();
                return enqueueResult;
            }
            return S_OK;
        }
        void ScheduleAsyncViewUpdateNotification(PCWSTR itemParsingName = nullptr,
            PCIDLIST_ABSOLUTE itemPidl = nullptr) const
        {
            auto* self = const_cast<SyntheticSortFolder*>(this);
            if (!InterlockedCompareExchange(&self->_asyncNotificationsEnabled, 0, 0) || !self->_backingRoot) return;
            const bool itemNotificationMode = InterlockedCompareExchange(
                &g_syntheticAsyncRecursiveSizeItemNotificationForProbe, 0, 0) && itemParsingName && *itemParsingName && itemPidl;
            PIDLIST_ABSOLUTE pendingItemPidl = itemNotificationMode ? ILCloneFull(itemPidl) : nullptr;
            bool enqueueTask = false;
            bool itemNotificationQueued = false;
            {
                std::lock_guard<std::mutex> lock(self->_asyncViewUpdateMutex);
                if (itemNotificationMode && pendingItemPidl)
                {
                    // Keep every changed row in the coalescing window; the prior single-pending flag dropped later rows.
                    try
                    {
                        const auto insertion = self->_asyncPendingItemNotifications.emplace(itemParsingName, pendingItemPidl);
                        itemNotificationQueued = true;
                        if (insertion.second) pendingItemPidl = nullptr;
                    }
                    catch (const std::bad_alloc&) {}
                }
                if (!itemNotificationMode || !itemNotificationQueued)
                {
                    self->_asyncPendingDirectoryNotification = true;
                    for (const auto& pending : self->_asyncPendingItemNotifications) CoTaskMemFree(pending.second);
                    self->_asyncPendingItemNotifications.clear();
                }
                if (!self->_asyncViewUpdatePending)
                {
                    self->_asyncViewUpdatePending = true;
                    self->AddRef();
                    enqueueTask = true;
                }
            }
            if (!enqueueTask)
            {
                CoTaskMemFree(pendingItemPidl);
                return;
            }
            HRESULT enqueueResult = E_FAIL;
            try
            {
                std::function<void()> task = [self]
                {
                    Sleep(SyntheticAsyncRecursiveSizeQueue::kShellUpdateCoalesceMilliseconds);
                    for (;;)
                    {
                        bool notifyDirectory = false;
                        std::map<std::wstring, PIDLIST_ABSOLUTE, ShellParsingNameLess> itemNotifications;
                        {
                            std::lock_guard<std::mutex> lock(self->_asyncViewUpdateMutex);
                            if (!InterlockedCompareExchange(&self->_asyncNotificationsEnabled, 0, 0) || !self->_backingRoot)
                            {
                                self->_asyncPendingDirectoryNotification = false;
                                for (const auto& pending : self->_asyncPendingItemNotifications) CoTaskMemFree(pending.second);
                                self->_asyncPendingItemNotifications.clear();
                                self->_asyncViewUpdatePending = false;
                                break;
                            }
                            notifyDirectory = self->_asyncPendingDirectoryNotification;
                            self->_asyncPendingDirectoryNotification = false;
                            itemNotifications.swap(self->_asyncPendingItemNotifications);
                            if (notifyDirectory)
                            {
                                for (const auto& pending : itemNotifications) CoTaskMemFree(pending.second);
                                itemNotifications.clear();
                            }
                            if (!notifyDirectory && itemNotifications.empty())
                            {
                                self->_asyncViewUpdatePending = false;
                                break;
                            }
                        }

#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
                        if (const auto window = self->_sizeDisplayWindow.load())
                            PostMessageW(window, SizeDisplayMessage(), self->_sizeDisplayCookie, 0);
#endif
                        if (notifyDirectory)
                        {
                            ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeNotificationsSentForProbe);
                            ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeDirectoryNotificationsSentForProbe);
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                            auto* directoryRoot =
                                InterlockedCompareExchange(&g_productViewNotificationsUseSyntheticRoot, 0, 0) && self->_root
                                    ? self->_root : self->_backingRoot;
                            UINT notifyFlags = SHCNF_IDLIST;
                            if (InterlockedCompareExchange(&g_productNotificationFlushForProbe, 0, 0))
                                notifyFlags |= SHCNF_FLUSH;
                            const auto delay = InterlockedCompareExchange(&g_productNotificationDelayMsForProbe, 0, 0);
                            if (delay > 0) Sleep(static_cast<DWORD>(delay));
                            // The HostProbe correlates row-count samples with the actual notification call, not its artificial delay.
                            InterlockedIncrement(&g_productAsyncDirectoryNotificationAttempts);
                            SHChangeNotify(SHCNE_UPDATEDIR, notifyFlags, directoryRoot, nullptr);
#else
                            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_IDLIST, self->_backingRoot, nullptr);
#endif
                        }
                        else
                        {
                            for (const auto& notification : itemNotifications)
                            {
                                ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeNotificationsSentForProbe);
                                ED_PROBE_METRIC_INCREMENT(g_syntheticAsyncRecursiveSizeItemNotificationsSentForProbe);
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                                RecordProductAsyncItemNotificationIdentity(
                                    reinterpret_cast<PCIDLIST_ABSOLUTE>(notification.second));
                                if (InterlockedCompareExchange(&g_productNotificationIdentityCaptureEnabled, 0, 0) && self->_root && ILIsParent(self->_root,
                                    reinterpret_cast<PCIDLIST_ABSOLUTE>(notification.second), TRUE))
                                    InterlockedIncrement(&g_productAsyncItemNotificationUnderSyntheticRoot);
#endif
                                UINT notifyFlags = SHCNF_IDLIST;
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                                if (InterlockedCompareExchange(&g_productNotificationFlushForProbe, 0, 0))
                                    notifyFlags |= SHCNF_FLUSH;
                                const auto delay = InterlockedCompareExchange(&g_productNotificationDelayMsForProbe, 0, 0);
                                if (delay > 0) Sleep(static_cast<DWORD>(delay));
                                // Keep the diagnostic counter aligned with entry to SHChangeNotify for time-correlated sampling.
                                InterlockedIncrement(&g_productAsyncItemNotificationAttempts);
#endif
                                SHChangeNotify(SHCNE_UPDATEITEM, notifyFlags, notification.second, nullptr);
                                CoTaskMemFree(notification.second);
                            }
                        }
                    }
                    self->Release();
                };
                enqueueResult = GetSyntheticAsyncRecursiveSizeQueue().Enqueue(std::move(task));
            }
            catch (const std::bad_alloc&) { enqueueResult = E_OUTOFMEMORY; }
            catch (const std::system_error&) { enqueueResult = E_FAIL; }
            if (FAILED(enqueueResult))
            {
                std::lock_guard<std::mutex> lock(self->_asyncViewUpdateMutex);
                self->_asyncPendingDirectoryNotification = false;
                for (const auto& pending : self->_asyncPendingItemNotifications) CoTaskMemFree(pending.second);
                self->_asyncPendingItemNotifications.clear();
                self->_asyncViewUpdatePending = false;
                CoTaskMemFree(pendingItemPidl);
                self->Release();
            }
            else CoTaskMemFree(pendingItemPidl);
        }
        ULONGLONG SizeForItem(PCUITEMID_CHILD item) const
        {
            const auto* child = SyntheticData(item);
            if (!child) return 0;
            if (_recursiveSizeStore && (child->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
                child->identity.available && !_backingPath.empty())
            {
                ULONGLONG bytes = 0;
                if (TryGetLocalRecursiveSize(child, &bytes))
                {
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                    InterlockedIncrement64(&g_productRecursiveCompareFastPathHits);
#endif
                    return bytes;
                }
#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
                InterlockedIncrement64(&g_productRecursiveCompareFastPathMisses);
#endif
            }
            std::wstring parsingName;
            if (_recursiveSizeStore && SUCCEEDED(GetAbsoluteParsingName(child, &parsingName)))
            {
                ULONGLONG bytes = 0;
                if (_recursiveSizeStore->TryGet(parsingName, child->identity, &bytes)) return bytes;
                if (g_syntheticCompareCallbackActive && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                    ED_PROBE_METRIC_INCREMENT(g_syntheticRecursiveSizeCacheMissesDuringCompareCount);
            }
            return SyntheticSize(child);
        }
        std::wstring RecursiveSizeTextForItem(PCUITEMID_CHILD item) const
        {
            const auto bytes = SizeForItem(item);
            const auto* child = SyntheticData(item);
            if (!child || !_recursiveSizeStore) return FormatSyntheticSizeText(bytes);
            std::wstring parsingName;
            ULONGLONG cachedBytes = 0, unreadable = 0;
            bool available = true;
            if (!TryGetLocalRecursiveSize(child, &cachedBytes, &unreadable, &available) &&
                (FAILED(GetAbsoluteParsingName(child, &parsingName)) ||
                !_recursiveSizeStore->TryGet(parsingName, child->identity, &cachedBytes, nullptr, nullptr, &unreadable, &available)))
                return FormatSyntheticSizeText(bytes);
            if (!available) return L"\x2014";
            if (unreadable == 0) return FormatSyntheticSizeText(bytes);
            return L">= " + FormatSyntheticSizeText(cachedBytes) + L" (partial; skipped " +
                std::to_wstring(unreadable) + L")";
        }
        std::wstring ContentsForItem(PCUITEMID_CHILD item) const
        {
            const auto* child = SyntheticData(item);
            if (!child) return {};
            std::wstring parsingName;
            if (_recursiveSizeStore && SUCCEEDED(GetAbsoluteParsingName(child, &parsingName)))
            {
                ULONGLONG bytes = 0, files = 0, folders = 0, unreadable = 0;
                bool sizeAvailable = true, countsAvailable = true;
                if (_recursiveSizeStore->TryGet(parsingName, child->identity, &bytes, &files, &folders, &unreadable,
                    &sizeAvailable, &countsAvailable))
                {
                    if (!sizeAvailable || !countsAvailable) return L"";
                    auto text = SyntheticContentsText(files, folders);
                    if (unreadable) text += L"; unreadable/skipped: " + std::to_wstring(unreadable);
                    return text;
                }
            }
            return SyntheticContentsText(child);
        }
        double RelativePercentForItem(PCUITEMID_CHILD item) const
        {
            const auto* child = SyntheticData(item);
            if (!child) return 0.0;
            const auto parentBytes = child->parentBytes;
            const auto bytes = SizeForItem(item);
            return parentBytes != 0 ? 100.0 * static_cast<double>(bytes) / static_cast<double>(parentBytes) : 0.0;
        }
        HRESULT DescribeBackingChild(IShellFolder2* backingFolder, const std::wstring& backingPath,
            PCUITEMID_CHILD original, SyntheticChild* child)
        {
            if (!original || !child || !backingFolder) return E_INVALIDARG;
            *child = {};
            child->cb = sizeof(SyntheticChild);
            child->id = 1;
            STRRET displayName{};
            auto result = backingFolder->GetDisplayNameOf(original, SHGDN_INFOLDER, &displayName);
            if (FAILED(result))
            {
                char message[128]{};
                sprintf_s(message, "synthetic-folder-tree: GetDisplayNameOf failed hr=0x%08X", static_cast<unsigned>(result));
                TraceSyntheticSortStage(message);
                return result;
            }
            if (displayName.uType == STRRET_WSTR && displayName.pOleStr)
            {
                if (wcslen(displayName.pOleStr) >= ARRAYSIZE(child->name)) result = HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
                else wcscpy_s(child->name, displayName.pOleStr);
                CoTaskMemFree(displayName.pOleStr);
            }
            else if (displayName.uType == STRRET_CSTR)
            {
                if (MultiByteToWideChar(CP_ACP, 0, displayName.cStr, -1, child->name, ARRAYSIZE(child->name)) == 0)
                    result = HRESULT_FROM_WIN32(GetLastError());
            }
            else if (displayName.uType == STRRET_OFFSET)
            {
                if (displayName.uOffset >= original->mkid.cb)
                    result = E_INVALIDARG;
                else
                {
                    const auto* nameBytes = reinterpret_cast<const char*>(original) + displayName.uOffset;
                    const auto available = original->mkid.cb - displayName.uOffset;
                    const auto* terminator = static_cast<const char*>(memchr(nameBytes, '\0', available));
                    if (!terminator)
                        result = E_INVALIDARG;
                    else if (MultiByteToWideChar(CP_ACP, 0, nameBytes,
                                 static_cast<int>(terminator - nameBytes + 1), child->name, ARRAYSIZE(child->name)) == 0)
                        result = HRESULT_FROM_WIN32(GetLastError());
                }
            }
            else result = E_UNEXPECTED;
            if (FAILED(result))
            {
                char message[160]{};
                sprintf_s(message, "synthetic-folder-tree: display-name conversion failed hr=0x%08X type=%u cb=%u offset=%u",
                    static_cast<unsigned>(result), displayName.uType, original->mkid.cb, displayName.uOffset);
                TraceSyntheticSortStage(message);
                return result;
            }

            SFGAOF attributes = SFGAO_FOLDER | SFGAO_HIDDEN;
            result = backingFolder->GetAttributesOf(1, const_cast<PCUITEMID_CHILD_ARRAY>(&original), &attributes);
            if (FAILED(result))
            {
                char message[128]{};
                sprintf_s(message, "synthetic-folder-tree: GetAttributesOf failed hr=0x%08X", static_cast<unsigned>(result));
                TraceSyntheticSortStage(message);
                return result;
            }
            if (attributes & SFGAO_FOLDER) child->attributes |= FILE_ATTRIBUTE_DIRECTORY;
            if (attributes & SFGAO_HIDDEN) child->attributes |= FILE_ATTRIBUTE_HIDDEN;
            if (!(child->attributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                VARIANT size{};
                VariantInit(&size);
                result = backingFolder->GetDetailsEx(original, &kSizeKey, &size);
                if (SUCCEEDED(result))
                {
                    if (size.vt == VT_UI8) child->size = size.ullVal;
                    else if (size.vt == VT_I8 && size.llVal >= 0) child->size = static_cast<ULONGLONG>(size.llVal);
                }
                VariantClear(&size);
                if (FAILED(result))
                {
                    char message[128]{};
                    sprintf_s(message, "synthetic-folder-tree: GetDetailsEx(size) failed hr=0x%08X", static_cast<unsigned>(result));
                    TraceSyntheticSortStage(message);
                    return result;
                }
            }
            else
            {
                std::wstring childPath;
                try { childPath = backingPath + L"\\" + child->name; }
                catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
                TryGetSyntheticFileIdentity(childPath.c_str(), &child->identity);
                if (!(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT) &&
                    InterlockedCompareExchange(&g_syntheticRecursiveSizeScanEnabledForProbe, 0, 0) &&
                    !InterlockedCompareExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0, 0))
                {
                    SyntheticDirectoryMetrics metrics{};
                    result = GetSyntheticDirectoryMetrics(childPath, &metrics);
                    if (FAILED(result)) return result;
                    child->size = metrics.bytes;
                    child->fileCount = metrics.files;
                    child->folderCount = metrics.folders;
                }
            }
            return S_OK;
        }
        HRESULT CaptureSelection()
        {
            IFolderView2* folderView = nullptr;
            auto result = _shellView ? _shellView->QueryInterface(IID_PPV_ARGS(&folderView)) : E_UNEXPECTED;
            if (FAILED(result)) return result;
            _pendingSelected.clear();
            _pendingHasFocused = false;
            int focusedIndex = -1;
            if (SUCCEEDED(folderView->GetFocusedItem(&focusedIndex)) && focusedIndex >= 0)
            {
                PITEMID_CHILD item = nullptr;
                if (SUCCEEDED(folderView->Item(focusedIndex, &item)))
                {
                    if (const auto* data = SyntheticData(item)) { _pendingFocused = *data; _pendingHasFocused = true; }
                    CoTaskMemFree(item);
                }
            }
            IEnumIDList* selection = nullptr;
            result = folderView->Items(SVGIO_SELECTION, IID_PPV_ARGS(&selection));
            if (SUCCEEDED(result) && selection)
            {
                try
                {
                    while (true)
                    {
                        PITEMID_CHILD item = nullptr;
                        ULONG fetched = 0;
                        result = selection->Next(1, &item, &fetched);
                        if (result != S_OK || fetched != 1) { if (item) CoTaskMemFree(item); break; }
                        if (const auto* data = SyntheticData(item)) _pendingSelected.push_back(*data);
                        CoTaskMemFree(item);
                    }
                }
                catch (const std::bad_alloc&) { _pendingSelected.clear(); _pendingHasFocused = false; }
                selection->Release();
            }
            HWND viewWindow = nullptr;
            if (_shellView) _shellView->GetWindow(&viewWindow);
            const auto listView = GetSyntheticListView(viewWindow);
            const auto listSelected = listView ? static_cast<int>(SendMessageW(listView, LVM_GETNEXTITEM, static_cast<WPARAM>(-1), LVNI_SELECTED)) : -1;
            const auto listFocused = listView ? static_cast<int>(SendMessageW(listView, LVM_GETNEXTITEM, static_cast<WPARAM>(-1), LVNI_FOCUSED)) : -1;
            char stateTrace[192]{};
            StringCchPrintfA(stateTrace, ARRAYSIZE(stateTrace), "synthetic-sort: refresh-state-captured view=0x%p list=0x%p selected=%zu focused=%d list-selected=%d list-focused=%d",
                viewWindow, listView, _pendingSelected.size(), _pendingHasFocused ? 1 : 0, listSelected, listFocused);
            TraceSyntheticSortStage(stateTrace);
            folderView->Release();
            return S_OK;
        }
        HRESULT RefreshView()
        {
            return _shellView ? _shellView->Refresh() : E_UNEXPECTED;
        }
        HRESULT RestoreSelection()
        {
            IFolderView2* folderView = nullptr;
            auto result = _shellView ? _shellView->QueryInterface(IID_PPV_ARGS(&folderView)) : E_UNEXPECTED;
            if (FAILED(result)) return result;
            int itemCount = 0;
            int restoredSelectionCount = 0;
            bool focusedItemRestored = !_pendingHasFocused;
            char stateTrace[128]{};
            if (SUCCEEDED(folderView->ItemCount(SVGIO_ALLVIEW, &itemCount)))
            {
                bool firstSelection = true;
                for (const auto& selectedItem : _pendingSelected)
                {
                    for (int index = 0; index < itemCount; ++index)
                    {
                        PITEMID_CHILD item = nullptr;
                        if (FAILED(folderView->Item(index, &item))) continue;
                        const auto* data = SyntheticData(item);
                        const bool match = data && _wcsicmp(data->name, selectedItem.name) == 0;
                        CoTaskMemFree(item);
                        if (match)
                        {
                            const DWORD flags = SVSI_SELECT | SVSI_NOTAKEFOCUS | (firstSelection ? SVSI_DESELECTOTHERS : 0);
                            result = folderView->SelectItem(index, flags);
                            if (FAILED(result)) break;
                            ++restoredSelectionCount;
                            firstSelection = false;
                            break;
                        }
                    }
                    if (FAILED(result)) break;
                }
                if (SUCCEEDED(result) && _pendingHasFocused)
                {
                    for (int index = 0; index < itemCount; ++index)
                    {
                        PITEMID_CHILD item = nullptr;
                        if (FAILED(folderView->Item(index, &item))) continue;
                        const auto* data = SyntheticData(item);
                        const bool match = data && _wcsicmp(data->name, _pendingFocused.name) == 0;
                        CoTaskMemFree(item);
                        if (match)
                        {
                            result = folderView->SelectItem(index, SVSI_SELECT | SVSI_FOCUSED | SVSI_NOTAKEFOCUS);
                            focusedItemRestored = SUCCEEDED(result);
                            break;
                        }
                    }
                }
                HWND viewWindow = nullptr;
                if (_shellView) _shellView->GetWindow(&viewWindow);
                StringCchPrintfA(stateTrace, ARRAYSIZE(stateTrace), "synthetic-sort: refresh-state-restored view=0x%p rows=%d selected=%d focus-identity=%d",
                    viewWindow, itemCount, restoredSelectionCount, _pendingHasFocused ? 1 : 0);
                TraceSyntheticSortStage(stateTrace);
            }
            folderView->Release();
            if (SUCCEEDED(result) && (restoredSelectionCount < static_cast<int>(_pendingSelected.size()) || !focusedItemRestored)) return S_FALSE;
            return result;
        }
        HRESULT ScheduleRefresh()
        {
            if (!_shellView) return E_UNEXPECTED;
            if (InterlockedCompareExchange(&_refreshPending, 1, 0) != 0)
            {
                InterlockedExchange(&_refreshAgain, 1);
                return S_FALSE;
            }
            AddRef();
            const auto timerId = SetTimer(nullptr, 0, 1, RefreshTimerProc);
            if (!timerId)
            {
                InterlockedExchange(&_refreshPending, 0);
                Release();
                return HRESULT_FROM_WIN32(GetLastError());
            }
            _refreshTimerId = timerId;
            return S_OK;
        }
        static bool FinishRefreshCycle(SyntheticSortFolder* folder)
        {
            InterlockedExchange(&folder->_refreshPending, 0);
            if (InterlockedExchange(&folder->_refreshAgain, 0) != 0 &&
                InterlockedCompareExchange(&folder->_refreshPending, 1, 0) == 0)
            {
                const auto timerId = SetTimer(nullptr, 0, 1, RefreshTimerProc);
                if (timerId)
                {
                    folder->_refreshTimerId = timerId;
                    return true;
                }
                InterlockedExchange(&folder->_refreshPending, 0);
            }
            folder->Release();
            return false;
        }
        static SyntheticSortFolder* FindPendingRefresh(UINT_PTR timerId)
        {
            for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
            {
                auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
                if (folder && folder->_refreshTimerId == timerId) return folder;
            }
            return nullptr;
        }
        static SyntheticSortFolder* FindPendingBackingDirectoryUpdate(UINT_PTR timerId)
        {
            for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
            {
                auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
                if (folder && folder->_directoryUpdateTimerId == timerId) return folder;
            }
            return nullptr;
        }
        static VOID CALLBACK BackingDirectoryUpdateTimerProc(HWND, UINT, UINT_PTR timerId, DWORD)
        {
            KillTimer(nullptr, timerId);
            auto* folder = FindPendingBackingDirectoryUpdate(timerId);
            if (!folder) return;
            folder->_directoryUpdateTimerId = 0;
            InterlockedExchange(&folder->_directoryUpdatePending, 0);
            // Defer until SFVM_FSNOTIFY returns; UPDATEDIR itself is excluded from translation to prevent a loop.
            SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_IDLIST, folder->_backingRoot, nullptr);
            ED_PROBE_METRIC_INCREMENT(g_syntheticChildNotificationTranslationSent);
            TraceSyntheticSortStage("synthetic-sort: child-notification-parent-update-sent");
            folder->Release();
        }
        static VOID CALLBACK RefreshTimerProc(HWND, UINT, UINT_PTR timerId, DWORD)
        {
            KillTimer(nullptr, timerId);
            auto* folder = FindPendingRefresh(timerId);
            if (!folder) return;
            ED_PROBE_METRIC_INCREMENT(g_syntheticDeferredRefreshCount);
            folder->_refreshTimerId = 0;
            auto result = folder->CaptureSelection();
            if (SUCCEEDED(result)) result = folder->RefreshView();
            TraceSyntheticSortStage(SUCCEEDED(result) ? "synthetic-sort: deferred-refresh-returned" : "synthetic-sort: deferred-refresh-failed");
            const auto restoreTimer = SUCCEEDED(result) ? SetTimer(nullptr, 0, 250, RestoreTimerProc) : 0;
            if (restoreTimer)
            {
                folder->_restoreDeadline = GetTickCount64() + 2500;
                folder->_refreshTimerId = restoreTimer;
                return;
            }
            FinishRefreshCycle(folder);
        }
        static VOID CALLBACK RestoreTimerProc(HWND, UINT, UINT_PTR timerId, DWORD)
        {
            KillTimer(nullptr, timerId);
            auto* folder = FindPendingRefresh(timerId);
            if (!folder) return;
            folder->_refreshTimerId = 0;
            const auto result = folder->RestoreSelection();
            if (result == S_FALSE && GetTickCount64() < folder->_restoreDeadline)
            {
                const auto retryTimer = SetTimer(nullptr, 0, 100, RestoreTimerProc);
                if (retryTimer)
                {
                    folder->_refreshTimerId = retryTimer;
                    TraceSyntheticSortStage("synthetic-sort: deferred-selection-retry");
                    return;
                }
            }
            TraceSyntheticSortStage(SUCCEEDED(result) ? "synthetic-sort: deferred-selection-restored" : "synthetic-sort: deferred-selection-restore-failed");
            FinishRefreshCycle(folder);
        }
        ~SyntheticSortFolder()
        {
            ReleaseViewHold();
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
            if (_viewNotificationRoot) CoTaskMemFree(_viewNotificationRoot);
#endif
            if (_root) CoTaskMemFree(_root);
            if (_backingRoot) CoTaskMemFree(_backingRoot);
            if (_inner) _inner->Release();
            if (_recursiveSizeStore) _recursiveSizeStore->Release();
            InterlockedDecrement(&g_syntheticLiveObjects);
        }
        volatile LONG _references = 1;
        CLSID _classId = g_syntheticFolderClassId;
        std::wstring _configuredBackingPath;
        volatile LONG _refreshAgain = 0;
        PIDLIST_ABSOLUTE _root = nullptr;
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
        PIDLIST_ABSOLUTE _viewNotificationRoot = nullptr;
#endif
        PIDLIST_ABSOLUTE _backingRoot = nullptr;
        IShellView* _shellView = nullptr;
#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
        volatile LONG _mixedSizeSort = 1; // Legacy until the host applies its per-tab default-off setting.
        bool _hasEncodedSizeSortPolicy = false;
        int _encodedSizeSortDirection = 1;
#endif
        std::wstring _backingPath;
        IShellFolder2* _inner = nullptr;
        SyntheticRecursiveSizeSnapshotStore* _recursiveSizeStore = nullptr;
        std::vector<SyntheticChild> _pendingSelected;
        mutable std::mutex _asyncSizeScanMutex;
#if defined(EXPLORERDIRS_PROVIDER_VIEW_NOTIFICATIONS)
        HWND _notificationWindow = nullptr;
        ULONG _backingNotificationRegistration = 0;
#if defined(EXPLORERDIRS_FOLDER_SIZE_DISPLAY)
        std::atomic<HWND> _sizeDisplayWindow{nullptr};
        const ULONG_PTR _sizeDisplayCookie = ++g_sizeDisplayCookie;
        HWND _sizeDisplayList = nullptr;
        bool _refreshingSizeDisplay = false;
        std::map<int, std::pair<std::wstring, std::wstring>> _sizeDisplayRows;
#endif
#endif
        mutable std::mutex _backingColumnMutex;
        mutable std::vector<UINT> _backingColumnOrdinals;
        mutable bool _backingColumnMapReady = false;
        mutable std::set<std::wstring, ShellParsingNameLess> _asyncSizeScansPending;
        mutable std::vector<AsyncRecursiveSizeBatchEntry> _asyncSizeScanBatch;
        mutable std::mutex _asyncViewUpdateMutex;
        mutable std::map<std::wstring, PIDLIST_ABSOLUTE, ShellParsingNameLess> _asyncPendingItemNotifications;
        mutable bool _asyncPendingDirectoryNotification = false;
        mutable bool _asyncViewUpdatePending = false;
        volatile LONG _asyncParentBatchPending = 0;
        volatile LONG _asyncChildBatchPending = 0;
        volatile LONG _asyncScansCancelled = 0;
        volatile LONG _asyncNotificationsEnabled = 0;
        SyntheticChild _pendingFocused{};
        bool _pendingHasFocused = false;
        volatile LONG _refreshPending = 0;
        UINT_PTR _refreshTimerId = 0;
        volatile LONG _directoryUpdatePending = 0;
        UINT_PTR _directoryUpdateTimerId = 0;
        ULONGLONG _restoreDeadline = 0;
    };

    class SyntheticFolderClassFactory final : public IClassFactory
    {
    public:
        explicit SyntheticFolderClassFactory(SyntheticRecursiveSizeSnapshotStore* recursiveSizeStore = nullptr,
            REFCLSID classId = g_syntheticFolderClassId, std::wstring configuredBackingPath = {})
            : _classId(classId), _configuredBackingPath(std::move(configuredBackingPath)),
              _recursiveSizeStore(recursiveSizeStore)
        {
            if (_recursiveSizeStore) _recursiveSizeStore->AddRef();
            InterlockedIncrement(&g_syntheticLiveObjects);
        }
        ~SyntheticFolderClassFactory()
        {
            if (_recursiveSizeStore) _recursiveSizeStore->Release();
            InterlockedDecrement(&g_syntheticLiveObjects);
        }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *value = static_cast<IClassFactory*>(this);
            AddRef();
            return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_references); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto references = InterlockedDecrement(&_references);
            if (!references) delete this;
            return references;
        }
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override
        {
            if (outer) return CLASS_E_NOAGGREGATION;
            ED_PROBE_METRIC_INCREMENT(g_syntheticClassFactoryCreateCount);
            SyntheticSortFolder* folder = nullptr;
            try { folder = new (std::nothrow) SyntheticSortFolder(_recursiveSizeStore, _classId, _configuredBackingPath); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            if (!folder) return E_OUTOFMEMORY;
            const auto result = folder->QueryInterface(iid, value);
            folder->Release();
            return result;
        }
        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
        {
            if (lock) InterlockedIncrement(&g_syntheticServerLocks);
            else if (InterlockedDecrement(&g_syntheticServerLocks) < 0)
            {
                InterlockedIncrement(&g_syntheticServerLocks);
                return E_UNEXPECTED;
            }
            return S_OK;
        }
    private:
        volatile LONG _references = 1;
        CLSID _classId = g_syntheticFolderClassId;
        std::wstring _configuredBackingPath;
        SyntheticRecursiveSizeSnapshotStore* _recursiveSizeStore = nullptr;
    };

    // Keep scan snapshots across transient Shell folder/factory lifetimes, then release
    // the DLL-owned reference only after COM reports that the adapter is idle.
    SRWLOCK g_sharedRecursiveSizeStoreLock = SRWLOCK_INIT;
    SyntheticRecursiveSizeSnapshotStore* g_sharedRecursiveSizeStore = nullptr;
    volatile LONG g_syntheticClassObjectCalls = 0;

    SyntheticRecursiveSizeSnapshotStore* AcquireSharedRecursiveSizeStore()
    {
        AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
        if (!g_sharedRecursiveSizeStore)
            g_sharedRecursiveSizeStore = new (std::nothrow) SyntheticRecursiveSizeSnapshotStore(
                static_cast<size_t>(InterlockedCompareExchange(&g_syntheticRecursiveSizeCacheCapacityForProbe, 0, 0)));
        auto* store = g_sharedRecursiveSizeStore;
        if (store) store->AddRef();
        ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
        return store;
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID classId, REFIID iid, void** value)
{
    // Serialize the in-flight marker with DllCanUnloadNow's final locked check so
    // unload cannot race a class-object request that is about to enter this DLL.
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    InterlockedIncrement(&g_syntheticClassObjectCalls);
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    HRESULT result = CLASS_E_CLASSNOTAVAILABLE;
    if (value) *value = nullptr;
    if (!value) result = E_POINTER;
    else
    {
        std::wstring configuredBackingPath;
        const bool configuredRoot = TryGetSyntheticRootBackingPath(classId, &configuredBackingPath);
        const bool legacyRoot = classId == g_syntheticFolderClassId;
        if (!configuredRoot && !legacyRoot)
        {
            InterlockedDecrement(&g_syntheticClassObjectCalls);
            return CLASS_E_CLASSNOTAVAILABLE;
        }
        if (legacyRoot && !configuredRoot && !g_syntheticSortBackingPath.empty())
        {
            try { configuredBackingPath = g_syntheticSortBackingPath; }
            catch (const std::bad_alloc&) { InterlockedDecrement(&g_syntheticClassObjectCalls); return E_OUTOFMEMORY; }
        }
        auto* store = AcquireSharedRecursiveSizeStore();
        if (!store) result = E_OUTOFMEMORY;
        else
        {
            SyntheticFolderClassFactory* factory = nullptr;
            try { factory = new (std::nothrow) SyntheticFolderClassFactory(store, classId, std::move(configuredBackingPath)); }
            catch (const std::bad_alloc&) { factory = nullptr; }
            store->Release();
            if (!factory) result = E_OUTOFMEMORY;
            else
            {
                result = factory->QueryInterface(iid, value);
                factory->Release();
            }
        }
    }
    InterlockedDecrement(&g_syntheticClassObjectCalls);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI ConfigureFolderSizeProvider(FolderSizeProviderCallback callback,
    FolderSizeInvalidationCallback invalidationCallback, BOOL enabled)
{
    AcquireSRWLockExclusive(&g_folderSizeProviderLock);
    InterlockedIncrement(&g_folderSizeGeneration);
    InterlockedExchange(&g_folderSizeEnabled, enabled ? 1 : 0);
    InterlockedExchangePointer(&g_folderSizeProviderCallback, reinterpret_cast<PVOID>(callback));
    InterlockedExchangePointer(&g_folderSizeInvalidationCallback, reinterpret_cast<PVOID>(invalidationCallback));
    AcquireSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    if (store) { store->ClearForProviderChange(); store->Release(); }
    ReleaseSRWLockExclusive(&g_folderSizeProviderLock);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI InvalidateFolderSizePath(PCWSTR path)
{
    if (!path || !*path) return E_INVALIDARG;
    AcquireSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    if (store) { store->InvalidatePath(path, true); store->Release(); }
    else InvalidateManagedFolderSize(path);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI QueryConfiguredFolderSizeForProbe(PCWSTR path, volatile LONG* cancelled,
    ULONGLONG* bytes, ULONGLONG* files, ULONGLONG* folders, ULONGLONG* unreadable, DWORD* flags)
{
    if (!path || !bytes || !files || !folders || !unreadable || !flags) return E_POINTER;
    SyntheticDirectoryMetrics metrics{};
    const auto result = GetConfiguredDirectoryMetrics(path, &metrics, cancelled);
    *bytes = metrics.bytes; *files = metrics.files; *folders = metrics.folders; *unreadable = metrics.unreadable;
    *flags = (metrics.sizeAvailable ? 1U : 0U) | (metrics.countsAvailable ? 2U : 0U);
    return result;
}

extern "C" ED_PROBE_EXPORT void WINAPI InvalidateConfiguredFolderSizeForProbe(PCWSTR path)
{
    InvalidateManagedFolderSize(path);
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI VerifyFolderSizeAvailabilityStoreForProbe()
{
    SyntheticRecursiveSizeSnapshotStore store;
    SyntheticFileIdentity identity{};
    identity.available = true;
    identity.volumeSerial = 123;
    ULONGLONG bytes = 42;
    bool available = true, counts = true;
    const auto generation = InterlockedCompareExchange(&g_folderSizeGeneration, 0, 0);
    if (store.SetForProviderGeneration(generation, L"C:\\generation-fixture", identity, 17) != S_OK ||
        store.SetForProviderGeneration(generation + 1, L"C:\\generation-fixture", identity, 99) != HRESULT_FROM_WIN32(ERROR_CANCELLED) ||
        !store.TryGet(L"C:\\generation-fixture", identity, &bytes) || bytes != 17) return E_FAIL;
    if (FAILED(store.Set(L"C:\\availability-fixture", identity, 0, 0, 0, 0, false, false)) ||
        !store.TryGet(L"C:\\availability-fixture", identity, &bytes, nullptr, nullptr, nullptr, &available, &counts) ||
        available || counts) return E_FAIL;
    if (FAILED(store.Set(L"C:\\availability-fixture", identity, 0)) ||
        !store.TryGet(L"C:\\availability-fixture", identity, &bytes, nullptr, nullptr, nullptr, &available, &counts) ||
        bytes != 0 || !available || !counts) return E_FAIL;
    if (store.TryReserveScan(L"C:\\pending-fixture", identity, &bytes) !=
        SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired) return E_FAIL;
    store.ClearForProviderChange();
    if (store.Count() != 0 || store.Set(L"C:\\pending-fixture", identity, 99) != S_FALSE) return E_FAIL;
    store.ReleaseScan(L"C:\\pending-fixture");
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI VerifyBackingColumnMetadataForProbe(PCWSTR path)
{
    if (!path) return E_POINTER;
    const auto initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized) && initialized != RPC_E_CHANGED_MODE) return initialized;
    PIDLIST_ABSOLUTE rootPidl = nullptr;
    PITEMID_CHILD earlier = nullptr, later = nullptr;
    auto* folder = new (std::nothrow) SyntheticSortFolder(nullptr, g_syntheticFolderClassId, path);
    auto result = folder ? SHParseDisplayName(path, nullptr, &rootPidl, 0, nullptr) : E_OUTOFMEMORY;
    if (SUCCEEDED(result)) result = folder->Initialize(rootPidl);
    if (SUCCEEDED(result)) result = folder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"earlier.bin"), nullptr, &earlier, nullptr);
    if (SUCCEEDED(result)) result = folder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"later.bin"), nullptr, &later, nullptr);
    UINT modifiedColumn = 0;
    bool foundModified = false, foundCreated = false, foundAccessed = false;
    if (SUCCEEDED(result))
    {
        for (UINT column = 3; column < 1024; ++column)
        {
            SHCOLUMNID key{};
            if (FAILED(folder->MapColumnToSCID(column, &key))) break;
            if (key == PKEY_DateModified) { modifiedColumn = column; foundModified = true; }
            if (key == PKEY_DateCreated) foundCreated = true;
            if (key == PKEY_DateAccessed) foundAccessed = true;
        }
        if (!foundModified || !foundCreated || !foundAccessed) result = E_FAIL;
    }
    if (SUCCEEDED(result))
    {
        SHELLDETAILS header{}, cell{};
        result = folder->GetDetailsOf(nullptr, modifiedColumn, &header);
        if (SUCCEEDED(result))
        {
            LPWSTR text = nullptr;
            result = StrRetToStrW(&header.str, nullptr, &text);
            if (SUCCEEDED(result) && (!text || !*text)) result = E_FAIL;
            if (text) CoTaskMemFree(text);
        }
        if (SUCCEEDED(result)) result = folder->GetDetailsOf(earlier, modifiedColumn, &cell);
        if (SUCCEEDED(result))
        {
            if (cell.str.uType != STRRET_WSTR || !cell.str.pOleStr || !*cell.str.pOleStr) result = E_FAIL;
            if (cell.str.uType == STRRET_WSTR && cell.str.pOleStr) CoTaskMemFree(cell.str.pOleStr);
        }
        if (SUCCEEDED(result))
        {
            const auto comparison = folder->CompareIDs(modifiedColumn, earlier, later);
            if (FAILED(comparison) || static_cast<short>(HRESULT_CODE(comparison)) >= 0) result = E_FAIL;
        }
        if (SUCCEEDED(result))
        {
            VARIANT value{}; VariantInit(&value);
            result = folder->GetDetailsEx(earlier, &PKEY_DateModified, &value);
            if (SUCCEEDED(result) && (value.vt == VT_EMPTY || value.vt == VT_NULL)) result = E_FAIL;
            VariantClear(&value);
        }
    }
    if (SUCCEEDED(result))
    {
        PITEMID_CHILD directory = nullptr;
        result = folder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"small"), nullptr, &directory, nullptr);
        const auto previousEnabled = InterlockedExchange(&g_folderSizeEnabled, 0);
        if (SUCCEEDED(result))
        {
            // Disabled totals are absent, while ordinary file sizes remain readable.
            VARIANT value{}; VariantInit(&value);
            result = folder->GetDetailsEx(directory, &kSizeKey, &value);
            if (SUCCEEDED(result) && value.vt != VT_EMPTY) result = E_FAIL;
            VariantClear(&value);
            if (SUCCEEDED(result)) result = folder->GetDetailsEx(earlier, &kSizeKey, &value);
            if (SUCCEEDED(result) && (value.vt != VT_UI8 || value.ullVal != 1)) result = E_FAIL;
            VariantClear(&value);
            SHELLDETAILS cell{};
            if (SUCCEEDED(result)) result = folder->GetDetailsOf(directory, 1, &cell);
            if (SUCCEEDED(result))
            {
                LPWSTR text = nullptr;
                result = StrRetToStrW(&cell.str, directory, &text);
                if (SUCCEEDED(result) && (!text || *text)) result = E_FAIL;
                if (text) CoTaskMemFree(text);
            }
        }
        InterlockedExchange(&g_folderSizeEnabled, previousEnabled);
        if (SUCCEEDED(result))
        {
            const auto previousAsync = InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 1);
            const auto previousDemand = InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0);
            VARIANT pending{}; VariantInit(&pending);
            result = folder->GetDetailsEx(directory, &kSizeKey, &pending);
            if (SUCCEEDED(result) && pending.vt != VT_EMPTY) result = E_FAIL;
            VariantClear(&pending);
            if (SUCCEEDED(result))
            {
                InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 0);
                InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 1);
                result = folder->GetDetailsEx(directory, &kSizeKey, &pending);
                if (SUCCEEDED(result) && (pending.vt != VT_UI8 || pending.ullVal != 5 ||
                    folder->RecursiveSizeSnapshotCountForProbe() == 0)) result = E_FAIL;
                VariantClear(&pending);
                InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 1);
                InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 0);
            }
            if (SUCCEEDED(result))
            {
                const auto* child = SyntheticData(directory);
                const auto childPath = std::wstring(path) + L"\\small";
                result = folder->SetRecursiveSizeSnapshotForProbe(directory, childPath.c_str(), child->identity, 0);
                if (SUCCEEDED(result)) result = folder->GetDetailsEx(directory, &kSizeKey, &pending);
                if (SUCCEEDED(result) && (pending.vt != VT_UI8 || pending.ullVal != 0)) result = E_FAIL;
                VariantClear(&pending);
            }
            InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, previousDemand);
            InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, previousAsync);
        }
        if (directory) CoTaskMemFree(directory);
    }
    if (earlier) CoTaskMemFree(earlier);
    if (later) CoTaskMemFree(later);
    if (rootPidl) CoTaskMemFree(rootPidl);
    if (folder) folder->Release();
    if (SUCCEEDED(initialized)) CoUninitialize();
    return result;
}

#if defined(EXPLORERDIRS_SIZE_SORT_POLICY_EXPERIMENT)
// Read-only policy in an opaque experimental PIDL survives Shell re-binding;
// canonical parsing names and the physical path are deliberately unchanged.
static HRESULT CreateSizeSortPolicyPidl(
    PCIDLIST_ABSOLUTE source, DWORD policy, PIDLIST_ABSOLUTE* output)
{
    if (!source || !output || policy > 3) return E_INVALIDARG;
    *output = nullptr;
    std::wstring path;
    const auto parsed = ReadSyntheticRootPathFromPidl(source, &path, true);
    if (parsed != S_OK) return FAILED(parsed) ? parsed : E_INVALIDARG;
    size_t bytes = ILGetSize(source);
    for (auto* part = reinterpret_cast<PCUIDLIST_RELATIVE>(source); !ILIsEmpty(part); part = ILNext(part))
        if (part->mkid.cb >= kSyntheticRootPathPidlHeaderSize &&
            reinterpret_cast<const SyntheticRootPathPidlItem*>(part)->magic == kSyntheticRootPathPidlMagic)
        {
            if (part->mkid.cb > USHRT_MAX - sizeof(DWORD) || bytes > SIZE_MAX - sizeof(DWORD)) return E_INVALIDARG;
            bytes += sizeof(DWORD);
        }
    auto* result = static_cast<BYTE*>(CoTaskMemAlloc(bytes));
    if (!result) return E_OUTOFMEMORY;
    auto* cursor = result;
    for (auto* part = reinterpret_cast<PCUIDLIST_RELATIVE>(source); !ILIsEmpty(part); part = ILNext(part))
    {
        const auto* header = reinterpret_cast<const SyntheticRootPathPidlItem*>(part);
        if (part->mkid.cb >= kSyntheticRootPathPidlHeaderSize && IsSyntheticRootPathMagic(header->magic))
        {
            const auto oldOffset = SyntheticRootPathTextOffset(header);
            const auto newOffset = kSyntheticRootPathPidlHeaderSize + sizeof(DWORD);
            const auto newBytes = static_cast<USHORT>(part->mkid.cb - oldOffset + newOffset);
            CopyMemory(cursor, part, kSyntheticRootPathPidlHeaderSize);
            auto* updated = reinterpret_cast<SyntheticRootPathPidlItem*>(cursor);
            updated->cb = newBytes; updated->magic = 0x32525044;
            CopyMemory(cursor + kSyntheticRootPathPidlHeaderSize, &policy, sizeof(policy));
            CopyMemory(cursor + newOffset, reinterpret_cast<const BYTE*>(part) + oldOffset, part->mkid.cb - oldOffset);
            cursor += newBytes;
        }
        else { CopyMemory(cursor, part, part->mkid.cb); cursor += part->mkid.cb; }
    }
    ZeroMemory(cursor, sizeof(USHORT));
    *output = reinterpret_cast<PIDLIST_ABSOLUTE>(result);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI CreateSizeSortPolicyPidlForProbe(
    PCIDLIST_ABSOLUTE source, DWORD policy, PIDLIST_ABSOLUTE* output)
{ return CreateSizeSortPolicyPidl(source, policy, output); }

extern "C" __declspec(dllexport) HRESULT WINAPI VerifySizeSortPolicyPidlForProbe()
{
    for (const size_t length : { 3u, 1024u, 1025u, 4096u })
    for (const DWORD policy : { 0u, 1u, 2u, 3u })
    {
        const auto path = std::wstring(L"C:\\") + std::wstring(length, L'x');
        PIDLIST_ABSOLUTE original = nullptr, prepared = nullptr;
        auto result = CreateSyntheticRootPathPidl(path.c_str(), &original);
        if (SUCCEEDED(result)) result = CreateSizeSortPolicyPidl(original, policy, &prepared);
        if (original) CoTaskMemFree(original);
        if (FAILED(result)) return result;
        std::wstring decoded;
        result = ReadSyntheticRootPathFromPidl(prepared, &decoded, true);
        bool passed = result == S_OK && decoded == path;
        auto* header = reinterpret_cast<SyntheticRootPathPidlItem*>(prepared);
        const DWORD invalid = 4;
        CopyMemory(reinterpret_cast<BYTE*>(header) + kSyntheticRootPathPidlHeaderSize, &invalid, sizeof(invalid));
        passed = passed && FAILED(ReadSyntheticRootPathFromPidl(prepared, &decoded, true));
        if (header->segmentCount > 1)
        {
            const DWORD restored = policy, mismatched = policy ^ 1;
            CopyMemory(reinterpret_cast<BYTE*>(header) + kSyntheticRootPathPidlHeaderSize, &restored, sizeof(restored));
            auto* next = ILNext(prepared);
            CopyMemory(reinterpret_cast<BYTE*>(const_cast<PUIDLIST_RELATIVE>(next)) + kSyntheticRootPathPidlHeaderSize, &mismatched, sizeof(mismatched));
            passed = passed && FAILED(ReadSyntheticRootPathFromPidl(prepared, &decoded, true));
        }
        CoTaskMemFree(prepared);
        if (!passed) return E_FAIL;
    }
    return S_OK;
}

#endif

extern "C" HRESULT WINAPI ConfigureShellNamespaceRoot(
    const CLSID* classId, PCWSTR backingPath)
{
    if (!classId || !backingPath || !*backingPath) return E_INVALIDARG;
    try
    {
        std::lock_guard<std::mutex> lock(g_syntheticRootConfigurationMutex);
        g_syntheticRootBackingPaths.insert_or_assign(*classId, backingPath);
    }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    return S_OK;
}

#if defined(EXPLORERDIRS_PRODUCT_DIAGNOSTICS)
// No view/window: exercise capture ownership, deferred names, bounds and overflow independently.
extern "C" HRESULT WINAPI RunProductNotificationIdentityCaptureSmoke()
{
    ProductNotificationIdentityCapture capture;
    PIDLIST_ABSOLUTE desktop = nullptr;
    auto result = SHGetKnownFolderIDList(FOLDERID_Desktop, 0, nullptr, &desktop);
    if (FAILED(result)) return result;
    LPWSTR expected = nullptr;
    result = SHGetNameFromIDList(desktop, SIGDN_DESKTOPABSOLUTEPARSING, &expected);
    if (FAILED(result)) { CoTaskMemFree(desktop); return result; }
    for (int index = 0; index < 32769; ++index) capture.Record(desktop);
    CoTaskMemFree(desktop);
    WCHAR first[32768] = {}, last[32768] = {}, tiny[1] = {};
    result = capture.Read(0, first, ARRAYSIZE(first));
    if (SUCCEEDED(result)) result = capture.Read(32767, last, ARRAYSIZE(last));
    const bool passed = SUCCEEDED(result) && expected &&
        wcscmp(first, expected) == 0 && wcscmp(last, expected) == 0 &&
        capture.Count() == 32768 && capture.Dropped() == 1 &&
        capture.Read(-1, first, ARRAYSIZE(first)) == E_BOUNDS &&
        capture.Read(32768, first, ARRAYSIZE(first)) == E_BOUNDS &&
        capture.Read(0, nullptr, 1) == E_INVALIDARG &&
        FAILED(capture.Read(0, tiny, ARRAYSIZE(tiny)));
    CoTaskMemFree(expected);
    return passed ? S_OK : E_FAIL;
}

extern "C" LONG WINAPI GetProductAsyncItemNotificationAttempts()
{ return InterlockedCompareExchange(&g_productAsyncItemNotificationAttempts, 0, 0); }

extern "C" LONG WINAPI GetProductAsyncItemNotificationUnderSyntheticRootCount()
{ return InterlockedCompareExchange(&g_productAsyncItemNotificationUnderSyntheticRoot, 0, 0); }

extern "C" void WINAPI ConfigureProductNotificationDeliveryForProbe(BOOL flush, DWORD delayMilliseconds)
{
    InterlockedExchange(&g_productNotificationFlushForProbe, flush ? 1 : 0);
    InterlockedExchange(&g_productNotificationDelayMsForProbe, static_cast<LONG>(delayMilliseconds));
}

extern "C" void WINAPI SetProductAsyncItemNotificationEnabledForProbe(BOOL enabled)
{ InterlockedExchange(&g_syntheticAsyncRecursiveSizeItemNotificationForProbe, enabled ? 1 : 0); }

extern "C" LONG WINAPI GetProductAsyncDirectoryNotificationAttempts()
{ return InterlockedCompareExchange(&g_productAsyncDirectoryNotificationAttempts, 0, 0); }

extern "C" LONG WINAPI GetProductAsyncItemNotificationIdentityCount()
{
    return g_productAsyncItemNotificationIdentities.Count();
}

extern "C" LONG WINAPI GetProductAsyncItemNotificationIdentityDroppedCount()
{ return g_productAsyncItemNotificationIdentities.Dropped(); }

extern "C" HRESULT WINAPI GetProductAsyncItemNotificationIdentity(LONG index, LPWSTR buffer, UINT capacity)
{
    return g_productAsyncItemNotificationIdentities.Read(index, buffer, capacity);
}

extern "C" LONG WINAPI GetProductViewItemNotificationCallbackCount()
{ return InterlockedCompareExchange(&g_productViewItemNotificationCallbacks, 0, 0); }

extern "C" void WINAPI ResetProductCompareDiagnostics()
{
    std::lock_guard<std::mutex> lock(g_productCompareDiagnosticMutex);
    g_productCompareDiagnostics.clear();
    InterlockedExchange(&g_productSyntheticEnumStarted, 0);
    InterlockedExchange(&g_productSyntheticEnumCompleted, 0);
    InterlockedExchange64(&g_productSyntheticEnumItemCount, 0);
    InterlockedExchange64(&g_productSyntheticEnumTotalMilliseconds, 0);
    InterlockedExchange64(&g_productSyntheticEnumMaximumMilliseconds, 0);
    InterlockedExchange64(&g_productRecursiveCompareCount, 0);
    InterlockedExchange64(&g_productRecursiveCompareTimingSamples, 0);
    InterlockedExchange64(&g_productRecursiveCompareSampledTicks, 0);
    InterlockedExchange64(&g_productRecursiveCompareMaximumSampleTicks, 0);
    InterlockedExchange64(&g_productRecursiveCompareFastPathHits, 0);
    InterlockedExchange64(&g_productRecursiveCompareFastPathMisses, 0);
}

extern "C" void WINAPI SetProductCompareTraceEnabled(BOOL enabled)
{ InterlockedExchange(&g_productCompareTraceEnabled, enabled ? 1 : 0); }

extern "C" void WINAPI SetProductNotificationIdentityCaptureEnabledForProbe(BOOL enabled)
{ InterlockedExchange(&g_productNotificationIdentityCaptureEnabled, enabled ? 1 : 0); }

extern "C" LONG WINAPI GetProductSyntheticEnumStarted()
{ return InterlockedCompareExchange(&g_productSyntheticEnumStarted, 0, 0); }

extern "C" LONG WINAPI GetProductSyntheticEnumCompleted()
{ return InterlockedCompareExchange(&g_productSyntheticEnumCompleted, 0, 0); }

extern "C" LONGLONG WINAPI GetProductSyntheticEnumItemCount()
{ return InterlockedCompareExchange64(&g_productSyntheticEnumItemCount, 0, 0); }

extern "C" LONGLONG WINAPI GetProductSyntheticEnumTotalMilliseconds()
{ return InterlockedCompareExchange64(&g_productSyntheticEnumTotalMilliseconds, 0, 0); }

extern "C" LONGLONG WINAPI GetProductSyntheticEnumMaximumMilliseconds()
{ return InterlockedCompareExchange64(&g_productSyntheticEnumMaximumMilliseconds, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareCount()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareCount, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareTimingSamples()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareTimingSamples, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareSampledTicks()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareSampledTicks, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareMaximumSampleTicks()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareMaximumSampleTicks, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareFastPathHits()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareFastPathHits, 0, 0); }

extern "C" LONGLONG WINAPI GetProductRecursiveCompareFastPathMisses()
{ return InterlockedCompareExchange64(&g_productRecursiveCompareFastPathMisses, 0, 0); }

extern "C" LONG WINAPI GetProductCompareDiagnosticCount()
{
    std::lock_guard<std::mutex> lock(g_productCompareDiagnosticMutex);
    return static_cast<LONG>(g_productCompareDiagnostics.size());
}

extern "C" HRESULT WINAPI GetProductCompareDiagnostic(LONG index, LPWSTR buffer, UINT capacity)
{
    if (!buffer || capacity == 0) return E_INVALIDARG;
    std::lock_guard<std::mutex> lock(g_productCompareDiagnosticMutex);
    if (index < 0 || static_cast<size_t>(index) >= g_productCompareDiagnostics.size()) return E_BOUNDS;
    return StringCchCopyW(buffer, capacity, g_productCompareDiagnostics[static_cast<size_t>(index)].c_str());
}

extern "C" LONG WINAPI GetProductViewGetNotifyCallCount()
{ return InterlockedCompareExchange(&g_productViewGetNotifyCalls, 0, 0); }

extern "C" LONG WINAPI GetProductViewGetNotifyEventMask()
{ return InterlockedCompareExchange(&g_productViewGetNotifyEventMask, 0, 0); }

extern "C" HRESULT WINAPI GetProductViewGetNotifyRootIdentity(LPWSTR buffer, UINT capacity)
{
    if (!buffer || capacity == 0) return E_INVALIDARG;
    std::lock_guard<std::mutex> lock(g_productViewNotifyRootMutex);
    if (g_productViewNotifyRootIdentity.empty()) return S_FALSE;
    return StringCchCopyW(buffer, capacity, g_productViewNotifyRootIdentity.c_str());
}

extern "C" void WINAPI SetProductViewNotificationsUseSyntheticRoot(BOOL enabled)
{ InterlockedExchange(&g_productViewNotificationsUseSyntheticRoot, enabled ? 1 : 0); }

extern "C" LONG WINAPI GetProductViewItemNotificationIdentityCount()
{
    return g_productViewItemNotificationIdentities.Count();
}

extern "C" LONG WINAPI GetProductViewItemNotificationIdentityDroppedCount()
{ return g_productViewItemNotificationIdentities.Dropped(); }

extern "C" HRESULT WINAPI GetProductViewItemNotificationIdentity(LONG index, LPWSTR buffer, UINT capacity)
{
    return g_productViewItemNotificationIdentities.Read(index, buffer, capacity);
}
#endif

#if defined(EXPLORERDIRS_NATIVEPROBE)
extern "C" ED_PROBE_EXPORT HRESULT WINAPI ConfigureSyntheticNamespaceRootForProbe(
    const CLSID* classId, PCWSTR backingPath)
{
    return ConfigureShellNamespaceRoot(classId, backingPath);
}
#endif

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RegisterSyntheticClassFactoryInProcForProbe(
    const CLSID* classId, DWORD* cookie)
{
    if (!classId || !cookie) return E_POINTER;
    *cookie = 0;
    SyntheticFolderClassFactory* factory = nullptr;
    try { factory = new (std::nothrow) SyntheticFolderClassFactory(nullptr, *classId, g_syntheticSortBackingPath); }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    if (!factory) return E_OUTOFMEMORY;
    const auto result = CoRegisterClassObject(*classId, factory, CLSCTX_INPROC_SERVER, REGCLS_MULTIPLEUSE, cookie);
    factory->Release();
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RevokeSyntheticClassFactoryForProbe(DWORD cookie)
{
    return cookie ? CoRevokeClassObject(cookie) : E_INVALIDARG;
}

namespace
{
    HRESULT CreateConfiguredRootForProbe(REFCLSID classId, IShellFolder2** value)
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        IClassFactory* factory = nullptr;
        auto result = DllGetClassObject(classId, IID_PPV_ARGS(&factory));
        if (FAILED(result)) return result;
        IShellFolder2* folder = nullptr;
        result = factory->CreateInstance(nullptr, IID_PPV_ARGS(&folder));
        factory->Release();
        if (FAILED(result)) return result;

        IPersistFolder2* persist = nullptr;
        result = folder->QueryInterface(IID_PPV_ARGS(&persist));
        if (FAILED(result)) { folder->Release(); return result; }
        auto* root = static_cast<PIDLIST_ABSOLUTE>(CoTaskMemAlloc(sizeof(USHORT)));
        if (!root) { persist->Release(); folder->Release(); return E_OUTOFMEMORY; }
        root->mkid.cb = 0;
        result = persist->Initialize(root);
        CoTaskMemFree(root);
        if (FAILED(result)) { persist->Release(); folder->Release(); return result; }

        CLSID actualClassId{};
        result = persist->GetClassID(&actualClassId);
        persist->Release();
        if (FAILED(result) || actualClassId != classId)
        {
            folder->Release();
            return FAILED(result) ? result : E_UNEXPECTED;
        }
        *value = folder;
        return S_OK;
    }

    HRESULT VerifyConfiguredRootForProbe(IShellFolder2* folder, PCWSTR expectedName, PCWSTR forbiddenName,
        ULONGLONG expectedRecursiveBytes)
    {
        if (!folder || !expectedName || !forbiddenName) return E_INVALIDARG;
        IEnumIDList* items = nullptr;
        auto result = folder->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, &items);
        if (FAILED(result) || !items)
        {
            if (items) items->Release();
            return FAILED(result) ? result : E_UNEXPECTED;
        }
        bool expectedFound = false;
        bool forbiddenFound = false;
        bool recursiveValueMatches = false;
        while (true)
        {
            PITEMID_CHILD item = nullptr;
            ULONG fetched = 0;
            const auto next = items->Next(1, &item, &fetched);
            if (next == S_FALSE) break;
            if (FAILED(next) || fetched != 1)
            {
                if (item) CoTaskMemFree(item);
                result = FAILED(next) ? next : E_UNEXPECTED;
                break;
            }
            const auto* child = SyntheticData(item);
            if (!child) result = E_INVALIDARG;
            else
            {
                expectedFound = expectedFound || _wcsicmp(child->name, expectedName) == 0;
                forbiddenFound = forbiddenFound || _wcsicmp(child->name, forbiddenName) == 0;
                if (_wcsicmp(child->name, L"shared-folder") == 0)
                {
                    SHELLDETAILS details{};
                    result = folder->GetDetailsOf(item, 1, &details);
                    if (SUCCEEDED(result) && details.str.uType == STRRET_WSTR && details.str.pOleStr)
                        recursiveValueMatches = wcscmp(details.str.pOleStr, std::to_wstring(expectedRecursiveBytes).c_str()) == 0;
                    if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
                    if (SUCCEEDED(result) && !recursiveValueMatches) result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
                }
            }
            CoTaskMemFree(item);
            if (FAILED(result)) break;
        }
        items->Release();
        return SUCCEEDED(result) && expectedFound && !forbiddenFound && recursiveValueMatches ? S_OK :
            FAILED(result) ? result : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
    }
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticMultiRootProviderSmoke(
    const CLSID* firstClassId, PCWSTR firstExpectedName, PCWSTR firstForbiddenName, ULONGLONG firstRecursiveBytes,
    const CLSID* secondClassId, PCWSTR secondExpectedName, PCWSTR secondForbiddenName, ULONGLONG secondRecursiveBytes)
{
    if (!firstClassId || !secondClassId || !firstExpectedName || !firstForbiddenName ||
        !secondExpectedName || !secondForbiddenName || *firstClassId == *secondClassId)
        return E_INVALIDARG;
    IShellFolder2* first = nullptr;
    IShellFolder2* second = nullptr;
    auto result = CreateConfiguredRootForProbe(*firstClassId, &first);
    if (SUCCEEDED(result)) result = CreateConfiguredRootForProbe(*secondClassId, &second);
    // Keep both configured roots alive while enumerating to detect accidental shared-path state.
    if (SUCCEEDED(result))
        result = VerifyConfiguredRootForProbe(first, firstExpectedName, firstForbiddenName, firstRecursiveBytes);
    if (SUCCEEDED(result))
        result = VerifyConfiguredRootForProbe(second, secondExpectedName, secondForbiddenName, secondRecursiveBytes);
    if (second) second->Release();
    if (first) first->Release();
    if (SUCCEEDED(result))
    {
        char message[192]{};
        sprintf_s(message, "synthetic-multi-root-provider: independent-roots=True; first=%ls:%lluB; second=%ls:%lluB",
            firstExpectedName, firstRecursiveBytes, secondExpectedName, secondRecursiveBytes);
        TraceSyntheticSortStage(message);
    }
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI VerifySyntheticFolderContentsForProbe(
    IShellFolder2* folder, PCWSTR expectedName, PCWSTR forbiddenName, ULONGLONG recursiveBytes)
{
    if (!folder || !expectedName || !forbiddenName) return E_INVALIDARG;
    return VerifyConfiguredRootForProbe(folder, expectedName, forbiddenName, recursiveBytes);
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticSingleClassMultiRootPidlSmoke(
    PCWSTR firstPath, PCWSTR firstExpectedName, PCWSTR firstForbiddenName, ULONGLONG firstRecursiveBytes,
    PCWSTR secondPath, PCWSTR secondExpectedName, PCWSTR secondForbiddenName, ULONGLONG secondRecursiveBytes)
{
    if (!firstPath || !*firstPath || !secondPath || !*secondPath || !firstExpectedName || !firstForbiddenName ||
        !secondExpectedName || !secondForbiddenName) return E_INVALIDARG;

    // Keep path chunks independent of the USHORT SHITEMID size limit and verify the multi-segment encoding.
    std::wstring longPathRoundTrip(1100, L'x');
    PIDLIST_ABSOLUTE longPathPidl = nullptr;
    auto result = CreateSyntheticRootPathPidl(longPathRoundTrip.c_str(), &longPathPidl);
    if (FAILED(result)) return result;
    std::wstring decodedLongPath;
    result = ReadSyntheticRootPathFromPidl(longPathPidl, &decodedLongPath);
    CoTaskMemFree(longPathPidl);
    if (FAILED(result) || decodedLongPath != longPathRoundTrip)
        return FAILED(result) ? result : HRESULT_FROM_WIN32(ERROR_INVALID_DATA);

    PIDLIST_ABSOLUTE firstRoot = nullptr;
    PIDLIST_ABSOLUTE secondRoot = nullptr;
    result = CreateSyntheticRootPathPidl(firstPath, &firstRoot);
    if (SUCCEEDED(result)) result = CreateSyntheticRootPathPidl(secondPath, &secondRoot);
    IShellFolder2* first = nullptr;
    IShellFolder2* second = nullptr;
    const auto classId = g_syntheticFolderClassId;
    IClassFactory* factory = nullptr;
    if (SUCCEEDED(result)) result = DllGetClassObject(classId, IID_PPV_ARGS(&factory));
    if (SUCCEEDED(result)) result = factory->CreateInstance(nullptr, IID_PPV_ARGS(&first));
    if (factory) { factory->Release(); factory = nullptr; }
    if (SUCCEEDED(result)) result = DllGetClassObject(classId, IID_PPV_ARGS(&factory));
    if (SUCCEEDED(result)) result = factory->CreateInstance(nullptr, IID_PPV_ARGS(&second));
    if (factory) factory->Release();
    IPersistFolder2* firstPersist = nullptr;
    IPersistFolder2* secondPersist = nullptr;
    if (SUCCEEDED(result)) result = first->QueryInterface(IID_PPV_ARGS(&firstPersist));
    if (SUCCEEDED(result)) result = second->QueryInterface(IID_PPV_ARGS(&secondPersist));
    if (SUCCEEDED(result)) result = firstPersist->Initialize(firstRoot);
    if (SUCCEEDED(result)) result = secondPersist->Initialize(secondRoot);
    if (secondPersist) secondPersist->Release();
    if (firstPersist) firstPersist->Release();

    if (SUCCEEDED(result))
    {
        PIDLIST_ABSOLUTE firstCurrent = nullptr;
        PIDLIST_ABSOLUTE secondCurrent = nullptr;
        IPersistFolder2* firstIdentity = nullptr;
        IPersistFolder2* secondIdentity = nullptr;
        result = first->QueryInterface(IID_PPV_ARGS(&firstIdentity));
        if (SUCCEEDED(result)) result = second->QueryInterface(IID_PPV_ARGS(&secondIdentity));
        if (SUCCEEDED(result)) result = firstIdentity->GetCurFolder(&firstCurrent);
        if (SUCCEEDED(result)) result = secondIdentity->GetCurFolder(&secondCurrent);
        if (SUCCEEDED(result) &&
            (ILGetSize(firstCurrent) != ILGetSize(firstRoot) ||
             memcmp(firstCurrent, firstRoot, ILGetSize(firstRoot)) != 0 ||
             ILGetSize(secondCurrent) != ILGetSize(secondRoot) ||
             memcmp(secondCurrent, secondRoot, ILGetSize(secondRoot)) != 0))
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        if (secondIdentity) secondIdentity->Release();
        if (firstIdentity) firstIdentity->Release();
        if (firstCurrent) CoTaskMemFree(firstCurrent);
        if (secondCurrent) CoTaskMemFree(secondCurrent);
    }
    if (SUCCEEDED(result))
        result = VerifyConfiguredRootForProbe(first, firstExpectedName, firstForbiddenName, firstRecursiveBytes);
    if (SUCCEEDED(result))
        result = VerifyConfiguredRootForProbe(second, secondExpectedName, secondForbiddenName, secondRecursiveBytes);

    // Exercise the folder contract: a legacy configured root at A parses a path identity at B,
    // round-trips its parsing name, then binds B as a separate folder object.
    if (SUCCEEDED(result))
    {
        PIDLIST_ABSOLUTE ordinaryRoot = nullptr;
        result = SHParseDisplayName(firstPath, nullptr, &ordinaryRoot, 0, nullptr);
        auto* parsingRoot = SUCCEEDED(result)
            ? new (std::nothrow) SyntheticSortFolder(nullptr, g_syntheticFolderClassId, firstPath)
            : nullptr;
        if (SUCCEEDED(result) && !parsingRoot) result = E_OUTOFMEMORY;
        if (SUCCEEDED(result)) result = parsingRoot->Initialize(ordinaryRoot);
        if (ordinaryRoot) CoTaskMemFree(ordinaryRoot);
        ULONG eaten = 0;
        PIDLIST_RELATIVE pathItem = nullptr;
        if (SUCCEEDED(result)) result = parsingRoot->ParseDisplayName(nullptr, nullptr,
            const_cast<LPWSTR>(secondPath), &eaten, &pathItem, nullptr);
        if (SUCCEEDED(result) && (!pathItem || eaten != wcslen(secondPath)))
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        STRRET displayName{};
        if (SUCCEEDED(result)) result = parsingRoot->GetDisplayNameOf(
            reinterpret_cast<PCUITEMID_CHILD>(pathItem), SHGDN_INFOLDER | SHGDN_FORPARSING, &displayName);
        if (SUCCEEDED(result) && (displayName.uType != STRRET_WSTR || !displayName.pOleStr ||
            _wcsnicmp(displayName.pOleStr, L"edpath:", 7) != 0))
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        STRRET friendlyName{};
        if (SUCCEEDED(result)) result = parsingRoot->GetDisplayNameOf(
            reinterpret_cast<PCUITEMID_CHILD>(pathItem), SHGDN_INFOLDER, &friendlyName);
        if (SUCCEEDED(result) && (friendlyName.uType != STRRET_WSTR || !friendlyName.pOleStr ||
            _wcsicmp(friendlyName.pOleStr, L"second") != 0))
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        PIDLIST_RELATIVE reparsedItem = nullptr;
        if (SUCCEEDED(result)) result = parsingRoot->ParseDisplayName(nullptr, nullptr,
            displayName.pOleStr, nullptr, &reparsedItem, nullptr);
        if (SUCCEEDED(result) && (!reparsedItem || ILGetSize(pathItem) != ILGetSize(reparsedItem) ||
            memcmp(pathItem, reparsedItem, ILGetSize(pathItem)) != 0))
            result = HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
        IShellFolder2* rebound = nullptr;
        if (SUCCEEDED(result)) result = parsingRoot->BindToObject(
            reinterpret_cast<PCUIDLIST_RELATIVE>(pathItem), nullptr, IID_PPV_ARGS(&rebound));
        if (SUCCEEDED(result)) result = VerifyConfiguredRootForProbe(
            rebound, secondExpectedName, firstExpectedName, secondRecursiveBytes);
        if (rebound) rebound->Release();
        if (parsingRoot) parsingRoot->Release();
        if (reparsedItem) CoTaskMemFree(reparsedItem);
        if (displayName.uType == STRRET_WSTR && displayName.pOleStr) CoTaskMemFree(displayName.pOleStr);
        if (friendlyName.uType == STRRET_WSTR && friendlyName.pOleStr) CoTaskMemFree(friendlyName.pOleStr);
        if (pathItem) CoTaskMemFree(pathItem);
    }

    if (second) second->Release();
    if (first) first->Release();
    if (secondRoot) CoTaskMemFree(secondRoot);
    if (firstRoot) CoTaskMemFree(firstRoot);
    if (SUCCEEDED(result))
    {
        char message[192]{};
        sprintf_s(message, "synthetic-single-class-root-pidl: same-clsid=True; parse-bind-name-roundtrip=True; first=%ls:%lluB; second=%ls:%lluB",
            firstExpectedName, firstRecursiveBytes, secondExpectedName, secondRecursiveBytes);
        TraceSyntheticSortStage(message);
    }
    return result;
}

STDAPI DllCanUnloadNow()
{
    if (InterlockedCompareExchange(&g_syntheticLiveObjects, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_syntheticServerLocks, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_syntheticClassObjectCalls, 0, 0) != 0) return S_FALSE;

    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    HRESULT result = S_OK;
    if (InterlockedCompareExchange(&g_syntheticLiveObjects, 0, 0) == 0 &&
        InterlockedCompareExchange(&g_syntheticServerLocks, 0, 0) == 0 &&
        InterlockedCompareExchange(&g_syntheticClassObjectCalls, 0, 0) == 0)
    {
        auto* store = g_sharedRecursiveSizeStore;
        g_sharedRecursiveSizeStore = nullptr;
        if (store) store->Release();
    }
    else result = S_FALSE;
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticRecursiveSizeCacheUnloadLifecycleSmoke()
{
    if (DllCanUnloadNow() != S_OK) return E_UNEXPECTED;

    auto* firstStore = AcquireSharedRecursiveSizeStore();
    if (!firstStore) return E_OUTOFMEMORY;
    SyntheticFileIdentity identity{};
    identity.volumeSerial = 0x1234;
    identity.fileId[0] = 0x5A;
    identity.available = true;
    const auto setResult = firstStore->Set(L"cache-lifecycle-probe", identity, 42);
    const auto firstStoreCount = firstStore->Count();
    firstStore->Release();
    if (FAILED(setResult)) return setResult;
    if (firstStoreCount != 1) return E_FAIL;

    // DllCanUnloadNow drops the DLL-owned cache reference once all Shell COM objects are gone.
    if (DllCanUnloadNow() != S_OK) return E_UNEXPECTED;
    AcquireSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    const bool oldStoreReleased = g_sharedRecursiveSizeStore == nullptr;
    ReleaseSRWLockShared(&g_sharedRecursiveSizeStoreLock);
    if (!oldStoreReleased) return E_FAIL;

    auto* reopenedStore = AcquireSharedRecursiveSizeStore();
    if (!reopenedStore) return E_OUTOFMEMORY;
    const bool reopenedEmpty = reopenedStore->Count() == 0;
    reopenedStore->Release();
    const auto finalUnload = DllCanUnloadNow();
    return reopenedEmpty && finalUnload == S_OK ? S_OK : E_FAIL;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticDllUnloadContractSmoke()
{
    const auto before = DllCanUnloadNow();
    auto* enumerator = new (std::nothrow) SyntheticChildEnumerator({});
    if (!enumerator) return E_OUTOFMEMORY;
    const auto whileEnumeratorAlive = DllCanUnloadNow();
    enumerator->Release();
    const auto afterEnumerator = DllCanUnloadNow();
    auto* factory = new (std::nothrow) SyntheticFolderClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    const auto whileFactoryAlive = DllCanUnloadNow();
    factory->Release();
    const auto afterFactory = DllCanUnloadNow();
    char message[192]{};
    sprintf_s(message, "synthetic-dll-unload-contract: before=0x%08X; enumerator=0x%08X->0x%08X; factory=0x%08X->0x%08X",
        static_cast<unsigned>(before), static_cast<unsigned>(whileEnumeratorAlive), static_cast<unsigned>(afterEnumerator),
        static_cast<unsigned>(whileFactoryAlive), static_cast<unsigned>(afterFactory));
    OutputDebugStringA(message);
    return before == S_OK && whileEnumeratorAlive == S_FALSE && afterEnumerator == S_OK &&
        whileFactoryAlive == S_FALSE && afterFactory == S_OK ? S_OK : E_FAIL;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateSyntheticSortShellView(PCIDLIST_ABSOLUTE rootPidl, IShellFolder2** folder, IShellView** view)
{
    TraceSyntheticSortStage("synthetic-sort: export-enter");
    if (!rootPidl || !folder || !view) return E_POINTER;
    *folder = nullptr;
    *view = nullptr;
    IShellFolder* parent = nullptr;
    PCUITEMID_CHILD relativePidl = nullptr;
    auto result = SHBindToParent(rootPidl, IID_PPV_ARGS(&parent), &relativePidl);
    TraceSyntheticSortStage("synthetic-sort: SHBindToParent-return");
    if (FAILED(result)) return result;
    result = parent->BindToObject(relativePidl, nullptr, IID_PPV_ARGS(folder));
    parent->Release();
    TraceSyntheticSortStage("synthetic-sort: namespace-BindToObject-return");
    if (FAILED(result)) return result;
    TraceSyntheticSortStage("synthetic-sort: namespace-CreateViewObject-enter");
    result = (*folder)->CreateViewObject(nullptr, IID_IShellView, reinterpret_cast<void**>(view));
    TraceSyntheticSortStage("synthetic-sort: export-create-view-object-return");
    if (FAILED(result)) { (*folder)->Release(); *folder = nullptr; }
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI CreateSyntheticSortShellViewDirectForProbe(
    PCIDLIST_ABSOLUTE rootPidl, const CLSID* classId, IShellFolder2** folder, IShellView** view)
{
    if (!rootPidl || !classId || !folder || !view) return E_INVALIDARG;
    *folder = nullptr;
    *view = nullptr;
    auto* directFolder = new (std::nothrow) SyntheticSortFolder(nullptr, *classId);
    if (!directFolder) return E_OUTOFMEMORY;
    auto result = directFolder->Initialize(rootPidl);
    if (SUCCEEDED(result))
        result = directFolder->CreateViewObject(nullptr, IID_IShellView, reinterpret_cast<void**>(view));
    if (FAILED(result))
    {
        if (*view) { (*view)->Release(); *view = nullptr; }
        directFolder->Release();
        return result;
    }
    *folder = directFolder;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI VerifySyntheticShellActivatedRecursiveSizeSnapshotCache(
    PCIDLIST_ABSOLUTE rootPidl, ULONGLONG sentinelBytes)
{
    if (!rootPidl || sentinelBytes == 0) return E_INVALIDARG;

    IShellFolder* parent = nullptr;
    PCUITEMID_CHILD relativeRoot = nullptr;
    IShellFolder2* first = nullptr;
    IShellFolder2* second = nullptr;
    IUnknown* firstIdentity = nullptr;
    IUnknown* secondIdentity = nullptr;
    PITEMID_CHILD secondChild = nullptr;
    SHELLDETAILS details{};
    HRESULT result = SHBindToParent(rootPidl, IID_PPV_ARGS(&parent), &relativeRoot);
    if (FAILED(result)) goto cleanup;
    result = parent->BindToObject(relativeRoot, nullptr, IID_PPV_ARGS(&first));
    if (FAILED(result)) goto cleanup;
    result = parent->BindToObject(relativeRoot, nullptr, IID_PPV_ARGS(&second));
    if (FAILED(result)) goto cleanup;

    result = first->QueryInterface(IID_PPV_ARGS(&firstIdentity));
    if (FAILED(result)) goto cleanup;
    result = second->QueryInterface(IID_PPV_ARGS(&secondIdentity));
    if (FAILED(result)) goto cleanup;
    if (firstIdentity == secondIdentity) { result = HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS); goto cleanup; }

    result = static_cast<SyntheticSortFolder*>(first)->PublishRecursiveSizeSnapshotForChildProbe(
        L"child", sentinelBytes, 0, 0, 0);
    if (FAILED(result)) goto cleanup;
    result = second->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &secondChild, nullptr);
    if (FAILED(result)) goto cleanup;
    result = second->GetDetailsOf(secondChild, 1, &details);
    if (FAILED(result)) goto cleanup;
    if (details.str.uType != STRRET_WSTR || !details.str.pOleStr ||
        wcscmp(details.str.pOleStr, std::to_wstring(sentinelBytes).c_str()) != 0)
        result = E_FAIL;

cleanup:
    if (details.str.uType == STRRET_WSTR) CoTaskMemFree(details.str.pOleStr);
    if (secondChild) CoTaskMemFree(secondChild);
    if (secondIdentity) secondIdentity->Release();
    if (firstIdentity) firstIdentity->Release();
    if (second) second->Release();
    if (first) first->Release();
    if (parent) parent->Release();
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticSortBackingPath(PCWSTR path)
{
    if (!path || !*path) return E_INVALIDARG;
    try { g_syntheticSortBackingPath.assign(path); }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI MeasureSyntheticFolderEnumerationScanCostForProbe(
    PCWSTR backingPath, LONG recursiveSizeEnabled, LONG* directRows, LONG* metricCalls,
    LONG* entriesVisited, ULONGLONG* elapsedMicroseconds)
{
    if (!backingPath || !*backingPath || !directRows || !metricCalls || !entriesVisited || !elapsedMicroseconds)
        return E_INVALIDARG;
    *directRows = 0;
    *metricCalls = 0;
    *entriesVisited = 0;
    *elapsedMicroseconds = 0;
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result)) return result;

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    IEnumIDList* enumerator = nullptr;
    LARGE_INTEGER frequency{}, start{}, end{};
    LONG previousScanSetting = -1;
    result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (FAILED(result)) goto cleanup;
    root = new (std::nothrow) SyntheticSortFolder();
    if (!root) { result = E_OUTOFMEMORY; goto cleanup; }
    result = root->Initialize(rootPidl);
    if (FAILED(result)) goto cleanup;

    InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);
    previousScanSetting = InterlockedExchange(&g_syntheticRecursiveSizeScanEnabledForProbe, recursiveSizeEnabled ? 1 : 0);
    QueryPerformanceFrequency(&frequency);
    QueryPerformanceCounter(&start);
    result = root->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, &enumerator);
    if (SUCCEEDED(result) && !enumerator) result = E_UNEXPECTED;
    if (SUCCEEDED(result))
    {
        while (true)
        {
            PITEMID_CHILD item = nullptr;
            ULONG fetched = 0;
            const auto nextResult = enumerator->Next(1, &item, &fetched);
            if (nextResult == S_FALSE) break;
            if (FAILED(nextResult) || fetched != 1)
            {
                if (item) CoTaskMemFree(item);
                result = FAILED(nextResult) ? nextResult : E_UNEXPECTED;
                break;
            }
            CoTaskMemFree(item);
            ++*directRows;
        }
    }
    QueryPerformanceCounter(&end);
    *metricCalls = InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0);
    *entriesVisited = InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0);
    if (frequency.QuadPart > 0 && end.QuadPart >= start.QuadPart)
    {
        const auto elapsedTicks = end.QuadPart - start.QuadPart;
        *elapsedMicroseconds = static_cast<ULONGLONG>(
            static_cast<long double>(elapsedTicks) * 1000000.0L / static_cast<long double>(frequency.QuadPart));
    }

cleanup:
    if (previousScanSetting >= 0)
        InterlockedExchange(&g_syntheticRecursiveSizeScanEnabledForProbe, previousScanSetting);
    if (enumerator) enumerator->Release();
    if (root) root->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticFolderClassIdForProbe(const CLSID* classId)
{
    if (!classId) return E_POINTER;
    if (InterlockedCompareExchange(&g_syntheticLiveObjects, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_syntheticServerLocks, 0, 0) != 0)
        return E_UNEXPECTED;
    g_syntheticFolderClassId = *classId;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticFolderTreeContractSmoke(PCWSTR backingPath)
{
    if (!backingPath || !*backingPath) return E_INVALIDARG;
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 1);
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result))
    {
        InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0);
        return result;
    }

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    IEnumIDList* enumerator = nullptr;
    PITEMID_CHILD childFolder = nullptr;
    PITEMID_CHILD childFile = nullptr;
    PITEMID_CHILD secondFile = nullptr;
    IShellFolder2* nested = nullptr;
    IEnumIDList* nestedEnumerator = nullptr;
    IPersistFolder2* nestedPersist = nullptr;
    IShellFolder2* deepFolder = nullptr;
    IEnumIDList* deepEnumerator = nullptr;
    PIDLIST_ABSOLUTE nestedRootPidl = nullptr;
    result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (FAILED(result)) goto cleanup;

    root = new (std::nothrow) SyntheticSortFolder();
    if (!root) { result = E_OUTOFMEMORY; goto cleanup; }
    result = root->Initialize(rootPidl);
    if (FAILED(result)) goto cleanup;

    InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);
    result = root->EnumObjects(nullptr, SHCONTF_FOLDERS, &enumerator);
    if (FAILED(result) || !enumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    const auto enumerationMetricCalls = InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0);
    const auto enumerationMetricEntries = InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0);
    char enumerationMetricsTrace[192]{};
    StringCchPrintfA(enumerationMetricsTrace, ARRAYSIZE(enumerationMetricsTrace),
        "synthetic-folder-tree: root-enumeration-recursive-scan-calls=%ld; subtree-entries-visited=%ld",
        enumerationMetricCalls, enumerationMetricEntries);
    TraceSyntheticSortStage(enumerationMetricsTrace);
    if (enumerationMetricCalls != 2 || enumerationMetricEntries != 3)
    { result = E_FAIL; goto cleanup; }
    {
        PITEMID_CHILD item = nullptr;
        ULONG fetched = 0;
        result = enumerator->Next(1, &item, &fetched);
        if (result != S_OK || fetched != 1 || !SyntheticData(item) ||
            _wcsicmp(SyntheticData(item)->name, L"child") != 0 ||
            !(SyntheticData(item)->attributes & FILE_ATTRIBUTE_DIRECTORY) ||
            SyntheticData(item)->size != 10)
        {
            if (item) CoTaskMemFree(item);
            result = E_FAIL;
            goto cleanup;
        }
        PIDLIST_RELATIVE rootFileForSort = nullptr;
        ULONG sortCharactersEaten = 0;
        WCHAR sortRootFileName[] = L"root.txt";
        result = root->ParseDisplayName(nullptr, nullptr, sortRootFileName, &sortCharactersEaten,
            &rootFileForSort, nullptr);
        if (FAILED(result) || !rootFileForSort || sortCharactersEaten != ARRAYSIZE(sortRootFileName) - 1)
        {
            if (rootFileForSort) CoTaskMemFree(rootFileForSort);
            CoTaskMemFree(item);
            result = E_FAIL;
            goto cleanup;
        }
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
        const auto recursiveFolderSort = root->CompareIDs(1, item, reinterpret_cast<PCUITEMID_CHILD>(rootFileForSort));
        InterlockedExchange(&g_syntheticIgnoreRecursiveFolderSizeSortForProbe, 1);
        const auto ignoredFolderSort = root->CompareIDs(1, item, reinterpret_cast<PCUITEMID_CHILD>(rootFileForSort));
        InterlockedExchange(&g_syntheticIgnoreRecursiveFolderSizeSortForProbe, 0);
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
        CoTaskMemFree(rootFileForSort);
        const auto recursiveFolderSortValue = static_cast<short>(HRESULT_CODE(recursiveFolderSort));
        const auto ignoredFolderSortValue = static_cast<short>(HRESULT_CODE(ignoredFolderSort));
        if (recursiveFolderSortValue <= 0 || ignoredFolderSortValue >= 0 ||
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) != enumerationMetricCalls ||
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0) != enumerationMetricEntries)
        {
            CoTaskMemFree(item);
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-sort-ignore-failed");
            result = E_FAIL;
            goto cleanup;
        }
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-sort-ignore-passed");
        SHCOLUMNID mappedSizeKey{};
        result = root->MapColumnToSCID(1, &mappedSizeKey);
        const bool standardSizeKeyMapped = SUCCEEDED(result) && mappedSizeKey == kSizeKey;
        if (!standardSizeKeyMapped) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        SHELLDETAILS details{};
        result = root->GetDetailsOf(item, 1, &details);
        const bool recursiveSizeDisplayed = SUCCEEDED(result) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"10") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        if (!recursiveSizeDisplayed) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 1);
        SHELLDETAILS formattedDetails{};
        result = root->GetDetailsOf(item, 1, &formattedDetails);
        const bool humanSizeDisplayed = SUCCEEDED(result) && formattedDetails.str.uType == STRRET_WSTR &&
            formattedDetails.str.pOleStr && _wcsicmp(formattedDetails.str.pOleStr, L"10 B") == 0;
        if (formattedDetails.str.uType == STRRET_WSTR && formattedDetails.str.pOleStr) CoTaskMemFree(formattedDetails.str.pOleStr);
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 0);
        if (!humanSizeDisplayed) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
        InterlockedExchange64(&g_syntheticChildSizeOverride, 1234567);
        InterlockedExchange(&g_syntheticSizeDisplayUnitForProbe, 2); // Fixed KB.
        InterlockedExchange(&g_syntheticSizeDisplayDecimalsForProbe, 1);
        InterlockedExchange(&g_syntheticSizeDisplayThousandsForProbe, 0);
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 1);
        WCHAR expectedDecimalSeparator[8] = L".";
        GetLocaleInfoEx(LOCALE_NAME_USER_DEFAULT, LOCALE_SDECIMAL, expectedDecimalSeparator, ARRAYSIZE(expectedDecimalSeparator));
        const auto localizedExpected = [&](PCWSTR invariant)
        {
            auto expected = std::wstring(invariant);
            if (wcscmp(expectedDecimalSeparator, L".") != 0)
            {
                const auto point = expected.find(L'.');
                if (point != std::wstring::npos) expected.replace(point, 1, expectedDecimalSeparator);
            }
            return expected;
        };
        const auto displaysSizeText = [&](PCWSTR expected)
        {
            SHELLDETAILS formatted{};
            const auto detailsResult = root->GetDetailsOf(item, 1, &formatted);
            const bool matches = SUCCEEDED(detailsResult) && formatted.str.uType == STRRET_WSTR &&
                formatted.str.pOleStr && _wcsicmp(formatted.str.pOleStr, expected) == 0;
            if (formatted.str.uType == STRRET_WSTR && formatted.str.pOleStr) CoTaskMemFree(formatted.str.pOleStr);
            return matches;
        };
        const auto fixedKilobytesExpected = localizedExpected(L"1205.6 KB");
        const bool fixedKilobytesDisplayed = displaysSizeText(fixedKilobytesExpected.c_str());
        InterlockedExchange(&g_syntheticSizeDisplayDecimalsForProbe, 0);
        const bool wholeKilobytesDisplayed = displaysSizeText(L"1206 KB");
        InterlockedExchange(&g_syntheticSizeDisplayDecimalsForProbe, 3);
        const auto preciseKilobytesExpected = localizedExpected(L"1205.632 KB");
        const bool preciseKilobytesDisplayed = displaysSizeText(preciseKilobytesExpected.c_str());
        InterlockedExchange(&g_syntheticSizeDisplayUnitForProbe, 3); // Fixed MB.
        const auto fixedMegabytesExpected = localizedExpected(L"1.177 MB");
        const bool fixedMegabytesDisplayed = displaysSizeText(fixedMegabytesExpected.c_str());
        InterlockedExchange(&g_syntheticSizeDisplayUnitForProbe, 1); // Fixed bytes.
        InterlockedExchange(&g_syntheticSizeDisplayThousandsForProbe, 1);
        WCHAR expectedThousandsSeparator[8] = L",";
        GetLocaleInfoEx(LOCALE_NAME_USER_DEFAULT, LOCALE_STHOUSAND, expectedThousandsSeparator, ARRAYSIZE(expectedThousandsSeparator));
        const auto groupedBytesExpected = std::wstring(L"1") + expectedThousandsSeparator + L"234" + expectedThousandsSeparator + L"567 B";
        const bool groupedBytesDisplayed = displaysSizeText(groupedBytesExpected.c_str());
        VARIANT formattedSizeValue{};
        result = root->GetDetailsEx(item, &kSizeKey, &formattedSizeValue);
        const bool formattedSizePropertyRemainsRaw = SUCCEEDED(result) && formattedSizeValue.vt == VT_UI8 && formattedSizeValue.ullVal == 1234567;
        VariantClear(&formattedSizeValue);
        InterlockedExchange(&g_syntheticSizeDisplayUnitForProbe, 0);
        InterlockedExchange(&g_syntheticSizeDisplayDecimalsForProbe, 2);
        InterlockedExchange(&g_syntheticSizeDisplayThousandsForProbe, 0);
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 0);
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
        InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
        if (!fixedKilobytesDisplayed || !wholeKilobytesDisplayed || !preciseKilobytesDisplayed ||
            !fixedMegabytesDisplayed || !groupedBytesDisplayed || !formattedSizePropertyRemainsRaw)
        { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        VARIANT sizeValue{};
        result = root->GetDetailsEx(item, &kSizeKey, &sizeValue);
        const bool recursiveSizeProperty = SUCCEEDED(result) && sizeValue.vt == VT_UI8 && sizeValue.ullVal == 10;
        VariantClear(&sizeValue);
        if (!recursiveSizeProperty) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        SHELLDETAILS contentDetails{};
        result = root->GetDetailsOf(item, 2, &contentDetails);
        const bool recursiveContentsDisplayed = SUCCEEDED(result) && contentDetails.str.uType == STRRET_WSTR &&
            contentDetails.str.pOleStr && _wcsicmp(contentDetails.str.pOleStr, L"2 files, 1 folder") == 0;
        if (contentDetails.str.uType == STRRET_WSTR && contentDetails.str.pOleStr) CoTaskMemFree(contentDetails.str.pOleStr);
        if (!recursiveContentsDisplayed) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        SHCOLSTATEF relativeColumnState{};
        result = root->GetDefaultColumnState(3, &relativeColumnState);
        const bool relativeColumnEnabled = SUCCEEDED(result) &&
            (relativeColumnState & SHCOLSTATE_ONBYDEFAULT) != 0 &&
            (relativeColumnState & SHCOLSTATE_TYPEMASK) == SHCOLSTATE_TYPE_INT;
        if (!relativeColumnEnabled) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        SHCOLUMNID mappedRelativeSizeKey{};
        result = root->MapColumnToSCID(3, &mappedRelativeSizeKey);
        const bool relativeSizeKeyMapped = SUCCEEDED(result) && mappedRelativeSizeKey == kSyntheticRelativeSizeKey;
        if (!relativeSizeKeyMapped) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        VARIANT relativeSizeValue{};
        result = root->GetDetailsEx(item, &mappedRelativeSizeKey, &relativeSizeValue);
        const bool relativeSizeProperty = SUCCEEDED(result) && relativeSizeValue.vt == VT_R8 && relativeSizeValue.dblVal == 100.0;
        VariantClear(&relativeSizeValue);
        if (!relativeSizeProperty) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        SHELLDETAILS relativeSizeDetails{};
        result = root->GetDetailsOf(item, 3, &relativeSizeDetails);
        const bool relativeSizeDisplayed = SUCCEEDED(result) && relativeSizeDetails.str.uType == STRRET_WSTR &&
            relativeSizeDetails.str.pOleStr && _wcsicmp(relativeSizeDetails.str.pOleStr, L"100.000000%") == 0;
        if (relativeSizeDetails.str.uType == STRRET_WSTR && relativeSizeDetails.str.pOleStr) CoTaskMemFree(relativeSizeDetails.str.pOleStr);
        if (!relativeSizeDisplayed) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
        InterlockedExchange64(&g_syntheticChildSizeOverride, 1);
        ResetSyntheticSortCounters();
        SHCOLUMNID mappedRecursiveSizeKey{};
        result = root->MapColumnToSCID(1, &mappedRecursiveSizeKey);
        const bool recursiveSizeKeyMapped = SUCCEEDED(result) &&
            mappedRecursiveSizeKey == kSyntheticRecursiveSizeKey;
        if (!recursiveSizeKeyMapped)
        {
            InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
            InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
            CoTaskMemFree(item);
            result = E_FAIL;
            goto cleanup;
        }
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 1);
        InterlockedExchange64(&g_syntheticChildSizeOverride, 1536);
        details = {};
        result = root->GetDetailsOf(item, 1, &details);
        const bool automaticUnitDisplayed = SUCCEEDED(result) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"1.5 KB") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        VariantInit(&sizeValue);
        result = root->GetDetailsEx(item, &mappedRecursiveSizeKey, &sizeValue);
        const bool numericValueUnaffected = SUCCEEDED(result) && sizeValue.vt == VT_UI8 && sizeValue.ullVal == 1536;
        VariantClear(&sizeValue);
        InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 0);
        InterlockedExchange64(&g_syntheticChildSizeOverride, 1);
        if (!automaticUnitDisplayed || !numericValueUnaffected)
        {
            InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
            InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
            CoTaskMemFree(item);
            result = E_FAIL;
            goto cleanup;
        }
        details = {};
        result = root->GetDetailsOf(item, 1, &details);
        const bool updatedSizeDisplayed = SUCCEEDED(result) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"1") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        VariantInit(&sizeValue);
        result = root->GetDetailsEx(item, &mappedRecursiveSizeKey, &sizeValue);
        const bool updatedSizeProperty = SUCCEEDED(result) && sizeValue.vt == VT_UI8 && sizeValue.ullVal == 1;
        VariantClear(&sizeValue);
        if (!updatedSizeDisplayed || !updatedSizeProperty) { CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        const auto sizeDetailsBeforeObservation = InterlockedCompareExchange(&g_syntheticSizeDetailsOfItemCount, 0, 0);
        const auto customPropertyBeforeObservation = InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExCount, 0, 0);
        const auto sizeCompareBeforeObservation = InterlockedCompareExchange(&g_syntheticSizeCompareCount, 0, 0);
        const auto enumCallsBeforeObservation = InterlockedCompareExchange(&g_syntheticEnumObjectsCount, 0, 0);
        const auto enumeratedRowsBeforeObservation = InterlockedCompareExchange(&g_syntheticEnumeratedItems, 0, 0);
        g_syntheticSizeMetricsObservation = TRUE;
        SHELLDETAILS observedDetails{};
        const auto observedDetailsResult = root->GetDetailsOf(item, 1, &observedDetails);
        if (observedDetails.str.uType == STRRET_WSTR && observedDetails.str.pOleStr)
            CoTaskMemFree(observedDetails.str.pOleStr);
        VARIANT observedProperty{};
        const auto observedPropertyResult = root->GetDetailsEx(item, &kSyntheticRecursiveSizeKey, &observedProperty);
        const bool observedPropertyValue = SUCCEEDED(observedPropertyResult) &&
            observedProperty.vt == VT_UI8 && observedProperty.ullVal == 1;
        VariantClear(&observedProperty);
        const auto observedCompareResult = root->CompareIDs(1, item, item);
        IEnumIDList* observedEnumerator = nullptr;
        auto observedEnumResult = root->EnumObjects(nullptr, SHCONTF_FOLDERS, &observedEnumerator);
        PITEMID_CHILD observedRow = nullptr;
        ULONG observedFetched = 0;
        if (SUCCEEDED(observedEnumResult) && observedEnumerator)
            observedEnumResult = observedEnumerator->Next(1, &observedRow, &observedFetched);
        g_syntheticSizeMetricsObservation = FALSE;
        if (observedRow) CoTaskMemFree(observedRow);
        if (observedEnumerator) observedEnumerator->Release();
        const bool observerCallbacksExcluded =
            InterlockedCompareExchange(&g_syntheticSizeDetailsOfItemCount, 0, 0) == sizeDetailsBeforeObservation &&
            InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExCount, 0, 0) == customPropertyBeforeObservation &&
            InterlockedCompareExchange(&g_syntheticSizeCompareCount, 0, 0) == sizeCompareBeforeObservation &&
            InterlockedCompareExchange(&g_syntheticEnumObjectsCount, 0, 0) == enumCallsBeforeObservation &&
            InterlockedCompareExchange(&g_syntheticEnumeratedItems, 0, 0) == enumeratedRowsBeforeObservation;
        if (FAILED(observedDetailsResult) || !observedPropertyValue || FAILED(observedCompareResult) ||
            FAILED(observedEnumResult) || observedFetched != 1 || !observerCallbacksExcluded)
        {
            CoTaskMemFree(item);
            result = E_FAIL;
            goto cleanup;
        }
        TraceSyntheticSortStage("synthetic-folder-tree: metric-observer-exclusion-passed");
        childFolder = item;
        item = nullptr;
        {
            WCHAR rootFileName[] = L"root.txt";
            ULONG rootFileNameEaten = 0;
            PIDLIST_RELATIVE rootFile = nullptr;
            result = root->ParseDisplayName(nullptr, nullptr, rootFileName,
                &rootFileNameEaten, &rootFile, nullptr);
            const auto overrideFolderAfterFile = SUCCEEDED(result) ? root->CompareIDs(1, childFolder, rootFile) : result;
            const auto overrideFileAfterFolder = SUCCEEDED(result) ? root->CompareIDs(1, rootFile, childFolder) : result;
            const bool recursiveSizeOverrideSortsNumerically = SUCCEEDED(result) && rootFile &&
                rootFileNameEaten == ARRAYSIZE(rootFileName) - 1 &&
                static_cast<SHORT>(HRESULT_CODE(overrideFolderAfterFile)) < 0 &&
                static_cast<SHORT>(HRESULT_CODE(overrideFileAfterFolder)) > 0;
            InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
            InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
            const auto folderAfterFile = SUCCEEDED(result) ? root->CompareIDs(1, childFolder, rootFile) : result;
            const auto fileAfterFolder = SUCCEEDED(result) ? root->CompareIDs(1, rootFile, childFolder) : result;
            const bool recursiveSizeSortsNumerically = SUCCEEDED(result) && rootFile &&
                rootFileNameEaten == ARRAYSIZE(rootFileName) - 1 &&
                static_cast<SHORT>(HRESULT_CODE(folderAfterFile)) > 0 &&
                static_cast<SHORT>(HRESULT_CODE(fileAfterFolder)) < 0;
            if (rootFile) CoTaskMemFree(rootFile);
            if (!recursiveSizeOverrideSortsNumerically || !recursiveSizeSortsNumerically)
            { result = E_FAIL; goto cleanup; }
        }
        result = enumerator->Next(1, &item, &fetched);
        if (item) CoTaskMemFree(item);
        if (result != S_FALSE || fetched != 0) { result = E_FAIL; goto cleanup; }
    }
    enumerator->Release();
    enumerator = nullptr;
    TraceSyntheticSortStage("synthetic-folder-tree: folder-enumeration-passed");

    {
        WCHAR childName[] = L"child";
        ULONG eaten = 0;
        SFGAOF attributes = SFGAO_FOLDER;
        PIDLIST_RELATIVE parsed = nullptr;
        result = root->ParseDisplayName(nullptr, nullptr, childName, &eaten, &parsed, &attributes);
        const bool parsedFolder = SUCCEEDED(result) && eaten == ARRAYSIZE(childName) - 1 &&
            SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed)) &&
            (SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed))->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
            (attributes & SFGAO_FOLDER);
        if (!parsedFolder)
        {
            char message[192]{};
            const auto* parsedChild = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed));
            sprintf_s(message, "synthetic-folder-tree: ParseDisplayName assertion failed hr=0x%08X eaten=%u pidl=%u attrs=0x%08X name=%ls childAttrs=0x%08X",
                static_cast<unsigned>(result), eaten, parsedChild ? 1u : 0u, static_cast<unsigned>(attributes),
                parsedChild ? parsedChild->name : L"<null>", parsedChild ? parsedChild->attributes : 0u);
            TraceSyntheticSortStage(message);
        }
        if (parsed) CoTaskMemFree(parsed);
        if (!parsedFolder) { result = E_FAIL; goto cleanup; }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: parse-display-name-passed");

    {
        WCHAR relativePath[] = L"child\\nested.txt";
        ULONG eaten = 0;
        PIDLIST_RELATIVE parsed = nullptr;
        result = root->ParseDisplayName(nullptr, nullptr, relativePath, &eaten, &parsed, nullptr);
        const auto* first = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed));
        const auto* second = parsed ? SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(ILNext(parsed))) : nullptr;
        const bool parsedPath = SUCCEEDED(result) && eaten == ARRAYSIZE(relativePath) - 1 && first && second &&
            _wcsicmp(first->name, L"child") == 0 && (first->attributes & FILE_ATTRIBUTE_DIRECTORY) &&
            _wcsicmp(second->name, L"nested.txt") == 0 && ILIsEmpty(ILNext(ILNext(parsed)));
        if (!parsedPath)
        {
            char message[224]{};
            sprintf_s(message, "synthetic-folder-tree: multi-component assertion failed hr=0x%08X eaten=%u pidl=%u first=%ls second=%ls",
                static_cast<unsigned>(result), eaten, parsed ? 1u : 0u,
                first ? first->name : L"<null>", second ? second->name : L"<null>");
            TraceSyntheticSortStage(message);
        }
        bool parsedStorage = false;
        if (parsedPath)
        {
            IStream* stream = nullptr;
            result = root->BindToStorage(parsed, nullptr, IID_PPV_ARGS(&stream));
            if (SUCCEEDED(result) && stream)
            {
                char contents[7]{};
                ULONG bytesRead = 0;
                result = stream->Read(contents, 6, &bytesRead);
                parsedStorage = SUCCEEDED(result) && bytesRead == 6 && std::string(contents, 6) == "nested";
                stream->Release();
            }
            if (!parsedStorage && SUCCEEDED(result)) result = E_FAIL;
        }
        if (parsed) CoTaskMemFree(parsed);
        if (!parsedPath || !parsedStorage) { result = E_FAIL; goto cleanup; }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: multi-component-parse-passed");
    TraceSyntheticSortStage("synthetic-folder-tree: multi-component-storage-passed");

    {
        WCHAR folderPath[] = L"child\\nested-folder";
        ULONG eaten = 0;
        PIDLIST_RELATIVE parsed = nullptr;
        result = root->ParseDisplayName(nullptr, nullptr, folderPath, &eaten, &parsed, nullptr);
        if (FAILED(result) || !parsed || eaten != ARRAYSIZE(folderPath) - 1)
        {
            if (parsed) CoTaskMemFree(parsed);
            if (SUCCEEDED(result)) result = E_FAIL;
            goto cleanup;
        }
        result = root->BindToObject(parsed, nullptr, IID_PPV_ARGS(&deepFolder));
        CoTaskMemFree(parsed);
        if (FAILED(result) || !deepFolder) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        result = deepFolder->EnumObjects(nullptr, SHCONTF_NONFOLDERS, &deepEnumerator);
        if (FAILED(result) || !deepEnumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        PITEMID_CHILD item = nullptr;
        ULONG fetched = 0;
        result = deepEnumerator->Next(1, &item, &fetched);
        const bool foundDeepFile = result == S_OK && fetched == 1 && SyntheticData(item) &&
            _wcsicmp(SyntheticData(item)->name, L"deep.txt") == 0;
        if (item) CoTaskMemFree(item);
        if (!foundDeepFile) { result = E_FAIL; goto cleanup; }
        deepEnumerator->Release();
        deepEnumerator = nullptr;
        deepFolder->Release();
        deepFolder = nullptr;
    }
    TraceSyntheticSortStage("synthetic-folder-tree: multi-component-bind-passed");

    result = root->EnumObjects(nullptr, SHCONTF_NONFOLDERS, &enumerator);
    if (FAILED(result) || !enumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    {
        PITEMID_CHILD item = nullptr;
        ULONG fetched = 0;
        result = enumerator->Next(1, &item, &fetched);
        const bool correctFile = result == S_OK && fetched == 1 && SyntheticData(item) &&
            _wcsicmp(SyntheticData(item)->name, L"root.txt") == 0 &&
            !(SyntheticData(item)->attributes & FILE_ATTRIBUTE_DIRECTORY);
        if (!correctFile) { if (item) CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        childFile = item;
        item = nullptr;
        result = enumerator->Next(1, &item, &fetched);
        const bool correctSecondFile = result == S_OK && fetched == 1 && SyntheticData(item) &&
            _wcsicmp(SyntheticData(item)->name, L"second.txt") == 0 &&
            !(SyntheticData(item)->attributes & FILE_ATTRIBUTE_DIRECTORY);
        if (!correctSecondFile) { if (item) CoTaskMemFree(item); result = E_FAIL; goto cleanup; }
        secondFile = item;
        item = nullptr;
        result = enumerator->Next(1, &item, &fetched);
        if (item) CoTaskMemFree(item);
        if (result != S_FALSE || fetched != 0) { result = E_FAIL; goto cleanup; }
    }
    enumerator->Release();
    enumerator = nullptr;
    TraceSyntheticSortStage("synthetic-folder-tree: file-and-hidden-enumeration-passed");

    {
        PCUITEMID_CHILD selected[] = { childFile };
        IContextMenu* contextMenu = nullptr;
        result = root->GetUIObjectOf(nullptr, 1, selected, IID_IContextMenu, nullptr,
            reinterpret_cast<void**>(&contextMenu));
        if (FAILED(result) || !contextMenu) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        HMENU menu = CreatePopupMenu();
        if (!menu) { contextMenu->Release(); result = HRESULT_FROM_WIN32(GetLastError()); goto cleanup; }
        result = contextMenu->QueryContextMenu(menu, 0, 1, 0x7FFF, CMF_DEFAULTONLY);
        const bool hasNativeCommands = SUCCEEDED(result) && GetMenuItemCount(menu) > 0;
        DestroyMenu(menu);
        contextMenu->Release();
        if (!hasNativeCommands) { if (SUCCEEDED(result)) result = E_FAIL; goto cleanup; }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: native-item-context-menu-passed");

    {
        PCUITEMID_CHILD selected[] = { childFile, secondFile };
        IDataObject* data = nullptr;
        result = root->GetUIObjectOf(nullptr, ARRAYSIZE(selected), selected, IID_IDataObject, nullptr,
            reinterpret_cast<void**>(&data));
        if (FAILED(result) || !data) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        FORMATETC format{ CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
        STGMEDIUM medium{};
        result = data->GetData(&format, &medium);
        if (FAILED(result)) { data->Release(); goto cleanup; }
        const auto drop = static_cast<HDROP>(medium.hGlobal);
        const auto count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
        WCHAR droppedPaths[2][MAX_PATH]{};
        bool allPathsRead = count == ARRAYSIZE(selected);
        for (UINT index = 0; allPathsRead && index < ARRAYSIZE(selected); ++index)
        {
            if (DragQueryFileW(drop, index, droppedPaths[index], ARRAYSIZE(droppedPaths[index])) == 0)
            {
                allPathsRead = false;
                break;
            }
        }
        const auto expectedRoot = std::wstring(backingPath) + L"\\root.txt";
        const auto expectedSecond = std::wstring(backingPath) + L"\\second.txt";
        const bool hasSelectedPaths = allPathsRead &&
            ((_wcsicmp(droppedPaths[0], expectedRoot.c_str()) == 0 && _wcsicmp(droppedPaths[1], expectedSecond.c_str()) == 0) ||
             (_wcsicmp(droppedPaths[1], expectedRoot.c_str()) == 0 && _wcsicmp(droppedPaths[0], expectedSecond.c_str()) == 0));
        ReleaseStgMedium(&medium);
        if (!hasSelectedPaths)
        {
            data->Release();
            result = E_FAIL;
            goto cleanup;
        }
        result = TransferSyntheticSelectionWithoutUi(data, backingPath, false);
        if (SUCCEEDED(result)) result = CopySyntheticSelectionCollisionWithoutUi(data, backingPath);
        data->Release();
        if (FAILED(result)) goto cleanup;
    }
    result = DeleteSyntheticCopiedSelectionWithoutUi(root, backingPath);
    if (FAILED(result)) goto cleanup;
    TraceSyntheticSortStage("synthetic-folder-tree: native-item-data-object-passed");
    TraceSyntheticSortStage("synthetic-folder-tree: multi-select-copyitems-passed");
    TraceSyntheticSortStage("synthetic-folder-tree: copy-collision-rename-passed");
    TraceSyntheticSortStage("synthetic-folder-tree: multi-select-deleteitems-passed");

    result = root->EnumObjects(nullptr, static_cast<SHCONTF>(SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN), &enumerator);
    if (FAILED(result) || !enumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    {
        ULONG count = 0;
        bool foundHidden = false;
        for (;;)
        {
            PITEMID_CHILD item = nullptr;
            ULONG fetched = 0;
            result = enumerator->Next(1, &item, &fetched);
            if (result == S_FALSE) break;
            if (FAILED(result) || fetched != 1 || !SyntheticData(item))
            {
                if (item) CoTaskMemFree(item);
                result = FAILED(result) ? result : E_FAIL;
                goto cleanup;
            }
            ++count;
            if (SyntheticData(item)->attributes & FILE_ATTRIBUTE_HIDDEN) foundHidden = true;
            CoTaskMemFree(item);
        }
        if (count != 3 || !foundHidden) { result = E_FAIL; goto cleanup; }
    }
    enumerator->Release();
    enumerator = nullptr;

    result = root->BindToObject(childFolder, nullptr, IID_PPV_ARGS(&nested));
    if (FAILED(result) || !nested) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    result = nested->QueryInterface(IID_PPV_ARGS(&nestedPersist));
    if (FAILED(result) || !nestedPersist) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    result = nestedPersist->GetCurFolder(&nestedRootPidl);
    if (FAILED(result) || !nestedRootPidl || !ILIsParent(rootPidl, nestedRootPidl, TRUE))
    {
        if (SUCCEEDED(result)) result = E_FAIL;
        goto cleanup;
    }
    CoTaskMemFree(nestedRootPidl);
    nestedRootPidl = nullptr;
    nestedPersist->Release();
    nestedPersist = nullptr;
    result = nested->EnumObjects(nullptr, SHCONTF_NONFOLDERS, &nestedEnumerator);
    if (FAILED(result) || !nestedEnumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
    {
        PITEMID_CHILD item = nullptr;
        ULONG fetched = 0;
        result = nestedEnumerator->Next(1, &item, &fetched);
        const bool correctFile = result == S_OK && fetched == 1 && SyntheticData(item) &&
            _wcsicmp(SyntheticData(item)->name, L"nested.txt") == 0;
        if (item) CoTaskMemFree(item);
        if (!correctFile) { result = E_FAIL; goto cleanup; }
        item = nullptr;
        result = nestedEnumerator->Next(1, &item, &fetched);
        if (item) CoTaskMemFree(item);
        if (result != S_FALSE || fetched != 0) { result = E_FAIL; goto cleanup; }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: nested-enumeration-passed");
    {
        WCHAR fileName[] = L"nested.txt";
        ULONG eaten = 0;
        PIDLIST_RELATIVE parsed = nullptr;
        result = nested->ParseDisplayName(nullptr, nullptr, fileName, &eaten, &parsed, nullptr);
        if (FAILED(result) || !parsed || eaten != ARRAYSIZE(fileName) - 1 ||
            !SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed)) ||
            _wcsicmp(SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(parsed))->name, fileName) != 0)
        {
            if (parsed) CoTaskMemFree(parsed);
            result = E_FAIL;
            goto cleanup;
        }
        IStream* stream = nullptr;
        result = nested->BindToStorage(parsed, nullptr, IID_PPV_ARGS(&stream));
        CoTaskMemFree(parsed);
        if (FAILED(result) || !stream) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        char contents[7]{};
        ULONG bytesRead = 0;
        result = stream->Read(contents, 6, &bytesRead);
        stream->Release();
        if (FAILED(result) || bytesRead != 6 || std::string(contents, 6) != "nested")
        {
            result = E_FAIL;
            goto cleanup;
        }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: nested-storage-passed");
    {
        const auto* child = SyntheticData(childFolder);
        if (!child) { result = E_FAIL; goto cleanup; }
        SyntheticFileIdentity siblingIdentity{};
        siblingIdentity.volumeSerial = child->identity.volumeSerial;
        siblingIdentity.fileId[0] = static_cast<BYTE>(child->identity.fileId[0] ^ 0x5A);
        siblingIdentity.available = child->identity.available;
        SyntheticRecursiveSizeSnapshotStore invalidationStore;
        std::wstring subtree = backingPath;
        std::wstring nestedPath;
        std::wstring siblingPath;
        try
        {
            subtree += L"\\child";
            nestedPath = subtree + L"\\nested";
            siblingPath = backingPath;
            siblingPath += L"\\child-sibling";
        }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        if (FAILED(invalidationStore.Set(subtree.c_str(), child->identity, 11)) ||
            FAILED(invalidationStore.Set(nestedPath.c_str(), child->identity, 12)) ||
            FAILED(invalidationStore.Set(siblingPath.c_str(), siblingIdentity, 22)) ||
            invalidationStore.RemoveSubtree(subtree.c_str()) != 2 ||
            invalidationStore.Count() != 1)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-subtree-invalidation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        SyntheticRecursiveSizeSnapshotStore changedPathStore;
        std::wstring changedFilePath;
        try { changedFilePath = nestedPath + L"\\leaf.txt"; }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        if (FAILED(changedPathStore.Set(backingPath, child->identity, 1)) ||
            FAILED(changedPathStore.Set(subtree.c_str(), child->identity, 2)) ||
            FAILED(changedPathStore.Set(nestedPath.c_str(), child->identity, 3)) ||
            FAILED(changedPathStore.Set(siblingPath.c_str(), siblingIdentity, 4)) ||
            changedPathStore.InvalidatePath(changedFilePath.c_str(), false) != 3 ||
            changedPathStore.Count() != 1)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-ancestor-invalidation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-ancestor-invalidation-passed");
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-subtree-invalidation-passed");

        SyntheticRecursiveSizeSnapshotStore inFlightInvalidationStore;
        ULONGLONG inFlightCachedBytes = 0;
        const auto reservation = inFlightInvalidationStore.TryReserveScan(
            nestedPath, child->identity, &inFlightCachedBytes);
        std::wstring changedDescendant;
        try { changedDescendant = nestedPath + L"\\new-file.txt"; }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        const auto inFlightInvalidation = inFlightInvalidationStore.InvalidatePath(
            changedDescendant.c_str(), false);
        const auto staleScanCommit = inFlightInvalidationStore.Set(
            nestedPath.c_str(), child->identity, 99);
        ULONGLONG unexpectedStaleBytes = 0;
        if (reservation != SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired ||
            inFlightInvalidation != 0 || staleScanCommit != S_FALSE ||
            inFlightInvalidationStore.TryGet(nestedPath, child->identity, &unexpectedStaleBytes))
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-inflight-invalidation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        inFlightInvalidationStore.ReleaseScan(nestedPath);

        SyntheticRecursiveSizeSnapshotStore subtreeInvalidationStore;
        if (subtreeInvalidationStore.TryReserveScan(nestedPath, child->identity, &inFlightCachedBytes) !=
                SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-subtree-inflight-reservation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        subtreeInvalidationStore.RemoveSubtree(nestedPath.c_str());
        if (subtreeInvalidationStore.Set(nestedPath.c_str(), child->identity, 31) != S_FALSE)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-subtree-inflight-invalidation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        subtreeInvalidationStore.ReleaseScan(nestedPath);
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-subtree-inflight-invalidation-passed");

        if (inFlightInvalidationStore.TryReserveScan(nestedPath, child->identity, &inFlightCachedBytes) !=
                SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired ||
            FAILED(inFlightInvalidationStore.Set(nestedPath.c_str(), child->identity, 23)) ||
            !inFlightInvalidationStore.TryGet(nestedPath, child->identity, &inFlightCachedBytes) ||
            inFlightCachedBytes != 23)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-inflight-retry-failed");
            result = E_FAIL;
            goto cleanup;
        }
        inFlightInvalidationStore.ReleaseScan(nestedPath);
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-inflight-invalidation-passed");

        // Exercise the real SFVM_FSNOTIFY PIDL pair path without creating a Shell view.
        std::wstring renameDestination;
        try { renameDestination = std::wstring(backingPath) + L"\\rename-destination"; }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        if (!CreateDirectoryW(renameDestination.c_str(), nullptr))
        {
            result = HRESULT_FROM_WIN32(GetLastError());
            goto cleanup;
        }
        PIDLIST_ABSOLUTE oldRenamePidl = nullptr;
        PIDLIST_ABSOLUTE newRenamePidl = nullptr;
        SyntheticRecursiveSizeSnapshotStore* renameStore = new (std::nothrow) SyntheticRecursiveSizeSnapshotStore();
        SyntheticSortFolder* renameFolder = renameStore ? new (std::nothrow) SyntheticSortFolder(renameStore) : nullptr;
        if (renameStore) renameStore->Release();
        result = renameFolder ? renameFolder->Initialize(rootPidl) : E_OUTOFMEMORY;
        if (SUCCEEDED(result)) result = SHParseDisplayName(subtree.c_str(), nullptr, &oldRenamePidl, 0, nullptr);
        if (SUCCEEDED(result)) result = SHParseDisplayName(renameDestination.c_str(), nullptr, &newRenamePidl, 0, nullptr);
        if (SUCCEEDED(result))
        {
            ULONGLONG ignoredBytes = 0;
            const auto oldReservation = renameStore->TryReserveScan(subtree, child->identity, &ignoredBytes);
            const auto newReservation = renameStore->TryReserveScan(renameDestination, child->identity, &ignoredBytes);
            if (oldReservation != SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired ||
                newReservation != SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired)
                result = E_FAIL;
            else
            {
                PCIDLIST_ABSOLUTE renamePidls[] = { oldRenamePidl, newRenamePidl };
                const auto previousTranslation = InterlockedExchange(&g_syntheticTranslateChildNotificationsForProbe, 0);
                const auto notificationResult = renameFolder->MessageSFVCB(SFVM_FSNOTIFY,
                    reinterpret_cast<WPARAM>(renamePidls), SHCNE_RENAMEFOLDER);
                InterlockedExchange(&g_syntheticTranslateChildNotificationsForProbe, previousTranslation);
                const auto oldCommit = renameStore->Set(subtree.c_str(), child->identity, 41);
                const auto newCommit = renameStore->Set(renameDestination.c_str(), child->identity, 42);
                if (notificationResult != E_NOTIMPL || oldCommit != S_FALSE || newCommit != S_FALSE)
                    result = E_FAIL;
                else
                    TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-rename-pair-inflight-invalidation-passed");
                renameStore->ReleaseScan(subtree);
                renameStore->ReleaseScan(renameDestination);

                if (SUCCEEDED(result))
                {
                    std::wstring oldFilePath = backingPath;
                    std::wstring newFilePath = subtree;
                    try
                    {
                        oldFilePath += L"\\root.txt";
                        newFilePath += L"\\nested.txt";
                    }
                    catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; }
                    PIDLIST_ABSOLUTE oldFilePidl = nullptr;
                    PIDLIST_ABSOLUTE newFilePidl = nullptr;
                    if (SUCCEEDED(result)) result = SHParseDisplayName(oldFilePath.c_str(), nullptr, &oldFilePidl, 0, nullptr);
                    if (SUCCEEDED(result)) result = SHParseDisplayName(newFilePath.c_str(), nullptr, &newFilePidl, 0, nullptr);
                    if (SUCCEEDED(result))
                    {
                        const auto oldParentReservation = renameStore->TryReserveScan(backingPath, child->identity, &ignoredBytes);
                        const auto newParentReservation = renameStore->TryReserveScan(subtree, child->identity, &ignoredBytes);
                        if (oldParentReservation != SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired ||
                            newParentReservation != SyntheticRecursiveSizeSnapshotStore::ScanReservation::Acquired)
                            result = E_FAIL;
                        else
                        {
                            PCIDLIST_ABSOLUTE renameFilePidls[] = { oldFilePidl, newFilePidl };
                            const auto fileNotificationResult = renameFolder->MessageSFVCB(SFVM_FSNOTIFY,
                                reinterpret_cast<WPARAM>(renameFilePidls), SHCNE_RENAMEITEM);
                            const auto oldParentCommit = renameStore->Set(backingPath, child->identity, 51);
                            const auto newParentCommit = renameStore->Set(subtree.c_str(), child->identity, 52);
                            if (fileNotificationResult != E_NOTIMPL || oldParentCommit != S_FALSE || newParentCommit != S_FALSE)
                                result = E_FAIL;
                            else
                                TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-file-rename-pair-inflight-invalidation-passed");
                            renameStore->ReleaseScan(backingPath);
                            renameStore->ReleaseScan(subtree);
                        }
                    }
                    if (oldFilePidl) CoTaskMemFree(oldFilePidl);
                    if (newFilePidl) CoTaskMemFree(newFilePidl);
                }
            }
        }
        if (oldRenamePidl) CoTaskMemFree(oldRenamePidl);
        if (newRenamePidl) CoTaskMemFree(newRenamePidl);
        if (renameFolder) renameFolder->Release();
        RemoveDirectoryW(renameDestination.c_str());
        if (FAILED(result)) goto cleanup;

        SyntheticRecursiveSizeSnapshotStore boundedStore;
        std::wstring oldestPath;
        std::wstring secondPath;
        std::wstring newestPath;
        try
        {
            for (size_t index = 0; index < 256; ++index)
            {
                const auto path = std::wstring(backingPath) + L"\\cache-entry-" + std::to_wstring(index);
                if (index == 0) oldestPath = path;
                if (index == 1) secondPath = path;
                if (FAILED(boundedStore.Set(path.c_str(), child->identity, index)))
                {
                    result = E_FAIL;
                    goto cleanup;
                }
            }
            newestPath = std::wstring(backingPath) + L"\\cache-entry-newest";
        }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        ULONGLONG cachedBytes = 0;
        if (!boundedStore.TryGet(oldestPath, child->identity, &cachedBytes) || cachedBytes != 0 ||
            FAILED(boundedStore.Set(newestPath.c_str(), child->identity, 999)) ||
            boundedStore.Count() != 256 ||
            !boundedStore.TryGet(oldestPath, child->identity, &cachedBytes) || cachedBytes != 0 ||
            boundedStore.TryGet(secondPath, child->identity, &cachedBytes) ||
            !boundedStore.TryGet(newestPath, child->identity, &cachedBytes) || cachedBytes != 999)
        {
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-cache-lru-bound-failed");
            result = E_FAIL;
            goto cleanup;
        }
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-cache-lru-bound-passed");
    }
    nested->Release();
    nested = nullptr;
    result = S_OK;

    {
        const auto* originalChild = SyntheticData(childFolder);
        if (!originalChild) { result = E_FAIL; goto cleanup; }
        std::wstring oldParsingName = backingPath;
        try { oldParsingName += L"\\child"; }
        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup; }
        result = root->SetRecursiveSizeSnapshotForProbe(childFolder, oldParsingName.c_str(),
            originalChild->identity, 1234);
        if (FAILED(result)) goto cleanup;
        PITEMID_CHILD renamedFolder = nullptr;
        result = root->SetNameOf(nullptr, childFolder, L"renamed-child", SHGDN_INFOLDER, &renamedFolder);
        const bool renamed = SUCCEEDED(result) && renamedFolder && SyntheticData(renamedFolder) &&
            _wcsicmp(SyntheticData(renamedFolder)->name, L"renamed-child") == 0;
        if (!renamed || GetFileAttributesW((std::wstring(backingPath) + L"\\renamed-child\\nested.txt").c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            if (renamedFolder) CoTaskMemFree(renamedFolder);
            result = E_FAIL;
            goto cleanup;
        }
        std::wstring newParsingName = backingPath;
        try { newParsingName += L"\\renamed-child"; }
        catch (const std::bad_alloc&) { CoTaskMemFree(renamedFolder); result = E_OUTOFMEMORY; goto cleanup; }
        ULONGLONG renamedSnapshotBytes = 0;
        if (root->RecursiveSizeSnapshotCountForProbe() != 1 ||
            !root->TryGetRecursiveSizeSnapshotForProbe(newParsingName.c_str(), originalChild->identity, &renamedSnapshotBytes) ||
            renamedSnapshotBytes != 1234 ||
            root->TryGetRecursiveSizeSnapshotForProbe(oldParsingName.c_str(), originalChild->identity, &renamedSnapshotBytes))
        {
            CoTaskMemFree(renamedFolder);
            TraceSyntheticSortStage("synthetic-folder-tree: folder-rename-cache-migration-failed");
            result = E_FAIL;
            goto cleanup;
        }
        std::wstring changedFileParsingName = newParsingName;
        try { changedFileParsingName += L"\\nested.txt"; }
        catch (const std::bad_alloc&) { CoTaskMemFree(renamedFolder); result = E_OUTOFMEMORY; goto cleanup; }
        PIDLIST_ABSOLUTE changedFilePidl = nullptr;
        result = SHParseDisplayName(changedFileParsingName.c_str(), nullptr, &changedFilePidl, 0, nullptr);
        if (FAILED(result) || !changedFilePidl)
        {
            if (changedFilePidl) CoTaskMemFree(changedFilePidl);
            CoTaskMemFree(renamedFolder);
            if (SUCCEEDED(result)) result = E_UNEXPECTED;
            goto cleanup;
        }
        PCIDLIST_ABSOLUTE notificationPidls[2] = { changedFilePidl, nullptr };
        const auto notificationResult = root->MessageSFVCB(SFVM_FSNOTIFY,
            reinterpret_cast<WPARAM>(notificationPidls), SHCNE_DELETE);
        CoTaskMemFree(changedFilePidl);
        if (notificationResult != E_NOTIMPL || root->RecursiveSizeSnapshotCountForProbe() != 0)
        {
            CoTaskMemFree(renamedFolder);
            TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-parent-summary-invalidation-failed");
            result = E_FAIL;
            goto cleanup;
        }
        TraceSyntheticSortStage("synthetic-folder-tree: recursive-size-parent-summary-invalidation-passed");
        CoTaskMemFree(renamedFolder);
    }
    TraceSyntheticSortStage("synthetic-folder-tree: folder-rename-and-cache-migration-passed");

    {
        PCUITEMID_CHILD selected[] = { childFile, secondFile };
        IDataObject* data = nullptr;
        result = root->GetUIObjectOf(nullptr, ARRAYSIZE(selected), selected, IID_IDataObject, nullptr,
            reinterpret_cast<void**>(&data));
        if (FAILED(result) || !data) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
        result = TransferSyntheticSelectionWithoutUi(data, backingPath, true);
        data->Release();
        if (FAILED(result)) goto cleanup;
    }
    TraceSyntheticSortStage("synthetic-folder-tree: multi-select-moveitems-passed");

cleanup:
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0);
    if (nestedRootPidl) CoTaskMemFree(nestedRootPidl);
    if (nestedPersist) nestedPersist->Release();
    if (nestedEnumerator) nestedEnumerator->Release();
    if (nested) nested->Release();
    if (deepEnumerator) deepEnumerator->Release();
    if (deepFolder) deepFolder->Release();
    if (secondFile) CoTaskMemFree(secondFile);
    if (childFile) CoTaskMemFree(childFile);
    if (childFolder) CoTaskMemFree(childFolder);
    if (enumerator) enumerator->Release();
    if (root) root->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticOnDemandRecursiveSizeColumnSmoke(PCWSTR backingPath)
{
    if (!backingPath || !*backingPath) return E_INVALIDARG;
    const auto previousDemandSetting = InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 1);
    const auto previousCustomColumnSetting = InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
    const auto result = [&]() -> HRESULT
    {
        auto operationResult = SetSyntheticSortBackingPath(backingPath);
        if (FAILED(operationResult)) return operationResult;
        PIDLIST_ABSOLUTE rootPidl = nullptr;
        SyntheticSortFolder* root = nullptr;
        IEnumIDList* enumerator = nullptr;
        PITEMID_CHILD item = nullptr;
        const auto cleanup = [&]()
        {
            if (item) CoTaskMemFree(item);
            if (enumerator) enumerator->Release();
            if (root) root->Release();
            if (rootPidl) CoTaskMemFree(rootPidl);
        };

        operationResult = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
        if (FAILED(operationResult)) { cleanup(); return operationResult; }
        root = new (std::nothrow) SyntheticSortFolder();
        if (!root) { cleanup(); return E_OUTOFMEMORY; }
        operationResult = root->Initialize(rootPidl);
        if (FAILED(operationResult)) { cleanup(); return operationResult; }

        InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
        InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);
        operationResult = root->EnumObjects(nullptr, SHCONTF_FOLDERS, &enumerator);
        if (FAILED(operationResult) || !enumerator)
        {
            cleanup();
            return FAILED(operationResult) ? operationResult : E_UNEXPECTED;
        }
        const auto enumerateCalls = InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0);
        const auto enumerateEntries = InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0);
        if (enumerateCalls != 0 || enumerateEntries != 0) { cleanup(); return E_FAIL; }

        ULONG fetched = 0;
        operationResult = enumerator->Next(1, &item, &fetched);
        const auto* child = SyntheticData(item);
        if (operationResult != S_OK || fetched != 1 || !child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY))
        { cleanup(); return E_FAIL; }

        SHELLDETAILS details{};
        operationResult = root->GetDetailsOf(item, 1, &details);
        const bool firstValueMatches = SUCCEEDED(operationResult) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"26") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        const auto requestedCalls = InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0);
        const auto requestedEntries = InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0);
        if (!firstValueMatches || requestedCalls != 18 || requestedEntries != 35)
        {
            char message[192]{};
            sprintf_s(message, "synthetic-on-demand-size: deep-read hr=0x%08X; value-matched=%d; calls=%ld; entries=%ld",
                static_cast<unsigned>(operationResult), firstValueMatches ? 1 : 0, requestedCalls, requestedEntries);
            TraceSyntheticSortStage(message);
            cleanup();
            return E_FAIL;
        }

        SHCOLUMNID recursiveSizeKey{};
        operationResult = root->MapColumnToSCID(1, &recursiveSizeKey);
        VARIANT propertyValue{};
        if (SUCCEEDED(operationResult))
            operationResult = root->GetDetailsEx(item, &recursiveSizeKey, &propertyValue);
        const bool propertyValueMatches = SUCCEEDED(operationResult) && propertyValue.vt == VT_UI8 &&
            propertyValue.ullVal == 26;
        VariantClear(&propertyValue);
        if (!propertyValueMatches ||
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) != requestedCalls ||
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0) != requestedEntries)
        { cleanup(); return E_FAIL; }

        details = {};
        operationResult = root->GetDetailsOf(item, 1, &details);
        const bool cachedValueMatches = SUCCEEDED(operationResult) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"26") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        const bool repeatedReadWasCached =
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) == requestedCalls &&
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0) == requestedEntries;
        if (!cachedValueMatches || !repeatedReadWasCached) { cleanup(); return E_FAIL; }

        PITEMID_CHILD secondFolderItem = nullptr;
        ULONG secondFetched = 0;
        // Shell enumeration order is provider-controlled, so find the eviction row by name.
        do
        {
            operationResult = enumerator->Next(1, &secondFolderItem, &secondFetched);
            if (operationResult != S_OK || secondFetched != 1) break;
            const auto* candidate = SyntheticData(secondFolderItem);
            if (candidate && _wcsicmp(candidate->name, L"unreadable-root") == 0) break;
            CoTaskMemFree(secondFolderItem);
            secondFolderItem = nullptr;
        } while (true);
        if (operationResult != S_OK || secondFetched != 1 || !secondFolderItem)
        {
            if (secondFolderItem) CoTaskMemFree(secondFolderItem);
            cleanup();
            return E_FAIL;
        }
        details = {};
        operationResult = root->GetDetailsOf(secondFolderItem, 1, &details);
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        const auto scansAfterSecondFolder = InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0);
        if (FAILED(operationResult) || scansAfterSecondFolder != 2)
        {
            CoTaskMemFree(secondFolderItem);
            cleanup();
            return E_FAIL;
        }

        details = {};
        operationResult = root->GetDetailsOf(item, 1, &details);
        const bool evictedValueMatches = SUCCEEDED(operationResult) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && _wcsicmp(details.str.pOleStr, L"26") == 0;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        const auto scansAfterEvictedRead = InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0);
        CoTaskMemFree(secondFolderItem);
        if (!evictedValueMatches || scansAfterEvictedRead != scansAfterSecondFolder + 1)
        {
            TraceSyntheticSortStage("synthetic-on-demand-size: evicted-row-reread-did-not-rescan");
            cleanup();
            return E_FAIL;
        }
        const auto metricsAfterEvictedRead = InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0);
        const auto entriesAfterEvictedRead = InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0);

        operationResult = root->CompareIDs(1, item, item);
        const bool compareAvoidedScanning = SUCCEEDED(operationResult) &&
            InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0) == scansAfterEvictedRead &&
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) == metricsAfterEvictedRead &&
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0) == entriesAfterEvictedRead;
        cleanup();
        if (!compareAvoidedScanning) return E_FAIL;
        std::wstring unreadableRoot;
        try { unreadableRoot = std::wstring(backingPath) + L"\\unreadable-root"; }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        SyntheticDirectoryMetrics unreadableMetrics{};
        const auto unreadableResult = GetSyntheticDirectoryMetrics(unreadableRoot, &unreadableMetrics);
        if (unreadableResult != S_OK || unreadableMetrics.bytes != 5 || unreadableMetrics.files != 1 ||
            unreadableMetrics.folders != 2 || unreadableMetrics.unreadable != 1) return E_FAIL;
        SyntheticFileIdentity unreadableIdentity{};
        if (!TryGetSyntheticFileIdentity(unreadableRoot.c_str(), &unreadableIdentity)) return E_FAIL;
        SyntheticRecursiveSizeSnapshotStore unreadableStore;
        if (FAILED(unreadableStore.Set(unreadableRoot.c_str(), unreadableIdentity, unreadableMetrics.bytes,
            unreadableMetrics.files, unreadableMetrics.folders, unreadableMetrics.unreadable))) return E_FAIL;
        ULONGLONG cachedBytes = 0, cachedFiles = 0, cachedFolders = 0, cachedUnreadable = 0;
        if (!unreadableStore.TryGet(unreadableRoot, unreadableIdentity, &cachedBytes, &cachedFiles,
            &cachedFolders, &cachedUnreadable) || cachedBytes != 5 || cachedFiles != 1 ||
            cachedFolders != 2 || cachedUnreadable != 1) return E_FAIL;
        TraceSyntheticSortStage("synthetic-on-demand-size: enumeration=0; first-details-read=18-calls/35-entries; depth=16; total=26-bytes/18-files/17-folders; repeated-read=cached; capacity-1-eviction-reread=rescan; compareids=0-additional-scans; nested-access-denied=partial-5-bytes/1-file/2-folders/1-skipped");
        return S_OK;
    }();
    InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, previousDemandSetting);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, previousCustomColumnSetting);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticAsyncOnDemandRecursiveSizeColumnSmoke(PCWSTR backingPath, LONG requestCount)
{
    if (!backingPath || !*backingPath || requestCount < 1 || requestCount > 2048) return E_INVALIDARG;
    const bool batchByView = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeBatchByViewForProbe, 0, 0) != 0;
    const auto expectedWorkerCount = static_cast<LONG>(SyntheticAsyncRecursiveSizeQueue::kWorkerCount);
    const auto expectedTaskCount = batchByView ? 1L : 8L;
    const auto expectedFolderCount = static_cast<size_t>(requestCount) * 2;
    const auto previousDemandSetting = InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, 1);
    const auto previousAsyncSetting = InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, 1);
    const auto previousCustomColumnSetting = InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
    const auto previousExtendedMetadataSetting = InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 1);
    auto gate = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!gate)
    {
        InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, previousDemandSetting);
        InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, previousAsyncSetting);
        InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, previousCustomColumnSetting);
        InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, previousExtendedMetadataSetting);
        return HRESULT_FROM_WIN32(GetLastError());
    }
    g_syntheticAsyncRecursiveSizeScanGateForProbe = gate;
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0);
    InterlockedExchange(&g_syntheticOnDemandSizeScanCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);

    HRESULT result = SetSyntheticSortBackingPath(backingPath);
    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    IEnumIDList* enumerator = nullptr;
    std::vector<PITEMID_CHILD> items;
    if (SUCCEEDED(result)) result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (SUCCEEDED(result))
    {
        root = new (std::nothrow) SyntheticSortFolder();
        if (!root) result = E_OUTOFMEMORY;
    }
    if (SUCCEEDED(result)) result = root->Initialize(rootPidl);
    if (SUCCEEDED(result)) result = root->EnumObjects(nullptr, SHCONTF_FOLDERS, &enumerator);
    while (SUCCEEDED(result))
    {
        PITEMID_CHILD item = nullptr;
        ULONG fetched = 0;
        const auto nextResult = enumerator->Next(1, &item, &fetched);
        if (nextResult == S_FALSE) break;
        if (FAILED(nextResult) || fetched != 1 || !item)
        {
            if (item) CoTaskMemFree(item);
            result = FAILED(nextResult) ? nextResult : E_FAIL;
            break;
        }
        try { items.push_back(item); }
        catch (const std::bad_alloc&) { CoTaskMemFree(item); result = E_OUTOFMEMORY; break; }
    }
    if (SUCCEEDED(result) && items.size() != expectedFolderCount) { TraceSyntheticSortStage("async-queue-smoke: unexpected-folder-count"); result = E_FAIL; }

    for (size_t index = 0; SUCCEEDED(result) && index < static_cast<size_t>(requestCount); ++index)
    {
        auto item = items[index];
        SHELLDETAILS pendingContents{};
        const auto contentsResult = root->GetDetailsOf(item, 2, &pendingContents);
        const bool contentsReturnedPending = SUCCEEDED(contentsResult) && pendingContents.str.uType == STRRET_WSTR &&
            pendingContents.str.pOleStr && !*pendingContents.str.pOleStr &&
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) == 0;
        if (pendingContents.str.uType == STRRET_WSTR && pendingContents.str.pOleStr) CoTaskMemFree(pendingContents.str.pOleStr);
        if (FAILED(contentsResult) || !contentsReturnedPending)
        {
            TraceSyntheticSortStage("async-queue-smoke: recursive-count-read-did-not-return-pending");
            result = FAILED(contentsResult) ? contentsResult : E_FAIL;
            break;
        }
        SHELLDETAILS pendingDetails{};
        const auto detailsResult = root->GetDetailsOf(item, 1, &pendingDetails);
        const bool returnedPending = SUCCEEDED(detailsResult) && pendingDetails.str.uType == STRRET_WSTR &&
            pendingDetails.str.pOleStr && !*pendingDetails.str.pOleStr &&
            InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0) == 0 &&
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) == 0;
        if (pendingDetails.str.uType == STRRET_WSTR && pendingDetails.str.pOleStr) CoTaskMemFree(pendingDetails.str.pOleStr);
        if (FAILED(detailsResult) || !returnedPending) { TraceSyntheticSortStage("async-queue-smoke: first-read-did-not-return-pending"); result = FAILED(detailsResult) ? detailsResult : E_FAIL; break; }
    }

    // View batching must leave the coalescing gate before it can enqueue its bounded second chunk.
    if (batchByView) SetEvent(g_syntheticAsyncRecursiveSizeScanGateForProbe);
    const auto workerStartDeadline = GetTickCount64() + 5000;
    while (SUCCEEDED(result) &&
        InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0, 0) <
            expectedWorkerCount &&
        GetTickCount64() < workerStartDeadline) Sleep(1);
    const auto peakWorkers = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0, 0);
    if (SUCCEEDED(result) && peakWorkers != expectedWorkerCount) { TraceSyntheticSortStage("async-queue-smoke: expected-worker-count-not-reached"); result = E_FAIL; }
    if (SUCCEEDED(result) && !batchByView)
    {
        const auto reservationDeadline = GetTickCount64() + 2000;
        const auto* first = SyntheticData(items[0]);
        while (!root->HasRecursiveSizeScanReservationForChildProbe(first->name) && GetTickCount64() < reservationDeadline) Sleep(1);
        if (!root->HasRecursiveSizeScanReservationForChildProbe(first->name))
        {
            TraceSyntheticSortStage("async-queue-smoke: per-row-scan-has-no-invalidation-reservation");
            result = E_FAIL;
        }
    }

    SetEvent(g_syntheticAsyncRecursiveSizeScanGateForProbe);
    const auto deadline = GetTickCount64() + 10000;
    while (InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) < expectedTaskCount &&
        GetTickCount64() < deadline) Sleep(1);
    const bool workerCompleted = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) == expectedTaskCount &&
        InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0, 0) == requestCount &&
        InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0) == requestCount &&
        InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) == requestCount * 2 + 1 &&
        InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0) == requestCount * 3 + 1;
    if (SUCCEEDED(result) && !workerCompleted) { TraceSyntheticSortStage("async-queue-smoke: first-batch-incomplete"); result = HRESULT_FROM_WIN32(ERROR_TIMEOUT); }

    for (size_t index = 0; SUCCEEDED(result) && index < static_cast<size_t>(requestCount); ++index)
    {
        auto item = items[index];
        SHELLDETAILS completedContents{};
        const auto contentsResult = root->GetDetailsOf(item, 2, &completedContents);
        const auto* expectedContents = index == 0
            ? L"2 files, 2 folders; unreadable/skipped: 1" : L"2 files, 1 folder";
        const bool contentsMatch = SUCCEEDED(contentsResult) && completedContents.str.uType == STRRET_WSTR &&
            completedContents.str.pOleStr && _wcsicmp(completedContents.str.pOleStr, expectedContents) == 0;
        if (completedContents.str.uType == STRRET_WSTR && completedContents.str.pOleStr) CoTaskMemFree(completedContents.str.pOleStr);
        if (FAILED(contentsResult) || !contentsMatch)
        {
            TraceSyntheticSortStage("async-queue-smoke: recursive-count-value-mismatch");
            result = FAILED(contentsResult) ? contentsResult : E_FAIL;
            break;
        }
        if (index == 0) InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 1);
        SHELLDETAILS completedDetails{};
        const auto detailsResult = root->GetDetailsOf(item, 1, &completedDetails);
        if (index == 0) InterlockedExchange(&g_syntheticHumanSizeTextForProbe, 0);
        const auto* expectedSizeText = index == 0 ? L">= 10 B (partial; skipped 1)" : L"10";
        const bool completedValueMatches = SUCCEEDED(detailsResult) && completedDetails.str.uType == STRRET_WSTR &&
            completedDetails.str.pOleStr && _wcsicmp(completedDetails.str.pOleStr, expectedSizeText) == 0;
        if (FAILED(detailsResult) || !completedValueMatches)
        {
            TraceSyntheticSortStage("async-queue-smoke: completed-value-mismatch");
            if (completedDetails.str.uType == STRRET_WSTR && completedDetails.str.pOleStr)
            {
                char actualText[128]{};
                WideCharToMultiByte(CP_UTF8, 0, completedDetails.str.pOleStr, -1, actualText,
                    static_cast<int>(sizeof(actualText)), nullptr, nullptr);
                TraceSyntheticSortStage(actualText);
            }
            if (completedDetails.str.uType == STRRET_WSTR && completedDetails.str.pOleStr) CoTaskMemFree(completedDetails.str.pOleStr);
            result = FAILED(detailsResult) ? detailsResult : E_FAIL;
            break;
        }
        if (completedDetails.str.uType == STRRET_WSTR && completedDetails.str.pOleStr) CoTaskMemFree(completedDetails.str.pOleStr);
    }

    if (SUCCEEDED(result) && !batchByView)
    {
        const auto* first = SyntheticData(items[0]);
        const auto finishedBefore = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0);
        const auto completedBefore = InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0, 0);
        result = root->InvalidateRecursiveSizeSnapshotForChildProbe(first->name);
        ResetEvent(gate);
        SHELLDETAILS pending{};
        if (SUCCEEDED(result)) result = root->GetDetailsOf(items[0], 1, &pending);
        if (pending.str.uType == STRRET_WSTR && pending.str.pOleStr) CoTaskMemFree(pending.str.pOleStr);
        const auto reserveDeadline = GetTickCount64() + 2000;
        while (SUCCEEDED(result) && !root->HasRecursiveSizeScanReservationForChildProbe(first->name) &&
            GetTickCount64() < reserveDeadline) Sleep(1);
        if (SUCCEEDED(result) && root->InvalidateRecursiveSizeScanForChildProbe(first->name) != S_OK)
            result = E_FAIL;
        SetEvent(gate);
        const auto finishDeadline = GetTickCount64() + 10000;
        while (InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) < finishedBefore + 1 &&
            GetTickCount64() < finishDeadline) Sleep(1);
        if (SUCCEEDED(result) && (root->HasRecursiveSizeSnapshotForChildProbe(first->name) ||
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0, 0) != completedBefore ||
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) != finishedBefore + 1))
        {
            TraceSyntheticSortStage("async-queue-smoke: invalidated-per-row-scan-repopulated-cache");
            result = E_FAIL;
        }
        if (SUCCEEDED(result)) TraceSyntheticSortStage("async-queue-smoke: invalidated-per-row-result-rejected");
    }
    HANDLE cancellationGate = nullptr;
    if (SUCCEEDED(result))
    {
        cancellationGate = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (!cancellationGate) result = HRESULT_FROM_WIN32(GetLastError());
    }
    if (SUCCEEDED(result))
    {
        g_syntheticAsyncRecursiveSizeScanGateForProbe = cancellationGate;
        InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0);
        InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0);
        InterlockedExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0);
        InterlockedExchange(&g_syntheticOnDemandSizeScanCount, 0);
        InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
        InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);
        for (size_t index = static_cast<size_t>(requestCount); index < items.size(); ++index)
        {
            SHELLDETAILS pendingDetails{};
            const auto detailsResult = root->GetDetailsOf(items[index], 1, &pendingDetails);
            const bool returnedPending = SUCCEEDED(detailsResult) && pendingDetails.str.uType == STRRET_WSTR &&
                pendingDetails.str.pOleStr && !*pendingDetails.str.pOleStr;
            if (pendingDetails.str.uType == STRRET_WSTR && pendingDetails.str.pOleStr) CoTaskMemFree(pendingDetails.str.pOleStr);
            if (FAILED(detailsResult) || !returnedPending) { result = FAILED(detailsResult) ? detailsResult : E_FAIL; break; }
        }
        root->ReleaseViewHold();
        SetEvent(cancellationGate);
        const auto cancellationDeadline = GetTickCount64() + 10000;
        while (SUCCEEDED(result) &&
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) < expectedTaskCount &&
            GetTickCount64() < cancellationDeadline) Sleep(1);
        const bool queuedWorkCancelled =
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0) == expectedTaskCount &&
            InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0, 0) == 0 &&
            InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0) == 0 &&
            InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0) == 0;
        if (SUCCEEDED(result) && !queuedWorkCancelled) { TraceSyntheticSortStage("async-queue-smoke: released-view-did-not-cancel-queued-work"); result = E_FAIL; }
        CloseHandle(cancellationGate);
        cancellationGate = nullptr;
    }
    if (cancellationGate) CloseHandle(cancellationGate);

    for (auto item : items) CoTaskMemFree(item);
    if (enumerator) enumerator->Release();
    if (root) root->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    g_syntheticAsyncRecursiveSizeScanGateForProbe = nullptr;
    CloseHandle(gate);
    InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, previousDemandSetting);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, previousAsyncSetting);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, previousCustomColumnSetting);
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, previousExtendedMetadataSetting);
    if (SUCCEEDED(result))
        TraceSyntheticSortStage(batchByView
            ? "synthetic-async-on-demand-size: mode=view-batch; callbacks-returned-empty; reads-coalesced; ACL-partial-snapshot-published-and-resolved; peak-active-workers=2; snapshots-published; released-view-cancelled-batch; repeated-reads-cached"
            : "synthetic-async-on-demand-size: mode=per-row; callbacks-returned-empty; 8-reads-queued; ACL-partial-snapshot-published-and-resolved; peak-active-workers=2; 8-snapshots-published; released-view-cancelled-8-queued-reads; repeated-reads-cached");
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticFolderRecursiveSizeIdentityProjectionSmoke(
    PCWSTR backingPath, PCWSTR parsingName, ULONGLONG volumeSerial, ULONGLONG fileIdLow,
    ULONGLONG fileIdHigh, LONGLONG scannedBytes)
{
    if (!backingPath || !*backingPath || !parsingName || !*parsingName || scannedBytes < 0) return E_INVALIDARG;
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result)) return result;

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    PITEMID_CHILD childFolder = nullptr;
    PITEMID_CHILD rootFile = nullptr;
    SyntheticFileIdentity expectedIdentity{};
    SyntheticFileIdentity mismatchedIdentity{};
    expectedIdentity.volumeSerial = volumeSerial;
    CopyMemory(expectedIdentity.fileId, &fileIdLow, sizeof(fileIdLow));
    CopyMemory(expectedIdentity.fileId + sizeof(fileIdLow), &fileIdHigh, sizeof(fileIdHigh));
    expectedIdentity.available = true;
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);

    result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_projection;
    root = new (std::nothrow) SyntheticSortFolder();
    if (!root) { result = E_OUTOFMEMORY; goto cleanup_recursive_size_projection; }
    result = root->Initialize(rootPidl);
    if (FAILED(result)) goto cleanup_recursive_size_projection;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &childFolder, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_projection;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"root.txt"), nullptr, &rootFile, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_projection;
    mismatchedIdentity = expectedIdentity;
    ++mismatchedIdentity.volumeSerial;
    if (root->SetRecursiveSizeSnapshotForProbe(childFolder, parsingName, mismatchedIdentity,
        static_cast<ULONGLONG>(scannedBytes)) != E_INVALIDARG)
    {
        result = E_FAIL;
        goto cleanup_recursive_size_projection;
    }
    result = root->SetRecursiveSizeSnapshotForProbe(childFolder, parsingName, expectedIdentity, static_cast<ULONGLONG>(scannedBytes));
    if (FAILED(result)) goto cleanup_recursive_size_projection;

    {
        SHCOLUMNID mappedKey{};
        result = root->MapColumnToSCID(1, &mappedKey);
        if (FAILED(result) || mappedKey != kSyntheticRecursiveSizeKey)
        {
            result = E_FAIL;
            goto cleanup_recursive_size_projection;
        }

        SHELLDETAILS details{};
        result = root->GetDetailsOf(childFolder, 1, &details);
        const auto expectedText = std::to_wstring(static_cast<ULONGLONG>(scannedBytes));
        const bool textMatches = SUCCEEDED(result) && details.str.uType == STRRET_WSTR &&
            details.str.pOleStr && expectedText == details.str.pOleStr;
        if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
        if (!textMatches) { result = E_FAIL; goto cleanup_recursive_size_projection; }

        VARIANT value{};
        VariantInit(&value);
        result = root->GetDetailsEx(childFolder, &mappedKey, &value);
        const bool propertyMatches = SUCCEEDED(result) && value.vt == VT_UI8 &&
            value.ullVal == static_cast<ULONGLONG>(scannedBytes);
        VariantClear(&value);
        if (!propertyMatches) { result = E_FAIL; goto cleanup_recursive_size_projection; }

        result = root->CompareIDs(1, childFolder, rootFile);
        if (FAILED(result) || static_cast<SHORT>(HRESULT_CODE(result)) <= 0)
        {
            result = E_FAIL;
            goto cleanup_recursive_size_projection;
        }
    }
    result = S_OK;

cleanup_recursive_size_projection:
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0);
    if (rootFile) CoTaskMemFree(rootFile);
    if (childFolder) CoTaskMemFree(childFolder);
    if (root) root->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    return result;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RunSyntheticFolderRecursiveSizeIdentityCacheSmoke(
    PCWSTR backingPath, PCWSTR childParsingName, PCWSTR otherParsingName,
    ULONGLONG childVolumeSerial, ULONGLONG childFileIdLow, ULONGLONG childFileIdHigh,
    ULONGLONG otherVolumeSerial, ULONGLONG otherFileIdLow, ULONGLONG otherFileIdHigh,
    LONGLONG initialBytes, LONGLONG otherRowBytes, LONGLONG updatedBytes)
{
    if (!backingPath || !*backingPath || !childParsingName || !*childParsingName ||
        !otherParsingName || !*otherParsingName || initialBytes < 0 || otherRowBytes < 0 || updatedBytes < 0) return E_INVALIDARG;
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result)) return result;

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    IClassFactory* initialFactory = nullptr;
    IShellFolder2* initialFactoryFolder = nullptr;
    IClassFactory* recreationFactory = nullptr;
    IShellFolder2* factoryRecreatedFolder = nullptr;
    IPersistFolder2* factoryRecreatedPersist = nullptr;
    const auto factoryRecreatedBytes = updatedBytes == 0
        ? 1ULL : static_cast<ULONGLONG>(updatedBytes) - 1;
    PITEMID_CHILD childFolder = nullptr;
    PITEMID_CHILD otherFolder = nullptr;
    PITEMID_CHILD rootFile = nullptr;
    PITEMID_CHILD refreshedChild = nullptr;
    PITEMID_CHILD refreshedOther = nullptr;
    IEnumIDList* refreshedEnumerator = nullptr;
    SyntheticFileIdentity expectedChildIdentity{};
    expectedChildIdentity.volumeSerial = childVolumeSerial;
    CopyMemory(expectedChildIdentity.fileId, &childFileIdLow, sizeof(childFileIdLow));
    CopyMemory(expectedChildIdentity.fileId + sizeof(childFileIdLow), &childFileIdHigh, sizeof(childFileIdHigh));
    expectedChildIdentity.available = true;
    SyntheticFileIdentity expectedOtherIdentity{};
    expectedOtherIdentity.volumeSerial = otherVolumeSerial;
    CopyMemory(expectedOtherIdentity.fileId, &otherFileIdLow, sizeof(otherFileIdLow));
    CopyMemory(expectedOtherIdentity.fileId + sizeof(otherFileIdLow), &otherFileIdHigh, sizeof(otherFileIdHigh));
    expectedOtherIdentity.available = true;
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);

    result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = DllGetClassObject(g_syntheticFolderClassId, IID_IClassFactory,
        reinterpret_cast<void**>(&initialFactory));
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = initialFactory->CreateInstance(nullptr, IID_IShellFolder2,
        reinterpret_cast<void**>(&initialFactoryFolder));
    initialFactory->Release();
    initialFactory = nullptr;
    if (FAILED(result)) goto cleanup_recursive_size_update;
    root = static_cast<SyntheticSortFolder*>(initialFactoryFolder);
    initialFactoryFolder = nullptr;
    result = root->Initialize(rootPidl);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &childFolder, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"other-child"), nullptr, &otherFolder, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"root.txt"), nullptr, &rootFile, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->SetRecursiveSizeSnapshotForProbe(childFolder, childParsingName, expectedChildIdentity, static_cast<ULONGLONG>(initialBytes));
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->SetRecursiveSizeSnapshotForProbe(otherFolder, otherParsingName, expectedOtherIdentity, static_cast<ULONGLONG>(otherRowBytes));
    if (FAILED(result)) goto cleanup_recursive_size_update;

    {
        const auto rowMatchesSnapshot = [&](IShellFolder2* folder, PCUITEMID_CHILD row, ULONGLONG expected, bool folderAfterFile)
        {
            SHCOLUMNID mappedKey{};
            if (FAILED(folder->MapColumnToSCID(1, &mappedKey)) || mappedKey != kSyntheticRecursiveSizeKey)
            {
                TraceSyntheticSortStage("recursive-size: snapshot-check-map-failed");
                return false;
            }

            SHELLDETAILS details{};
            const auto detailsResult = folder->GetDetailsOf(row, 1, &details);
            const auto expectedText = std::to_wstring(expected);
            const bool textMatches = SUCCEEDED(detailsResult) && details.str.uType == STRRET_WSTR &&
                details.str.pOleStr && expectedText == details.str.pOleStr;
            if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
            if (!textMatches)
            {
                TraceSyntheticSortStage("recursive-size: snapshot-check-text-failed");
                return false;
            }

            VARIANT value{};
            VariantInit(&value);
            const auto propertyResult = folder->GetDetailsEx(row, &mappedKey, &value);
            const bool propertyMatches = SUCCEEDED(propertyResult) && value.vt == VT_UI8 && value.ullVal == expected;
            VariantClear(&value);
            if (!propertyMatches)
            {
                TraceSyntheticSortStage("recursive-size: snapshot-check-property-failed");
                return false;
            }

            const auto compareResult = folderAfterFile
                ? folder->CompareIDs(1, row, rootFile)
                : folder->CompareIDs(1, rootFile, row);
            const auto matches = SUCCEEDED(compareResult) && static_cast<SHORT>(HRESULT_CODE(compareResult)) > 0;
            if (!matches) TraceSyntheticSortStage("recursive-size: snapshot-check-order-failed");
            return matches;
        };

        // Sibling rows own independent snapshots; updating one must not replace the other's value.
        if (!rowMatchesSnapshot(root, childFolder, static_cast<ULONGLONG>(initialBytes), true) ||
            !rowMatchesSnapshot(root, otherFolder, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }
        result = root->SetRecursiveSizeSnapshotForProbe(childFolder, childParsingName, expectedChildIdentity, static_cast<ULONGLONG>(updatedBytes));
        if (FAILED(result)) goto cleanup_recursive_size_update;
        if (!rowMatchesSnapshot(root, childFolder, static_cast<ULONGLONG>(updatedBytes), false) ||
            !rowMatchesSnapshot(root, otherFolder, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }

        result = root->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, &refreshedEnumerator);
        if (FAILED(result) || !refreshedEnumerator)
        {
            if (SUCCEEDED(result)) result = E_UNEXPECTED;
            goto cleanup_recursive_size_update;
        }
        for (;;)
        {
            PITEMID_CHILD item = nullptr;
            ULONG fetched = 0;
            result = refreshedEnumerator->Next(1, &item, &fetched);
            if (result == S_FALSE) { result = S_OK; break; }
            if (FAILED(result) || fetched != 1 || !item)
            {
                if (item) CoTaskMemFree(item);
                if (SUCCEEDED(result)) result = E_UNEXPECTED;
                goto cleanup_recursive_size_update;
            }

            const auto* child = SyntheticData(item);
            if (child && _wcsicmp(child->name, L"child") == 0) refreshedChild = item;
            else if (child && _wcsicmp(child->name, L"other-child") == 0) refreshedOther = item;
            else CoTaskMemFree(item);
            if (refreshedChild && refreshedOther) break;
        }
        if (!refreshedChild || !refreshedOther ||
            !rowMatchesSnapshot(root, refreshedChild, static_cast<ULONGLONG>(updatedBytes), false) ||
            !rowMatchesSnapshot(root, refreshedOther, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }

        // Use a value that differs from the backing directory's actual recursive total so
        // provider recreation can only pass by reusing the published snapshot.
        result = root->SetRecursiveSizeSnapshotForProbe(childFolder, childParsingName,
            expectedChildIdentity, factoryRecreatedBytes);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        refreshedEnumerator->Release();
        refreshedEnumerator = nullptr;
        CoTaskMemFree(refreshedOther);
        refreshedOther = nullptr;
        CoTaskMemFree(refreshedChild);
        refreshedChild = nullptr;
        root->Release();
        root = nullptr;
        CoTaskMemFree(rootFile);
        rootFile = nullptr;
        CoTaskMemFree(otherFolder);
        otherFolder = nullptr;
        CoTaskMemFree(childFolder);
        childFolder = nullptr;

        // Recreating the provider must not lose scan snapshots keyed by absolute Shell identity.
        result = DllGetClassObject(g_syntheticFolderClassId, IID_IClassFactory,
            reinterpret_cast<void**>(&recreationFactory));
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = recreationFactory->CreateInstance(nullptr, IID_IShellFolder2,
            reinterpret_cast<void**>(&factoryRecreatedFolder));
        recreationFactory->Release();
        recreationFactory = nullptr;
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = factoryRecreatedFolder->QueryInterface(IID_PPV_ARGS(&factoryRecreatedPersist));
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = factoryRecreatedPersist->Initialize(rootPidl);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = factoryRecreatedFolder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &childFolder, nullptr);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = factoryRecreatedFolder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"other-child"), nullptr, &otherFolder, nullptr);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        result = factoryRecreatedFolder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"root.txt"), nullptr, &rootFile, nullptr);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        if (!rowMatchesSnapshot(factoryRecreatedFolder, childFolder, factoryRecreatedBytes, false) ||
            !rowMatchesSnapshot(factoryRecreatedFolder, otherFolder, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            TraceSyntheticSortStage("recursive-size: factory-recreated-snapshot-failed");
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }
        TraceSyntheticSortStage("recursive-size: factory-recreated-snapshot-passed");

        // A replacement directory at the same path must not inherit the retired folder's scan snapshot.
        CoTaskMemFree(childFolder);
        childFolder = nullptr;
        {
            std::wstring originalChildPath = backingPath;
            std::wstring retiredChildPath = backingPath;
            try
            {
                originalChildPath += L"\\child";
                retiredChildPath += L"\\child.retired";
            }
            catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; goto cleanup_recursive_size_update; }
            if (!MoveFileW(originalChildPath.c_str(), retiredChildPath.c_str()))
            {
                result = HRESULT_FROM_WIN32(GetLastError());
                goto cleanup_recursive_size_update;
            }
            if (!CreateDirectoryW(originalChildPath.c_str(), nullptr))
            {
                result = HRESULT_FROM_WIN32(GetLastError());
                goto cleanup_recursive_size_update;
            }
            std::wstring replacementFilePath = originalChildPath + L"\\replacement.bin";
            const auto replacementFile = CreateFileW(replacementFilePath.c_str(), GENERIC_WRITE, 0, nullptr,
                CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
            if (replacementFile == INVALID_HANDLE_VALUE)
            {
                result = HRESULT_FROM_WIN32(GetLastError());
                goto cleanup_recursive_size_update;
            }
            constexpr BYTE replacementContents[] = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 };
            DWORD bytesWritten = 0;
            const bool wroteReplacement = WriteFile(replacementFile, replacementContents,
                static_cast<DWORD>(sizeof(replacementContents)), &bytesWritten, nullptr) != FALSE &&
                bytesWritten == sizeof(replacementContents);
            CloseHandle(replacementFile);
            if (!wroteReplacement)
            {
                result = HRESULT_FROM_WIN32(GetLastError());
                goto cleanup_recursive_size_update;
            }
        }
        result = factoryRecreatedFolder->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &childFolder, nullptr);
        if (FAILED(result)) goto cleanup_recursive_size_update;
        {
            SHELLDETAILS details{};
            result = factoryRecreatedFolder->GetDetailsOf(childFolder, 1, &details);
            const bool textMatches = SUCCEEDED(result) && details.str.uType == STRRET_WSTR &&
                details.str.pOleStr && wcscmp(details.str.pOleStr, L"17") == 0;
            if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
            if (!textMatches) { TraceSyntheticSortStage("recursive-size: replacement-cache-invalidation-failed"); result = E_FAIL; goto cleanup_recursive_size_update; }
            if (static_cast<SyntheticSortFolder*>(factoryRecreatedFolder)->RecursiveSizeSnapshotCountForProbe() != 1)
            {
                TraceSyntheticSortStage("recursive-size: stale-snapshot-retained");
                result = E_FAIL;
                goto cleanup_recursive_size_update;
            }
        }
        TraceSyntheticSortStage("recursive-size: replacement-cache-invalidation-and-pruning-passed");
    }
    result = S_OK;

cleanup_recursive_size_update:
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0);
    if (refreshedEnumerator) refreshedEnumerator->Release();
    if (refreshedOther) CoTaskMemFree(refreshedOther);
    if (refreshedChild) CoTaskMemFree(refreshedChild);
    if (rootFile) CoTaskMemFree(rootFile);
    if (otherFolder) CoTaskMemFree(otherFolder);
    if (childFolder) CoTaskMemFree(childFolder);
    if (root) root->Release();
    if (initialFactoryFolder) initialFactoryFolder->Release();
    if (initialFactory) initialFactory->Release();
    if (recreationFactory) recreationFactory->Release();
    if (factoryRecreatedPersist) factoryRecreatedPersist->Release();
    if (factoryRecreatedFolder) factoryRecreatedFolder->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    return result;
}

extern "C" ED_PROBE_EXPORT void WINAPI ResetSyntheticSortCounters()
{
    InterlockedExchange(&g_syntheticCompareCount, 0);
    for (auto& count : g_syntheticCompareByColumn) InterlockedExchange(&count, 0);
    InterlockedExchange(&g_syntheticSizeCompareCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsDuringCompareCount, 0);
    InterlockedExchange(&g_syntheticRecursiveSizeCacheMissesDuringCompareCount, 0);
    InterlockedExchange(&g_syntheticItemDetailsOfCount, 0);
    InterlockedExchange(&g_syntheticSizeDetailsExCount, 0);
    InterlockedExchange(&g_syntheticSizeDetailsExFolderCount, 0);
    InterlockedExchange(&g_syntheticSizeDetailsExFileCount, 0);
    InterlockedExchange(&g_syntheticSizeDetailsOfItemCount, 0);
    InterlockedExchange(&g_syntheticCustomSizeDetailsExCount, 0);
    InterlockedExchange(&g_syntheticCustomSizeDetailsExFolderCount, 0);
    InterlockedExchange(&g_syntheticCustomSizeDetailsExFileCount, 0);
    InterlockedExchange(&g_syntheticPropertyStoreFactoryRequests, 0);
    InterlockedExchange(&g_syntheticPropertyStoreRequests, 0);
    InterlockedExchange(&g_syntheticOtherUiObjectRequests, 0);
    InterlockedExchange(&g_syntheticGetAttributesOfCount, 0);
    InterlockedExchange(&g_syntheticFilesystemAttributeCount, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeNotificationInvalidatedReservationsForProbe, 0);
    WCHAR notificationTraceSetting[2]{};
    InterlockedExchange(&g_syntheticTraceNotificationPidlsForProbe,
        GetEnvironmentVariableW(L"EXPLORER_DIRS_NATIVEPROBE_TRACE_NOTIFICATION_PIDLS", notificationTraceSetting,
            ARRAYSIZE(notificationTraceSetting)) == 1 && notificationTraceSetting[0] == L'1' ? 1 : 0);
    // Counter resets occur between Shell operations; keep the caller-selected provider behavior stable.
    InterlockedExchange(&g_syntheticEnumObjectsCount, 0);
    InterlockedExchange(&g_syntheticEnumeratedItems, 0);
    InterlockedExchange(&g_syntheticClassFactoryCreateCount, 0);
    InterlockedExchange(&g_syntheticMapColumnToScidCount, 0);
    for (size_t column = 0; column < ARRAYSIZE(g_syntheticMapColumnToScidByColumn); ++column)
        InterlockedExchange(&g_syntheticMapColumnToScidByColumn[column], 0);
    InterlockedExchange(&g_syntheticMapColumnTraceCount, 0);
    for (auto& entry : g_syntheticMapColumnTrace)
        InterlockedExchange(&entry.sequence, 0);
    InterlockedExchange(&g_syntheticColumnMetadataTraceCount, 0);
    for (auto& entry : g_syntheticColumnMetadataTrace)
        InterlockedExchange(&entry.sequence, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticMapColumnToScidCount()
{
    return InterlockedCompareExchange(&g_syntheticMapColumnToScidCount, 0, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticMapColumnToScidColumnCount(UINT column)
{
    if (column >= ARRAYSIZE(g_syntheticMapColumnToScidByColumn)) return -1;
    return InterlockedCompareExchange(&g_syntheticMapColumnToScidByColumn[column], 0, 0);
}

extern "C" ED_PROBE_EXPORT void WINAPI ResetSyntheticMapColumnTrace()
{
    InterlockedExchange(&g_syntheticMapColumnTraceCount, 0);
    for (auto& entry : g_syntheticMapColumnTrace)
        InterlockedExchange(&entry.sequence, 0);
}

extern "C" ED_PROBE_EXPORT void WINAPI ResetSyntheticColumnDiagnostics()
{
    InterlockedExchange(&g_syntheticMapColumnToScidCount, 0);
    for (size_t column = 0; column < ARRAYSIZE(g_syntheticMapColumnToScidByColumn); ++column)
        InterlockedExchange(&g_syntheticMapColumnToScidByColumn[column], 0);
    ResetSyntheticMapColumnTrace();
    InterlockedExchange(&g_syntheticColumnMetadataTraceCount, 0);
    for (auto& entry : g_syntheticColumnMetadataTrace)
        InterlockedExchange(&entry.sequence, 0);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticMapColumnTraceCount()
{
    const auto count = InterlockedCompareExchange(&g_syntheticMapColumnTraceCount, 0, 0);
    return count < kSyntheticMapColumnTraceCapacity ? count : kSyntheticMapColumnTraceCapacity;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI ReadSyntheticMapColumnTrace(
    UINT index, UINT* column, SHCOLUMNID* key, HRESULT* result)
{
    if (!column || !key || !result) return E_POINTER;
    if (index >= kSyntheticMapColumnTraceCapacity ||
        InterlockedCompareExchange(&g_syntheticMapColumnTrace[index].sequence, 0, 0) != static_cast<LONG>(index + 1))
        return E_INVALIDARG;
    const auto& entry = g_syntheticMapColumnTrace[index];
    *column = entry.column;
    *key = entry.key;
    *result = entry.result;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticColumnMetadataTraceCount()
{
    const auto count = InterlockedCompareExchange(&g_syntheticColumnMetadataTraceCount, 0, 0);
    return count < kSyntheticColumnMetadataTraceCapacity ? count : kSyntheticColumnMetadataTraceCapacity;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI ReadSyntheticColumnMetadataTrace(
    UINT index, UINT* kind, UINT* column, SHCOLSTATEF* state, HRESULT* result, WCHAR* text, UINT textCapacity)
{
    if (!kind || !column || !state || !result || !text || textCapacity == 0) return E_POINTER;
    if (index >= kSyntheticColumnMetadataTraceCapacity ||
        InterlockedCompareExchange(&g_syntheticColumnMetadataTrace[index].sequence, 0, 0) != static_cast<LONG>(index + 1))
        return E_INVALIDARG;
    const auto& entry = g_syntheticColumnMetadataTrace[index];
    *kind = entry.kind;
    *column = entry.column;
    *state = entry.state;
    *result = entry.result;
    return StringCchCopyW(text, textCapacity, entry.text);
}

extern "C" ED_PROBE_EXPORT void WINAPI ReleaseSyntheticSortViewHold()
{
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedExchangePointer(&g_syntheticSortViewHolds[index], nullptr));
        if (!folder) continue;
        folder->CancelPendingRefresh();
        folder->CancelPendingBackingDirectoryUpdate();
        folder->ReleaseViewHold();
        folder->Release();
    }
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI RetireSyntheticSortViewHoldForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(
            &g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view) ||
            InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, folder) != folder)
            continue;
        folder->CancelPendingRefresh();
        folder->CancelPendingBackingDirectoryUpdate();
        folder->ReleaseViewHold();
        folder->Release();
        return S_OK;
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSortViewHoldCountForProbe()
{
    LONG count = 0;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
        if (InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr)) ++count;
    return count;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI CancelSyntheticSortRefreshForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->CancelPendingRefreshForProbe();
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI CancelSyntheticBackingDirectoryUpdateForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->CancelPendingBackingDirectoryUpdateForProbe();
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT BOOL WINAPI ShouldTranslateSyntheticChildNotificationForProbe(UINT event)
{
    return ShouldTranslateSyntheticChildNotification(static_cast<LONG>(event)) ? TRUE : FALSE;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI ScheduleSyntheticBackingDirectoryUpdateForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->ScheduleBackingDirectoryUpdateForProbe();
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI ScheduleSyntheticSortRefreshForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->ScheduleRefreshForProbe();
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI NotifySyntheticSortRefreshForProbe(IShellView* view)
{
    if (!view) return E_POINTER;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->NotifyRefreshForProbe();
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI PublishSyntheticRecursiveSizeSnapshotForViewProbe(
    IShellView* view, PCWSTR childName, ULONGLONG bytes, ULONGLONG files, ULONGLONG folders,
    ULONGLONG unreadable)
{
    if (!view || !childName || !*childName) return E_INVALIDARG;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->PublishRecursiveSizeSnapshotForChildProbe(childName, bytes, files, folders, unreadable);
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI GetSyntheticRecursiveSizeSnapshotForViewProbe(
    IShellView* view, PCWSTR childName, ULONGLONG* bytes, ULONGLONG* files, ULONGLONG* folders,
    ULONGLONG* unreadable)
{
    if (!view || !childName || !*childName) return E_INVALIDARG;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->ReadRecursiveSizeSnapshotForChildProbe(childName, bytes, files, folders, unreadable);
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT BOOL WINAPI HasSyntheticRecursiveSizeSnapshotForViewProbe(
    IShellView* view, PCWSTR childName)
{
    if (!view || !childName || !*childName) return FALSE;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->HasRecursiveSizeSnapshotForChildProbe(childName);
    }
    return FALSE;
}

extern "C" ED_PROBE_EXPORT BOOL WINAPI HasSyntheticRecursiveSizeScanReservationForChildProbe(
    IShellView* view, PCWSTR childName)
{
    if (!view || !childName || !*childName) return FALSE;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->HasRecursiveSizeScanReservationForChildProbe(childName);
    }
    return FALSE;
}

extern "C" ED_PROBE_EXPORT BOOL WINAPI HasSyntheticAsyncRecursiveSizeScanPendingForViewProbe(
    IShellView* view, PCWSTR childName)
{
    if (!view || !childName || !*childName) return FALSE;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->HasAsyncRecursiveSizeScanPendingForChildProbe(childName);
    }
    return FALSE;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI InvalidateSyntheticRecursiveSizeScanForChildProbe(
    IShellView* view, PCWSTR childName)
{
    if (!view || !childName || !*childName) return E_INVALIDARG;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->InvalidateRecursiveSizeScanForChildProbe(childName);
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI InvalidateSyntheticRecursiveSizeSnapshotForChildProbe(
    IShellView* view, PCWSTR childName)
{
    if (!view || !childName || !*childName) return E_INVALIDARG;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder || !folder->OwnsShellView(view)) continue;
        return folder->InvalidateRecursiveSizeSnapshotForChildProbe(childName);
    }
    return E_INVALIDARG;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI ScheduleSyntheticSortRefreshesForProbe()
{
    LONG scheduled = 0;
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], nullptr, nullptr));
        if (!folder) continue;
        const auto result = folder->ScheduleRefreshForProbe();
        if (FAILED(result)) return result;
        if (result == S_OK) ++scheduled;
    }
    return scheduled;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticDeferredRefreshCount()
{ return InterlockedCompareExchange(&g_syntheticDeferredRefreshCount, 0, 0); }

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticChildNotificationTranslationForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticTranslateChildNotificationsForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticChildNotificationTranslationQueuedCount()
{ return InterlockedCompareExchange(&g_syntheticChildNotificationTranslationQueued, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticChildNotificationTranslationSentCount()
{ return InterlockedCompareExchange(&g_syntheticChildNotificationTranslationSent, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAttributeNotificationTranslationCount()
{ return InterlockedCompareExchange(&g_syntheticAttributeNotificationTranslationCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSortCompareCount()
{ return InterlockedCompareExchange(&g_syntheticCompareCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSortCompareCountForColumn(UINT column)
{ return column < ARRAYSIZE(g_syntheticCompareByColumn) ? InterlockedCompareExchange(&g_syntheticCompareByColumn[column], 0, 0) : -1; }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSizeCompareCount()
{ return InterlockedCompareExchange(&g_syntheticSizeCompareCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticDirectoryMetricsDuringCompareCount()
{ return InterlockedCompareExchange(&g_syntheticDirectoryMetricsDuringCompareCount, 0, 0); }

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticRecursiveSizeScanOnDemandForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticRecursiveSizeScanOnDemandForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeScanForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeItemNotificationForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeItemNotificationForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeBatchByViewForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeBatchByViewForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeScanDelayForProbe(LONG delayMilliseconds)
{
    if (delayMilliseconds < 0 || delayMilliseconds > 1000) return E_INVALIDARG;
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanDelayMsForProbe, delayMilliseconds);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeEntryDelayForProbe(LONG delayMilliseconds)
{
    if (delayMilliseconds < 0 || delayMilliseconds > 1000) return E_INVALIDARG;
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeEntryDelayMsForProbe, delayMilliseconds);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticRecursiveSizeCacheCapacityForProbe(LONG capacity)
{
    // Probe overrides share the product store's hard bound so capacity tests stay representative.
    if (capacity < 1 || capacity > 8192) return E_INVALIDARG;
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (g_sharedRecursiveSizeStore)
    {
        ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
        return E_UNEXPECTED;
    }
    InterlockedExchange(&g_syntheticRecursiveSizeCacheCapacityForProbe, capacity);
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticRecursiveSizeCacheSnapshotCountForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto count = store->Count();
    store->Release();
    return static_cast<LONG>(count);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticRecursiveSizeCachePayloadBytesForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto bytes = store->PayloadBytes();
    store->Release();
    return static_cast<LONG>(bytes);
}

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticRecursiveSizeCacheLookupHitsForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto count = store->LookupHits();
    store->Release();
    return static_cast<LONG64>(count);
}

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticRecursiveSizeCacheLookupMissesForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto count = store->LookupMisses();
    store->Release();
    return static_cast<LONG64>(count);
}

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticRecursiveSizeCacheEvictionsForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto count = store->Evictions();
    store->Release();
    return static_cast<LONG64>(count);
}

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticRecursiveSizeCacheEvictionSearchStepsForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto count = store->EvictionSearchSteps();
    store->Release();
    return static_cast<LONG64>(count);
}

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticRecursiveSizeCacheEvictionSearchMicrosecondsForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto elapsed = store->EvictionSearchMicroseconds();
    store->Release();
    return static_cast<LONG64>(elapsed);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticRecursiveSizeCacheIndexedHeapAuxiliaryBytesEstimateForProbe()
{
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (!store) return 0;
    const auto bytes = store->IndexedHeapAuxiliaryBytesEstimate();
    store->Release();
    return static_cast<LONG>(bytes);
}

extern "C" ED_PROBE_EXPORT void WINAPI ResetSyntheticOnDemandSizeMetricsForProbe()
{
    InterlockedExchange(&g_syntheticDirectoryMetricsCallCount, 0);
    InterlockedExchange(&g_syntheticDirectoryMetricsEntryCount, 0);
    InterlockedExchange(&g_syntheticOnDemandSizeScanCount, 0);
    InterlockedExchange64(&g_syntheticOnDemandSizeScanMicroseconds, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeNotificationsSentForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeItemNotificationsSentForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeDirectoryNotificationsSentForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeItemNotificationForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeNotificationInvalidatedReservationsForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeScanStartedForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeQueueRejectedScanCountForProbe, 0);
    InterlockedExchange(&g_syntheticAsyncRecursiveSizeBatchRequestedEntriesForProbe, 0);
    AcquireSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    auto* store = g_sharedRecursiveSizeStore;
    if (store) store->AddRef();
    ReleaseSRWLockExclusive(&g_sharedRecursiveSizeStoreLock);
    if (store)
    {
        store->ResetLookupMetrics();
        store->Release();
    }
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticDirectoryMetricsCallCount()
{ return InterlockedCompareExchange(&g_syntheticDirectoryMetricsCallCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticDirectoryMetricsEntryCount()
{ return InterlockedCompareExchange(&g_syntheticDirectoryMetricsEntryCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticOnDemandSizeScanCount()
{ return InterlockedCompareExchange(&g_syntheticOnDemandSizeScanCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG64 WINAPI GetSyntheticOnDemandSizeScanMicroseconds()
{ return InterlockedCompareExchange64(&g_syntheticOnDemandSizeScanMicroseconds, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizePeakWorkerCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizePeakWorkersForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeNotificationCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeNotificationsSentForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeItemNotificationCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeItemNotificationsSentForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeDirectoryNotificationCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeDirectoryNotificationsSentForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeNotificationInvalidatedReservationCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeNotificationInvalidatedReservationsForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeQueueRejectedScanCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeQueueRejectedScanCountForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeBatchRequestedEntriesForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeBatchRequestedEntriesForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeScanFinishedCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanFinishedForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeScanStartedCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanStartedForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticAsyncRecursiveSizeScanCompletedCountForProbe()
{ return InterlockedCompareExchange(&g_syntheticAsyncRecursiveSizeScanCompletedForProbe, 0, 0); }

extern "C" ED_PROBE_EXPORT HRESULT WINAPI BeginSyntheticAsyncRecursiveSizeQueueGateForProbe()
{
    if (g_syntheticAsyncRecursiveSizeScanGateForProbe) return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
    const auto gate = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!gate) return HRESULT_FROM_WIN32(GetLastError());
    g_syntheticAsyncRecursiveSizeScanGateForProbe = gate;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT void WINAPI ReleaseSyntheticAsyncRecursiveSizeQueueGateForProbe()
{
    const auto gate = g_syntheticAsyncRecursiveSizeScanGateForProbe;
    if (gate) SetEvent(gate);
}

extern "C" ED_PROBE_EXPORT void WINAPI ClearSyntheticAsyncRecursiveSizeQueueGateForProbe()
{
    const auto gate = g_syntheticAsyncRecursiveSizeScanGateForProbe;
    g_syntheticAsyncRecursiveSizeScanGateForProbe = nullptr;
    if (gate) CloseHandle(gate);
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticAsyncRecursiveSizeCommitGatePathForProbe(PCWSTR path)
{
    if (!path || !*path) return E_INVALIDARG;
    try
    {
        std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
        if (g_syntheticAsyncRecursiveSizeCommitGateForProbe) return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
        g_syntheticAsyncRecursiveSizeCommitGatePathForProbe.assign(path);
    }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI BeginSyntheticAsyncRecursiveSizeCommitGateForProbe()
{
    std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
    if (g_syntheticAsyncRecursiveSizeCommitGateForProbe || g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe ||
        g_syntheticAsyncRecursiveSizeCommitGatePathForProbe.empty())
        return HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED);
    const auto gate = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!gate) return HRESULT_FROM_WIN32(GetLastError());
    const auto reached = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!reached)
    {
        const auto error = GetLastError();
        CloseHandle(gate);
        return HRESULT_FROM_WIN32(error);
    }
    g_syntheticAsyncRecursiveSizeCommitGateForProbe = gate;
    g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe = reached;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT BOOL WINAPI HasSyntheticAsyncRecursiveSizeCommitGateReachedForProbe()
{
    HANDLE reached = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
        reached = g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe;
    }
    return reached && WaitForSingleObject(reached, 0) == WAIT_OBJECT_0;
}

extern "C" ED_PROBE_EXPORT void WINAPI ReleaseSyntheticAsyncRecursiveSizeCommitGateForProbe()
{
    HANDLE gate = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
        gate = g_syntheticAsyncRecursiveSizeCommitGateForProbe;
    }
    if (gate) SetEvent(gate);
}

extern "C" ED_PROBE_EXPORT void WINAPI ClearSyntheticAsyncRecursiveSizeCommitGateForProbe()
{
    HANDLE gate = nullptr, reached = nullptr;
    {
        std::lock_guard<std::mutex> lock(g_syntheticAsyncRecursiveSizeCommitGateMutexForProbe);
        gate = g_syntheticAsyncRecursiveSizeCommitGateForProbe;
        reached = g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe;
        g_syntheticAsyncRecursiveSizeCommitGateForProbe = nullptr;
        g_syntheticAsyncRecursiveSizeCommitGateReachedForProbe = nullptr;
        g_syntheticAsyncRecursiveSizeCommitGatePathForProbe.clear();
    }
    if (gate) CloseHandle(gate);
    if (reached) CloseHandle(reached);
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticRecursiveSizeCacheMissesDuringCompareCount()
{ return InterlockedCompareExchange(&g_syntheticRecursiveSizeCacheMissesDuringCompareCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSizeDetailsExCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSizeDetailsExFolderCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExFolderCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSizeDetailsExFileCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExFileCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticSizeDetailsOfItemCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsOfItemCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticItemDetailsOfCount()
{ return InterlockedCompareExchange(&g_syntheticItemDetailsOfCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticPropertyStoreFactoryRequestCount()
{ return InterlockedCompareExchange(&g_syntheticPropertyStoreFactoryRequests, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticPropertyStoreRequestCount()
{ return InterlockedCompareExchange(&g_syntheticPropertyStoreRequests, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticGetAttributesOfCount()
{ return InterlockedCompareExchange(&g_syntheticGetAttributesOfCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticFilesystemAttributeCount()
{ return InterlockedCompareExchange(&g_syntheticFilesystemAttributeCount, 0, 0); }

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticMaskFilesystemAttributesForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticMaskFilesystemAttributes, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticOwnAttributesForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticOwnAttributesForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticCustomSizeColumnForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticIgnoreRecursiveFolderSizeSortForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticIgnoreRecursiveFolderSizeSortForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticHumanSizeTextForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticHumanSizeTextForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticSizeDisplayOptionsForProbe(LONG unit, LONG decimalPlaces, BOOL thousandsSeparator)
{
    if (unit < 0 || unit > 7 || decimalPlaces < 0 || decimalPlaces > 3) return E_INVALIDARG;
    InterlockedExchange(&g_syntheticSizeDisplayUnitForProbe, unit);
    InterlockedExchange(&g_syntheticSizeDisplayDecimalsForProbe, decimalPlaces);
    InterlockedExchange(&g_syntheticSizeDisplayThousandsForProbe, thousandsSeparator ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticSlowCustomSizeColumnForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticSlowCustomSizeColumnForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticRelativePercentColumnTypeForProbe(BOOL enabled, SHCOLSTATEF type)
{
    if (type != SHCOLSTATE_DEFAULT && type != SHCOLSTATE_TYPE_INT && type != SHCOLSTATE_TYPE_STR)
        return E_INVALIDARG;
    InterlockedExchange(&g_syntheticRelativePercentColumnTypeForProbe, static_cast<LONG>(type));
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticSizeMetricsObservationForProbe(BOOL enabled)
{
    g_syntheticSizeMetricsObservation = enabled ? TRUE : FALSE;
    return S_OK;
}

extern "C" ED_PROBE_EXPORT HRESULT WINAPI SetSyntheticChildSizeOverrideForProbe(LONGLONG size)
{
    if (size < -1) return E_INVALIDARG;
    InterlockedExchange64(&g_syntheticChildSizeOverride, size);
    return S_OK;
}

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticCustomSizeDetailsExCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticCustomSizeDetailsExFolderCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExFolderCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticCustomSizeDetailsExFileCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExFileCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticEnumObjectsCount()
{ return InterlockedCompareExchange(&g_syntheticEnumObjectsCount, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticEnumeratedItemCount()
{ return InterlockedCompareExchange(&g_syntheticEnumeratedItems, 0, 0); }

extern "C" ED_PROBE_EXPORT LONG WINAPI GetSyntheticClassFactoryCreateCount()
{ return InterlockedCompareExchange(&g_syntheticClassFactoryCreateCount, 0, 0); }
