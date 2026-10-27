#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <propsys.h>
#include <propkey.h>
#include <strsafe.h>
#include <new>
#include <string>
#include <vector>
#include <cstdio>

namespace
{
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
    volatile LONG g_propertyStoreQueryInterfaceRequests = 0;
    volatile LONG g_persistClassIdRequests = 0;
    volatile LONG g_persistRootRequests = 0;
    volatile LONG g_propertyStoreBindToObjectRequests = 0;
    volatile LONG g_propertyStoreBindToStorageRequests = 0;
    volatile LONG g_propertyStoreUiObjectRequests = 0;

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
            return SHCreateShellFolderView(&create, reinterpret_cast<IShellView**>(value));
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
        HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD reserved, ULONG* sort, ULONG* display) override { return _inner->GetDefaultColumn(reserved, sort, display); }
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
    InterlockedExchange(&g_propertyStoreQueryInterfaceRequests, 0);
    InterlockedExchange(&g_persistClassIdRequests, 0);
    InterlockedExchange(&g_persistRootRequests, 0);
    InterlockedExchange(&g_propertyStoreBindToObjectRequests, 0);
    InterlockedExchange(&g_propertyStoreBindToStorageRequests, 0);
    InterlockedExchange(&g_propertyStoreUiObjectRequests, 0);
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
