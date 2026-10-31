#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <shellapi.h>
#include <commctrl.h>
#include <propsys.h>
#include <propkey.h>
#include <strsafe.h>
#include <algorithm>
#include <new>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>
#include <cstdio>

namespace
{
    void TraceSyntheticSortStage(const char* stage)
    {
        const auto output = GetStdHandle(STD_OUTPUT_HANDLE);
        if (!output || output == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(output, stage, static_cast<DWORD>(strlen(stage)), &written, nullptr);
        WriteFile(output, "\r\n", 2, &written, nullptr);
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
    constexpr size_t kSyntheticSortViewHoldCount = 8;
    PVOID volatile g_syntheticSortViewHolds[kSyntheticSortViewHoldCount]{};
    CLSID g_syntheticFolderClassId =
    { 0xA17D2A61, 0x4C0B, 0x4B22, { 0x8E, 0x36, 0x35, 0xD9, 0x2C, 0xBC, 0x11, 0x70 } };
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
            InterlockedIncrement(&g_propertyStoreValueRequests);
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
            InterlockedIncrement(&g_propertyStoreFactoryCreateRequests);
            return CreateStore(_byteCount, _size, iid, value);
        }
        HRESULT STDMETHODCALLTYPE GetPropertyStoreForKeys(const PROPERTYKEY*, UINT, GETPROPERTYSTOREFLAGS, REFIID iid, void** value) override
        {
            InterlockedIncrement(&g_propertyStoreFactoryCreateRequests);
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
                InterlockedIncrement(&g_sortCallbackColumnClickRequests);
                InterlockedExchange(&g_sortCallbackLastColumn, static_cast<LONG>(column));
                return E_NOTIMPL;
            }
            if (message == SFVM_GETSORTDEFAULTS)
            {
                InterlockedIncrement(&g_sortCallbackGetDefaultsRequests);
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
            InterlockedIncrement(&g_legacyDetailsCallbackRequests);
            InterlockedExchange(&g_legacyDetailsCallbackLastColumn, static_cast<LONG>(column));
            if (details->pidl) InterlockedIncrement(&g_legacyDetailsCallbackItemRequests);
            else InterlockedIncrement(&g_legacyDetailsCallbackHeaderRequests);
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

    class WrappedEnumIdList final : public IEnumIDList
    {
    public:
        explicit WrappedEnumIdList(IEnumIDList* inner) : _inner(inner) { if (_inner) _inner->AddRef(); }
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
            std::vector<PITEMID_CHILD> raw;
            try { raw.assign(count, nullptr); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            ULONG rawFetched = 0;
            auto result = _inner->Next(count, raw.data(), &rawFetched);
            if (FAILED(result))
            {
                for (auto* item : raw) if (item) CoTaskMemFree(item);
                return result;
            }
            for (ULONG index = 0; index < rawFetched; ++index)
            {
                const auto wrapResult = WrapChildPidl(raw[index], &items[index]);
                CoTaskMemFree(raw[index]);
                raw[index] = nullptr;
                if (FAILED(wrapResult))
                {
                    for (ULONG rest = index + 1; rest < rawFetched; ++rest) CoTaskMemFree(raw[rest]);
                    for (ULONG made = 0; made < index; ++made) CoTaskMemFree(items[made]);
                    return wrapResult;
                }
            }
            if (fetched) *fetched = rawFetched;
            return result;
        }
        HRESULT STDMETHODCALLTYPE Skip(ULONG count) override { return _inner->Skip(count); }
        HRESULT STDMETHODCALLTYPE Reset() override { return _inner->Reset(); }
        HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            IEnumIDList* clone = nullptr;
            auto result = _inner->Clone(&clone);
            if (FAILED(result)) return result;
            auto* wrapped = new (std::nothrow) WrappedEnumIdList(clone);
            clone->Release();
            if (!wrapped) return E_OUTOFMEMORY;
            *value = wrapped;
            return S_OK;
        }
    private:
        ~WrappedEnumIdList() { if (_inner) _inner->Release(); }
        volatile LONG _refs = 1;
        IEnumIDList* _inner = nullptr;
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
            if (iid == IID_IPropertyStore) InterlockedIncrement(&g_propertyStoreQueryInterfaceRequests);
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
            InterlockedIncrement(&g_persistClassIdRequests);
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
            InterlockedIncrement(&g_persistRootRequests);
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
            auto* wrapped = new (std::nothrow) WrappedEnumIdList(raw);
            raw->Release();
            if (!wrapped) return E_OUTOFMEMORY;
            *enumerator = wrapped;
            return result;
        }
        HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (iid == IID_IPropertyStore) InterlockedIncrement(&g_propertyStoreBindToObjectRequests);
            if (!value) return E_POINTER;
            *value = nullptr;
            PCUIDLIST_RELATIVE original = nullptr;
            auto result = UnwrapChildPidlView(item, &original);
            if (FAILED(result)) return result;
            if (iid == IID_IPropertyStoreFactory && InterlockedCompareExchange(&g_propertyStoreFactoryProbeEnabled, 0, 0))
            {
                InterlockedIncrement(&g_propertyStoreFactoryBindRequests);
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
            if (iid == IID_IPropertyStore) InterlockedIncrement(&g_propertyStoreBindToStorageRequests);
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
            InterlockedIncrement(&g_allComparisons);
            InterlockedExchange(&g_lastCompareColumn, LOWORD(column));
            const auto columnIndex = LOWORD(column);
            if (columnIndex == _columnCount + 1) return _inner->CompareIDs(15, left, right);
            const bool isStaticNumeric = columnIndex == _columnCount || columnIndex == _columnCount + 2;
            if (!isStaticNumeric && columnIndex != 1) return _inner->CompareIDs(column, left, right);
            if (columnIndex == 1) InterlockedIncrement(&g_standardSizeComparisons);
            else if (columnIndex == _columnCount + 2) InterlockedIncrement(&g_documentByteCountComparisons);
            else InterlockedIncrement(&g_staticColumnComparisons);
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
            if (iid == IID_IPropertyStore) InterlockedIncrement(&g_propertyStoreUiObjectRequests);
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
            InterlockedIncrement(&g_defaultColumnRequests);
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
            if (*column == kSizeKey) InterlockedIncrement(&g_standardSizeDetailsEx);
            else if (*column == kDocumentByteCountKey) InterlockedIncrement(&g_documentByteCountDetailsEx);
            else InterlockedIncrement(&g_customDetailsEx);
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
                if (item) InterlockedIncrement(&g_documentByteCountDetailsOf);
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
            if (column == 1 && item) InterlockedIncrement(&g_standardSizeDetailsOf);
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
            InterlockedIncrement(&g_customDetailsOf);
            if (item) InterlockedIncrement(&g_customItemDetailsOf);
            else InterlockedIncrement(&g_customHeaderDetailsOf);
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
            InterlockedIncrement(&g_mapColumnToScidRequests);
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
extern "C" __declspec(dllexport) HRESULT WINAPI WrapStaticShellChildPidl(
    PCUIDLIST_RELATIVE source, PIDLIST_RELATIVE* wrapped)
{ return WrapChildPidl(source, wrapped); }

extern "C" __declspec(dllexport) HRESULT WINAPI UnwrapStaticShellChildPidl(
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

extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellColumnComparisonCount()
{
    return InterlockedCompareExchange(&g_staticColumnComparisons, 0, 0);
}

extern "C" __declspec(dllexport) void WINAPI ResetStaticShellColumnProbeCounters()
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

extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreFactoryBindRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreFactoryBindRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreFactoryCreateRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreFactoryCreateRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreValueRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreValueRequests, 0, 0);
}
extern "C" __declspec(dllexport) void WINAPI SetStaticShellPropertyStoreFactoryProbe(BOOL enabled)
{
    InterlockedExchange(&g_propertyStoreFactoryProbeEnabled, enabled ? 1 : 0);
}

extern "C" __declspec(dllexport) void WINAPI SetStaticShellLegacyDetailsCallbackProbe(BOOL enabled)
{
    InterlockedExchange(&g_legacyDetailsCallbackEnabled, enabled ? 1 : 0);
}
extern "C" __declspec(dllexport) void WINAPI SetStaticShellSortCallbackProbe(BOOL enabled)
{
    InterlockedExchange(&g_sortCallbackProbeEnabled, enabled ? 1 : 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellSortCallbackColumnClickCount()
{
    return InterlockedCompareExchange(&g_sortCallbackColumnClickRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellSortCallbackLastColumn()
{
    return InterlockedCompareExchange(&g_sortCallbackLastColumn, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellSortCallbackGetDefaultsCount()
{
    return InterlockedCompareExchange(&g_sortCallbackGetDefaultsRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellLegacyDetailsCallbackRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellLegacyDetailsCallbackHeaderRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackHeaderRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellLegacyDetailsCallbackItemRequestCount()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackItemRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellLegacyDetailsCallbackLastColumn()
{
    return InterlockedCompareExchange(&g_legacyDetailsCallbackLastColumn, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetAllShellColumnComparisonCount()
{
    return InterlockedCompareExchange(&g_allComparisons, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetLastShellColumnComparisonIndex()
{
    return InterlockedCompareExchange(&g_lastCompareColumn, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetCustomShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_customDetailsEx, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetStandardSizeShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_standardSizeDetailsEx, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetStandardSizeShellComparisonCount()
{
    return InterlockedCompareExchange(&g_standardSizeComparisons, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetStandardSizeShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_standardSizeDetailsOf, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetCustomShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customDetailsOf, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetCustomShellItemDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customItemDetailsOf, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetCustomShellHeaderDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customHeaderDetailsOf, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetDocumentByteCountShellComparisonCount()
{
    return InterlockedCompareExchange(&g_documentByteCountComparisons, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetDocumentByteCountShellDetailsExCount()
{
    return InterlockedCompareExchange(&g_documentByteCountDetailsEx, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetDocumentByteCountShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_documentByteCountDetailsOf, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetMapColumnToScidRequestCount()
{
    return InterlockedCompareExchange(&g_mapColumnToScidRequests, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetDefaultColumnRequestCount()
{
    return InterlockedCompareExchange(&g_defaultColumnRequests, 0, 0);
}

extern "C" __declspec(dllexport) void WINAPI SetDefaultColumnSortProbe(BOOL enabled)
{
    InterlockedExchange(&g_defaultColumnSortProbeEnabled, enabled ? 1 : 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreQueryInterfaceRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreQueryInterfaceRequests, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreBindToObjectRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreBindToObjectRequests, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreBindToStorageRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreBindToStorageRequests, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetPropertyStoreUiObjectRequestCount()
{
    return InterlockedCompareExchange(&g_propertyStoreUiObjectRequests, 0, 0);
}

extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellPersistClassRequestCount()
{
    return InterlockedCompareExchange(&g_persistClassIdRequests, 0, 0);
}
extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellPersistRootRequestCount()
{
    return InterlockedCompareExchange(&g_persistRootRequests, 0, 0);
}
extern "C" __declspec(dllexport) UINT WINAPI GetStaticShellDetailsSize()
{
    return static_cast<UINT>(sizeof(SHELLDETAILS));
}
extern "C" __declspec(dllexport) HRESULT WINAPI CreateStaticShellFolderAdapter(
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
extern "C" __declspec(dllexport) HRESULT WINAPI CreateStrictShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" __declspec(dllexport) HRESULT WINAPI CreateClassIdentityShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, true, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" __declspec(dllexport) HRESULT WINAPI CreateWrappedPidlShellFolderAdapter(
    IShellFolder2* folder, IShellFolder2** adapter)
{
    if (!folder || !adapter) return E_POINTER;
    *adapter = nullptr;
    auto* instance = new (std::nothrow) StaticShellFolderAdapter(folder, false, false, true);
    if (!instance) return E_OUTOFMEMORY;
    *adapter = instance;
    return S_OK;
}
extern "C" __declspec(dllexport) HRESULT WINAPI VerifyStaticShellViewFolder(
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
    };

    constexpr SyntheticChild kSyntheticChildren[] =
    {
        { sizeof(SyntheticChild), 1, 100, 0, L"a-large.bin" },
        { sizeof(SyntheticChild), 2, 2, 0, L"two.bin" },
        { sizeof(SyntheticChild), 3, 10, 0, L"z-ten.bin" }
    };
    volatile LONG g_syntheticCompareCount = 0;
    volatile LONG g_syntheticSizeCompareCount = 0;
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
    volatile LONG g_syntheticMaskFilesystemAttributes = 0;
    volatile LONG g_syntheticOwnAttributesForProbe = 0;
    volatile LONG g_syntheticCustomSizeColumnForProbe = 0;
    volatile LONG g_syntheticExtendedMetadataColumnsForProbe = 0;
    volatile LONG64 g_syntheticChildSizeOverride = -1;
    // Exclude synchronous property callbacks caused by the probe's own
    // ListView text reads from notification-strategy counters.
    thread_local BOOL g_syntheticSizeMetricsObservation = FALSE;
    constexpr PROPERTYKEY kSyntheticRecursiveSizeKey =
    { { 0x7E1F84B7, 0xC052, 0x4E51, { 0x8A, 0x3C, 0x86, 0x92, 0x54, 0xA6, 0xB9, 0x10 } }, 1 };
    constexpr PROPERTYKEY kSyntheticRelativeSizeKey =
    { { 0x7E1F84B7, 0xC052, 0x4E51, { 0x8A, 0x3C, 0x86, 0x92, 0x54, 0xA6, 0xB9, 0x10 } }, 2 };
    volatile LONG g_syntheticEnumObjectsCount = 0;
    volatile LONG g_syntheticEnumeratedItems = 0;
    volatile LONG g_syntheticClassFactoryCreateCount = 0;
    std::wstring g_syntheticSortBackingPath;

    const SyntheticChild* SyntheticData(PCUITEMID_CHILD item)
    {
        if (!item || item->mkid.cb != sizeof(SyntheticChild)) return nullptr;
        const auto* child = reinterpret_cast<const SyntheticChild*>(item);
        return child->id != 0 ? child : nullptr;
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

    std::wstring SyntheticContentsText(const SyntheticChild* child)
    {
        if (!child) return {};
        return std::to_wstring(child->fileCount) + (child->fileCount == 1 ? L" file, " : L" files, ") +
            std::to_wstring(child->folderCount) + (child->folderCount == 1 ? L" folder" : L" folders");
    }

    struct SyntheticDirectoryMetrics
    {
        ULONGLONG bytes;
        ULONGLONG files;
        ULONGLONG folders;
    };

    HRESULT GetSyntheticDirectoryMetrics(const std::wstring& directory, SyntheticDirectoryMetrics* metrics)
    {
        if (!metrics) return E_POINTER;
        *metrics = {};
        std::wstring pattern;
        try { pattern = directory + L"\\*"; }
        catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }

        WIN32_FIND_DATAW entry{};
        const auto search = FindFirstFileW(pattern.c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        HRESULT result = S_OK;
        bool hasEntry = true;
        while (hasEntry)
        {
            if (wcscmp(entry.cFileName, L".") != 0 && wcscmp(entry.cFileName, L"..") != 0)
            {
                ULONGLONG entrySize = (static_cast<ULONGLONG>(entry.nFileSizeHigh) << 32) | entry.nFileSizeLow;
                if (entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
                {
                    if (entry.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT)
                    {
                        if (metrics->folders == ~static_cast<ULONGLONG>(0))
                        {
                            result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                            break;
                        }
                        ++metrics->folders;
                    }
                    else
                    {
                        std::wstring childPath;
                        try { childPath = directory + L"\\" + entry.cFileName; }
                        catch (const std::bad_alloc&) { result = E_OUTOFMEMORY; break; }
                        SyntheticDirectoryMetrics nested{};
                        result = GetSyntheticDirectoryMetrics(childPath, &nested);
                        if (FAILED(result)) break;
                        entrySize = nested.bytes;
                        if (~static_cast<ULONGLONG>(0) - metrics->files < nested.files ||
                            ~static_cast<ULONGLONG>(0) - metrics->folders <= nested.folders)
                        {
                            result = HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
                            break;
                        }
                        metrics->files += nested.files;
                        metrics->folders += nested.folders + 1;
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
                if (error != ERROR_NO_MORE_FILES) result = HRESULT_FROM_WIN32(error);
            }
            if (FAILED(result)) break;
        }
        FindClose(search);
        if (FAILED(result)) *metrics = {};
        return result;
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
                    SyntheticDirectoryMetrics metrics{};
                    result = GetSyntheticDirectoryMetrics(childPath, &metrics);
                    if (FAILED(result)) break;
                    child.size = metrics.bytes;
                    child.fileCount = metrics.files;
                    child.folderCount = metrics.folders;
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
                InterlockedAdd(&g_syntheticEnumeratedItems, static_cast<LONG>(made));
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

    class SyntheticSortFolder final : public IShellFolder2, public IPersistFolder2, public IShellFolderViewCB
    {
    public:
        SyntheticSortFolder() { InterlockedIncrement(&g_syntheticLiveObjects); }
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
        HRESULT SetRecursiveSizeSnapshotForProbe(PCUITEMID_CHILD item, ULONGLONG bytes)
        {
            const auto* child = SyntheticData(item);
            if (!child || !(child->attributes & FILE_ATTRIBUTE_DIRECTORY)) return E_INVALIDARG;
            try
            {
                std::lock_guard<std::mutex> lock(_recursiveSizeMutex);
                _recursiveSizeByRowName[child->name] = bytes;
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetClassID(CLSID* value) override
        {
            if (!value) return E_POINTER;
            *value = g_syntheticFolderClassId;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE value) override
        {
            TraceSyntheticSortStage("synthetic-sort: Initialize-enter");
            if (!value) return E_INVALIDARG;
            auto* copy = ILCloneFull(value);
            if (!copy) return E_OUTOFMEMORY;
            if (g_syntheticSortBackingPath.empty()) { CoTaskMemFree(copy); return E_UNEXPECTED; }
            PIDLIST_ABSOLUTE backingPidl = nullptr;
            TraceSyntheticSortStage("synthetic-sort: backing-SHParseDisplayName-enter");
            auto result = SHParseDisplayName(g_syntheticSortBackingPath.c_str(), nullptr, &backingPidl, 0, nullptr);
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
            if (_backingRoot) CoTaskMemFree(_backingRoot);
            _backingRoot = backingRoot;
            if (_inner) _inner->Release();
            _inner = backingFolder;
            try { _backingPath = g_syntheticSortBackingPath; }
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
            if (message == SFVM_GETNOTIFY) TraceSyntheticSortStage("synthetic-sort: SFVM_GETNOTIFY");
            if (message == SFVM_FSNOTIFY)
            {
                TraceSyntheticSortStage("synthetic-sort: SFVM_FSNOTIFY");
                return ScheduleRefresh();
            }
            if (message == SFVM_SETISFV)
            {
                TraceSyntheticSortStage("synthetic-sort: SFVM_SETISFV");
                return lParam ? S_OK : E_POINTER;
            }
            if (message == SFVM_WINDOWCREATED)
            {
                TraceSyntheticSortStage("synthetic-sort: SFVM_WINDOWCREATED");
            }
            if (message != SFVM_GETNOTIFY) return E_NOTIMPL;
            if (!wParam || !lParam || !_backingRoot) return E_POINTER;
            *reinterpret_cast<PIDLIST_ABSOLUTE*>(wParam) = _backingRoot;
            *reinterpret_cast<LONG*>(lParam) = SHCNE_ALLEVENTS;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND owner, IBindCtx* bind, LPWSTR name, ULONG* eaten,
            PIDLIST_RELATIVE* item, ULONG* attributes) override
        {
            if (!item) return E_POINTER;
            *item = nullptr;
            if (!_inner || !name) return E_INVALIDARG;
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
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticEnumObjectsCount);
            if (!_inner) return E_UNEXPECTED;
            std::vector<SyntheticChild> children;
            HRESULT result = S_OK;
            try { result = EnumerateBackingChildren(_backingPath, flags, &children); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            if (FAILED(result)) return result;
            *value = new (std::nothrow) SyntheticChildEnumerator(std::move(children));
            return *value ? S_OK : E_OUTOFMEMORY;
        }
        HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (!_inner) return E_UNEXPECTED;
            const auto* child = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(item));
            if (!child) return E_INVALIDARG;
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
            auto* nested = new (std::nothrow) SyntheticSortFolder();
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
            const auto* leftChild = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(left));
            const auto* rightChild = SyntheticData(reinterpret_cast<PCUITEMID_CHILD>(right));
            if (!leftChild || !rightChild) return E_INVALIDARG;
            InterlockedIncrement(&g_syntheticCompareCount);
            short comparison = 0;
            switch (LOWORD(column))
            {
            case 0: comparison = static_cast<short>(_wcsicmp(leftChild->name, rightChild->name)); break;
            case 1:
                if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticSizeCompareCount);
                comparison = SizeForChild(leftChild) < SizeForChild(rightChild) ? -1 :
                    SizeForChild(leftChild) > SizeForChild(rightChild) ? 1 : 0;
                break;
            case 2: comparison = 0; break;
            case 3:
                if (!InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0)) return E_INVALIDARG;
                comparison = RelativePercentForChild(leftChild) < RelativePercentForChild(rightChild) ? -1 :
                    RelativePercentForChild(leftChild) > RelativePercentForChild(rightChild) ? 1 : 0;
                break;
            default: return E_INVALIDARG;
            }
            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, static_cast<USHORT>(comparison));
        }
        HRESULT STDMETHODCALLTYPE CreateViewObject(HWND, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IShellView) return E_NOINTERFACE;
            SFV_CREATE create{};
            create.cbSize = sizeof(create);
            create.pshf = this;
            create.psfvcb = static_cast<IShellFolderViewCB*>(this);
            const auto result = SHCreateShellFolderView(&create, reinterpret_cast<IShellView**>(value));
            if (SUCCEEDED(result) && *value && !_shellView)
            {
                for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
                {
                    auto* existing = InterlockedCompareExchangePointer(&g_syntheticSortViewHolds[index], this, nullptr);
                    if (!existing || existing == this)
                    {
                        AddRef();
                        _shellView = static_cast<IShellView*>(*value);
                        _shellView->AddRef();
                        break;
                    }
                }
            }
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT count, PCUITEMID_CHILD_ARRAY items, SFGAOF* attributes) override
        {
            if (!attributes) return E_POINTER;
            if (count == 0) { *attributes = SFGAO_FOLDER | SFGAO_HASSUBFOLDER; return S_OK; }
            if (!_inner) return E_UNEXPECTED;
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
                InterlockedIncrement(&g_syntheticGetAttributesOfCount);
                return S_OK;
            }
            std::vector<PITEMID_CHILD> original;
            try { original.resize(count, nullptr); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            HRESULT result = S_OK;
            for (UINT index = 0; index < count; ++index)
            {
                const auto* child = SyntheticData(items[index]);
                if (!child) { result = E_INVALIDARG; break; }
                ULONG eaten = 0;
                result = _inner->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(child->name), &eaten, &original[index], nullptr);
                if (FAILED(result)) break;
            }
            if (SUCCEEDED(result))
            {
                result = _inner->GetAttributesOf(count, const_cast<PCUITEMID_CHILD_ARRAY>(original.data()), attributes);
                if (SUCCEEDED(result))
                {
                    InterlockedIncrement(&g_syntheticGetAttributesOfCount);
                    if (*attributes & (SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR))
                        InterlockedIncrement(&g_syntheticFilesystemAttributeCount);
                    if (InterlockedCompareExchange(&g_syntheticMaskFilesystemAttributes, 0, 0))
                        *attributes &= ~(SFGAO_FILESYSTEM | SFGAO_FILESYSANCESTOR);
                }
            }
            for (auto item : original) if (item) CoTaskMemFree(item);
            return result;
        }
        HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND owner, UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, UINT* reserved, void** value) override
        {
            if (iid == IID_IPropertyStoreFactory) InterlockedIncrement(&g_syntheticPropertyStoreFactoryRequests);
            else if (iid == IID_IPropertyStore) InterlockedIncrement(&g_syntheticPropertyStoreRequests);
            else InterlockedIncrement(&g_syntheticOtherUiObjectRequests);
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
        HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD item, SHGDNF, STRRET* name) override
        {
            if (!name) return E_POINTER;
            const auto* child = SyntheticData(item);
            if (!child) return E_INVALIDARG;
            name->uType = STRRET_WSTR;
            const auto bytes = (wcslen(child->name) + 1) * sizeof(WCHAR);
            name->pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
            if (!name->pOleStr) return E_OUTOFMEMORY;
            CopyMemory(name->pOleStr, child->name, bytes);
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
            if (!state) return E_POINTER;
            const bool extended = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) != 0;
            if (column > (extended ? 3u : 2u)) return E_INVALIDARG;
            *state = SHCOLSTATE_ONBYDEFAULT | (column == 1 || column == 3 ? SHCOLSTATE_TYPE_INT : SHCOLSTATE_TYPE_STR);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* key, VARIANT* value) override
        {
            if (key && *key == kSizeKey)
            {
                if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticSizeDetailsExCount);
                const auto* child = SyntheticData(item);
                if (child && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticSizeDetailsExFolderCount);
                }
                else if (child)
                {
                    if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticSizeDetailsExFileCount);
                }
            }
            if (key && *key == kSyntheticRecursiveSizeKey)
            {
                if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticCustomSizeDetailsExCount);
                const auto* child = SyntheticData(item);
                if (child && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                {
                    if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticCustomSizeDetailsExFolderCount);
                }
                else if (child)
                {
                    if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticCustomSizeDetailsExFileCount);
                }
            }
            if (!key || !value) return E_INVALIDARG;
            const auto* child = SyntheticData(item);
            if (!child) return E_INVALIDARG;
            VariantInit(value);
            if (*key == kSyntheticRelativeSizeKey &&
                InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0))
            {
                value->vt = VT_R8;
                value->dblVal = RelativePercentForChild(child);
            }
            else if (*key == kSizeKey || *key == kSyntheticRecursiveSizeKey)
            {
                value->vt = VT_UI8;
                value->ullVal = SizeForChild(child);
            }
            else return E_INVALIDARG;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override
        {
            if (item)
            {
                if (!g_syntheticSizeMetricsObservation) InterlockedIncrement(&g_syntheticItemDetailsOfCount);
                if (column == 1 && !g_syntheticSizeMetricsObservation)
                    InterlockedIncrement(&g_syntheticSizeDetailsOfItemCount);
            }
            const bool extended = InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0) != 0;
            if (!details || column > (extended ? 3u : 2u)) return E_INVALIDARG;
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
                text = column == 0 ? child->name : column == 1 ? std::to_wstring(SizeForChild(child)) :
                    column == 2 ? ((extended && (child->attributes & FILE_ATTRIBUTE_DIRECTORY))
                        ? SyntheticContentsText(child)
                        : ((child->attributes & FILE_ATTRIBUTE_DIRECTORY) ? L"File folder" : L"File")) :
                    std::to_wstring(RelativePercentForChild(child)) + L"%";
                details->fmt = column == 1 || column == 3 ? LVCFMT_RIGHT : LVCFMT_LEFT;
                details->cxChar = 16;
            }
            details->str.uType = STRRET_WSTR;
            const auto bytes = (text.size() + 1) * sizeof(WCHAR);
            details->str.pOleStr = static_cast<LPWSTR>(CoTaskMemAlloc(bytes));
            if (!details->str.pOleStr) return E_OUTOFMEMORY;
            CopyMemory(details->str.pOleStr, text.c_str(), bytes);
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT column, SHCOLUMNID* key) override
        {
            if (!key) return E_POINTER;
            if (column == 0) *key = PKEY_ItemNameDisplay;
            else if (column == 1 && InterlockedCompareExchange(&g_syntheticCustomSizeColumnForProbe, 0, 0))
                *key = kSyntheticRecursiveSizeKey;
            else if (column == 1) *key = PKEY_Size;
            else if (column == 2) *key = PKEY_ItemTypeText;
            else if (column == 3 && InterlockedCompareExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0, 0))
                *key = kSyntheticRelativeSizeKey;
            else return E_INVALIDARG;
            return S_OK;
        }
        void ReleaseViewHold()
        {
            auto* view = _shellView;
            _shellView = nullptr;
            if (view) view->Release();
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
        HRESULT NotifyRefreshForProbe() { return MessageSFVCB(SFVM_FSNOTIFY, 0, 0); }
    private:
        ULONGLONG SizeForChild(const SyntheticChild* child) const
        {
            if (!child) return 0;
            {
                std::lock_guard<std::mutex> lock(_recursiveSizeMutex);
                const auto found = _recursiveSizeByRowName.find(child->name);
                if (found != _recursiveSizeByRowName.end()) return found->second;
            }
            return SyntheticSize(child);
        }
        double RelativePercentForChild(const SyntheticChild* child) const
        {
            return child && child->parentBytes != 0
                ? 100.0 * static_cast<double>(SizeForChild(child)) / static_cast<double>(child->parentBytes)
                : 0.0;
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
            else if (!(child->attributes & FILE_ATTRIBUTE_REPARSE_POINT))
            {
                std::wstring childPath;
                try { childPath = backingPath + L"\\" + child->name; }
                catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
                SyntheticDirectoryMetrics metrics{};
                result = GetSyntheticDirectoryMetrics(childPath, &metrics);
                if (FAILED(result)) return result;
                child->size = metrics.bytes;
                child->fileCount = metrics.files;
                child->folderCount = metrics.folders;
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
        static VOID CALLBACK RefreshTimerProc(HWND, UINT, UINT_PTR timerId, DWORD)
        {
            KillTimer(nullptr, timerId);
            auto* folder = FindPendingRefresh(timerId);
            if (!folder) return;
            InterlockedIncrement(&g_syntheticDeferredRefreshCount);
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
        ~SyntheticSortFolder() { ReleaseViewHold(); if (_root) CoTaskMemFree(_root); if (_backingRoot) CoTaskMemFree(_backingRoot); if (_inner) _inner->Release(); InterlockedDecrement(&g_syntheticLiveObjects); }
        volatile LONG _references = 1;
        volatile LONG _refreshAgain = 0;
        PIDLIST_ABSOLUTE _root = nullptr;
        PIDLIST_ABSOLUTE _backingRoot = nullptr;
        IShellView* _shellView = nullptr;
        std::wstring _backingPath;
        IShellFolder2* _inner = nullptr;
        mutable std::mutex _recursiveSizeMutex;
        std::unordered_map<std::wstring, ULONGLONG> _recursiveSizeByRowName;
        std::vector<SyntheticChild> _pendingSelected;
        SyntheticChild _pendingFocused{};
        bool _pendingHasFocused = false;
        volatile LONG _refreshPending = 0;
        UINT_PTR _refreshTimerId = 0;
        ULONGLONG _restoreDeadline = 0;
    };

    class SyntheticFolderClassFactory final : public IClassFactory
    {
    public:
        SyntheticFolderClassFactory() { InterlockedIncrement(&g_syntheticLiveObjects); }
        ~SyntheticFolderClassFactory() { InterlockedDecrement(&g_syntheticLiveObjects); }
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
            InterlockedIncrement(&g_syntheticClassFactoryCreateCount);
            auto* folder = new (std::nothrow) SyntheticSortFolder();
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
    };
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
    if (classId != g_syntheticFolderClassId) return CLASS_E_CLASSNOTAVAILABLE;
    auto* factory = new (std::nothrow) SyntheticFolderClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    const auto result = factory->QueryInterface(iid, value);
    factory->Release();
    return result;
}

STDAPI DllCanUnloadNow()
{
    return InterlockedCompareExchange(&g_syntheticLiveObjects, 0, 0) == 0 &&
        InterlockedCompareExchange(&g_syntheticServerLocks, 0, 0) == 0 ? S_OK : S_FALSE;
}

extern "C" __declspec(dllexport) HRESULT WINAPI RunSyntheticDllUnloadContractSmoke()
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

extern "C" __declspec(dllexport) HRESULT WINAPI CreateSyntheticSortShellView(PCIDLIST_ABSOLUTE rootPidl, IShellFolder2** folder, IShellView** view)
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

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticSortBackingPath(PCWSTR path)
{
    if (!path || !*path) return E_INVALIDARG;
    try { g_syntheticSortBackingPath.assign(path); }
    catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticFolderClassIdForProbe(const CLSID* classId)
{
    if (!classId) return E_POINTER;
    if (InterlockedCompareExchange(&g_syntheticLiveObjects, 0, 0) != 0 ||
        InterlockedCompareExchange(&g_syntheticServerLocks, 0, 0) != 0)
        return E_UNEXPECTED;
    g_syntheticFolderClassId = *classId;
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI RunSyntheticFolderTreeContractSmoke(PCWSTR backingPath)
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

    result = root->EnumObjects(nullptr, SHCONTF_FOLDERS, &enumerator);
    if (FAILED(result) || !enumerator) { if (SUCCEEDED(result)) result = E_UNEXPECTED; goto cleanup; }
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
    nested->Release();
    nested = nullptr;
    result = S_OK;

    {
        PITEMID_CHILD renamedFolder = nullptr;
        result = root->SetNameOf(nullptr, childFolder, L"renamed-child", SHGDN_INFOLDER, &renamedFolder);
        const bool renamed = SUCCEEDED(result) && renamedFolder && SyntheticData(renamedFolder) &&
            _wcsicmp(SyntheticData(renamedFolder)->name, L"renamed-child") == 0;
        if (renamedFolder) CoTaskMemFree(renamedFolder);
        if (!renamed || GetFileAttributesW((std::wstring(backingPath) + L"\\renamed-child\\nested.txt").c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            result = E_FAIL;
            goto cleanup;
        }
    }
    TraceSyntheticSortStage("synthetic-folder-tree: folder-rename-passed");

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

extern "C" __declspec(dllexport) HRESULT WINAPI RunSyntheticFolderRecursiveSizeProjectionSmoke(
    PCWSTR backingPath, LONGLONG scannedBytes)
{
    if (!backingPath || !*backingPath || scannedBytes < 0) return E_INVALIDARG;
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result)) return result;

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    PITEMID_CHILD childFolder = nullptr;
    PITEMID_CHILD rootFile = nullptr;
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
    result = root->SetRecursiveSizeSnapshotForProbe(childFolder, static_cast<ULONGLONG>(scannedBytes));
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

extern "C" __declspec(dllexport) HRESULT WINAPI RunSyntheticFolderRecursiveSizeByRowSmoke(
    PCWSTR backingPath, LONGLONG initialBytes, LONGLONG otherRowBytes, LONGLONG updatedBytes)
{
    if (!backingPath || !*backingPath || initialBytes < 0 || otherRowBytes < 0 || updatedBytes < 0) return E_INVALIDARG;
    auto result = SetSyntheticSortBackingPath(backingPath);
    if (FAILED(result)) return result;

    PIDLIST_ABSOLUTE rootPidl = nullptr;
    SyntheticSortFolder* root = nullptr;
    PITEMID_CHILD childFolder = nullptr;
    PITEMID_CHILD otherFolder = nullptr;
    PITEMID_CHILD rootFile = nullptr;
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 1);
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);

    result = SHParseDisplayName(backingPath, nullptr, &rootPidl, 0, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    root = new (std::nothrow) SyntheticSortFolder();
    if (!root) { result = E_OUTOFMEMORY; goto cleanup_recursive_size_update; }
    result = root->Initialize(rootPidl);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"child"), nullptr, &childFolder, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"other-child"), nullptr, &otherFolder, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->ParseDisplayName(nullptr, nullptr, const_cast<LPWSTR>(L"root.txt"), nullptr, &rootFile, nullptr);
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->SetRecursiveSizeSnapshotForProbe(childFolder, static_cast<ULONGLONG>(initialBytes));
    if (FAILED(result)) goto cleanup_recursive_size_update;
    result = root->SetRecursiveSizeSnapshotForProbe(otherFolder, static_cast<ULONGLONG>(otherRowBytes));
    if (FAILED(result)) goto cleanup_recursive_size_update;

    {
        const auto rowMatchesSnapshot = [&](PCUITEMID_CHILD row, ULONGLONG expected, bool folderAfterFile)
        {
            SHCOLUMNID mappedKey{};
            if (FAILED(root->MapColumnToSCID(1, &mappedKey)) || mappedKey != kSyntheticRecursiveSizeKey) return false;

            SHELLDETAILS details{};
            const auto detailsResult = root->GetDetailsOf(row, 1, &details);
            const auto expectedText = std::to_wstring(expected);
            const bool textMatches = SUCCEEDED(detailsResult) && details.str.uType == STRRET_WSTR &&
                details.str.pOleStr && expectedText == details.str.pOleStr;
            if (details.str.uType == STRRET_WSTR && details.str.pOleStr) CoTaskMemFree(details.str.pOleStr);
            if (!textMatches) return false;

            VARIANT value{};
            VariantInit(&value);
            const auto propertyResult = root->GetDetailsEx(row, &mappedKey, &value);
            const bool propertyMatches = SUCCEEDED(propertyResult) && value.vt == VT_UI8 && value.ullVal == expected;
            VariantClear(&value);
            if (!propertyMatches) return false;

            const auto compareResult = folderAfterFile
                ? root->CompareIDs(1, row, rootFile)
                : root->CompareIDs(1, rootFile, row);
            return SUCCEEDED(compareResult) && static_cast<SHORT>(HRESULT_CODE(compareResult)) > 0;
        };

        // Sibling rows own independent snapshots; updating one must not replace the other's value.
        if (!rowMatchesSnapshot(childFolder, static_cast<ULONGLONG>(initialBytes), true) ||
            !rowMatchesSnapshot(otherFolder, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }
        result = root->SetRecursiveSizeSnapshotForProbe(childFolder, static_cast<ULONGLONG>(updatedBytes));
        if (FAILED(result)) goto cleanup_recursive_size_update;
        if (!rowMatchesSnapshot(childFolder, static_cast<ULONGLONG>(updatedBytes), false) ||
            !rowMatchesSnapshot(otherFolder, static_cast<ULONGLONG>(otherRowBytes), true))
        {
            result = E_FAIL;
            goto cleanup_recursive_size_update;
        }
    }
    result = S_OK;

cleanup_recursive_size_update:
    InterlockedExchange64(&g_syntheticChildSizeOverride, -1);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
    InterlockedExchange(&g_syntheticExtendedMetadataColumnsForProbe, 0);
    if (rootFile) CoTaskMemFree(rootFile);
    if (otherFolder) CoTaskMemFree(otherFolder);
    if (childFolder) CoTaskMemFree(childFolder);
    if (root) root->Release();
    if (rootPidl) CoTaskMemFree(rootPidl);
    return result;
}

extern "C" __declspec(dllexport) void WINAPI ResetSyntheticSortCounters()
{
    InterlockedExchange(&g_syntheticCompareCount, 0);
    InterlockedExchange(&g_syntheticSizeCompareCount, 0);
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
    InterlockedExchange(&g_syntheticMaskFilesystemAttributes, 0);
    InterlockedExchange(&g_syntheticOwnAttributesForProbe, 0);
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, 0);
    InterlockedExchange(&g_syntheticEnumObjectsCount, 0);
    InterlockedExchange(&g_syntheticEnumeratedItems, 0);
    InterlockedExchange(&g_syntheticClassFactoryCreateCount, 0);
}

extern "C" __declspec(dllexport) void WINAPI ReleaseSyntheticSortViewHold()
{
    for (size_t index = 0; index < kSyntheticSortViewHoldCount; ++index)
    {
        auto* folder = static_cast<SyntheticSortFolder*>(InterlockedExchangePointer(&g_syntheticSortViewHolds[index], nullptr));
        if (!folder) continue;
        folder->CancelPendingRefresh();
        folder->ReleaseViewHold();
        folder->Release();
    }
}

extern "C" __declspec(dllexport) HRESULT WINAPI CancelSyntheticSortRefreshForProbe(IShellView* view)
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

extern "C" __declspec(dllexport) HRESULT WINAPI ScheduleSyntheticSortRefreshForProbe(IShellView* view)
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

extern "C" __declspec(dllexport) HRESULT WINAPI NotifySyntheticSortRefreshForProbe(IShellView* view)
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

extern "C" __declspec(dllexport) LONG WINAPI ScheduleSyntheticSortRefreshesForProbe()
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

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticDeferredRefreshCount()
{ return InterlockedCompareExchange(&g_syntheticDeferredRefreshCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSortCompareCount()
{ return InterlockedCompareExchange(&g_syntheticCompareCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSizeCompareCount()
{ return InterlockedCompareExchange(&g_syntheticSizeCompareCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSizeDetailsExCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSizeDetailsExFolderCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExFolderCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSizeDetailsExFileCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsExFileCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticSizeDetailsOfItemCount()
{ return InterlockedCompareExchange(&g_syntheticSizeDetailsOfItemCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticItemDetailsOfCount()
{ return InterlockedCompareExchange(&g_syntheticItemDetailsOfCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticPropertyStoreFactoryRequestCount()
{ return InterlockedCompareExchange(&g_syntheticPropertyStoreFactoryRequests, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticPropertyStoreRequestCount()
{ return InterlockedCompareExchange(&g_syntheticPropertyStoreRequests, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticGetAttributesOfCount()
{ return InterlockedCompareExchange(&g_syntheticGetAttributesOfCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticFilesystemAttributeCount()
{ return InterlockedCompareExchange(&g_syntheticFilesystemAttributeCount, 0, 0); }

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticMaskFilesystemAttributesForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticMaskFilesystemAttributes, enabled ? 1 : 0);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticOwnAttributesForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticOwnAttributesForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticCustomSizeColumnForProbe(BOOL enabled)
{
    InterlockedExchange(&g_syntheticCustomSizeColumnForProbe, enabled ? 1 : 0);
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticSizeMetricsObservationForProbe(BOOL enabled)
{
    g_syntheticSizeMetricsObservation = enabled ? TRUE : FALSE;
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT WINAPI SetSyntheticChildSizeOverrideForProbe(LONGLONG size)
{
    if (size < -1) return E_INVALIDARG;
    InterlockedExchange64(&g_syntheticChildSizeOverride, size);
    return S_OK;
}

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticCustomSizeDetailsExCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticCustomSizeDetailsExFolderCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExFolderCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticCustomSizeDetailsExFileCount()
{ return InterlockedCompareExchange(&g_syntheticCustomSizeDetailsExFileCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticEnumObjectsCount()
{ return InterlockedCompareExchange(&g_syntheticEnumObjectsCount, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticEnumeratedItemCount()
{ return InterlockedCompareExchange(&g_syntheticEnumeratedItems, 0, 0); }

extern "C" __declspec(dllexport) LONG WINAPI GetSyntheticClassFactoryCreateCount()
{ return InterlockedCompareExchange(&g_syntheticClassFactoryCreateCount, 0, 0); }
