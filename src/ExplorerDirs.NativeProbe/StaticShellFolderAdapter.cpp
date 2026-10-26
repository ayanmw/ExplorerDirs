#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <propsys.h>
#include <propkey.h>
#include <strsafe.h>
#include <new>
#include <string>

namespace
{
    // This adapter is loaded only by the opt-in NativeProbe. It forwards normal
    // filesystem PIDLs and Shell operations while adding one deterministic column.
    constexpr PROPERTYKEY kStaticSizeKey =
    { { 0xA38B883C, 0xFA8A, 0x4E1C, { 0x8B, 0x52, 0xC6, 0x05, 0xE5, 0xD0, 0x12, 0x34 } }, 1 };
    constexpr UINT kColumnLimit = 64;
    volatile LONG g_staticColumnComparisons = 0;
    volatile LONG g_allComparisons = 0;
    volatile LONG g_lastCompareColumn = -1;
    volatile LONG g_customDetailsEx = 0;
    volatile LONG g_customDetailsOf = 0;

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

    ULONGLONG ValueForName(const std::wstring& name, bool& found)
    {
        found = true;
        if (_wcsicmp(name.c_str(), L"two.bin") == 0) return 2;
        if (_wcsicmp(name.c_str(), L"ten.bin") == 0 || _wcsicmp(name.c_str(), L"ten-copy.bin") == 0) return 10;
        if (_wcsicmp(name.c_str(), L"hundred.bin") == 0) return 100;
        found = false;
        return 0;
    }

    class StaticShellFolderAdapter final : public IShellFolder2
    {
    public:
        explicit StaticShellFolderAdapter(IShellFolder2* inner) : _inner(inner)
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
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IShellFolder || iid == IID_IShellFolder2)
            {
                *object = static_cast<IShellFolder2*>(this);
                AddRef();
                return S_OK;
            }
            return _inner ? _inner->QueryInterface(iid, object) : E_NOINTERFACE;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&_refs); }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto refs = InterlockedDecrement(&_refs);
            if (!refs) delete this;
            return refs;
        }

        HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND hwnd, IBindCtx* bind, LPWSTR name, ULONG* eaten, PIDLIST_RELATIVE* item, ULONG* attributes) override
        { return _inner->ParseDisplayName(hwnd, bind, name, eaten, item, attributes); }
        HRESULT STDMETHODCALLTYPE EnumObjects(HWND hwnd, SHCONTF flags, IEnumIDList** enumerator) override
        { return _inner->EnumObjects(hwnd, flags, enumerator); }
        HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        { return _inner->BindToObject(item, bind, iid, value); }
        HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE item, IBindCtx* bind, REFIID iid, void** value) override
        { return _inner->BindToStorage(item, bind, iid, value); }
        HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM column, PCUIDLIST_RELATIVE left, PCUIDLIST_RELATIVE right) override
        {
            InterlockedIncrement(&g_allComparisons);
            InterlockedExchange(&g_lastCompareColumn, LOWORD(column));
            if (LOWORD(column) != _columnCount) return _inner->CompareIDs(column, left, right);
            InterlockedIncrement(&g_staticColumnComparisons);
            std::wstring leftName, rightName;
            if (!NameFromPidl(_inner, left, leftName) || !NameFromPidl(_inner, right, rightName)) return E_FAIL;
            bool leftFound = false, rightFound = false;
            const auto leftValue = ValueForName(leftName, leftFound);
            const auto rightValue = ValueForName(rightName, rightFound);
            if (!leftFound || !rightFound) return E_FAIL;
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
        { return _inner->GetAttributesOf(count, items, attributes); }
        HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND owner, UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, UINT* reserved, void** value) override
        { return _inner->GetUIObjectOf(owner, count, items, iid, reserved, value); }
        HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD item, SHGDNF flags, STRRET* name) override
        { return _inner->GetDisplayNameOf(item, flags, name); }
        HRESULT STDMETHODCALLTYPE SetNameOf(HWND owner, PCUITEMID_CHILD item, LPCWSTR name, SHGDNF flags, PITEMID_CHILD* renamed) override
        { return _inner->SetNameOf(owner, item, name, flags, renamed); }
        HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID* value) override { return _inner->GetDefaultSearchGUID(value); }
        HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch** enumerator) override { return _inner->EnumSearches(enumerator); }
        HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD reserved, ULONG* sort, ULONG* display) override { return _inner->GetDefaultColumn(reserved, sort, display); }
        HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column, SHCOLSTATEF* state) override
        {
            if (column == _columnCount) { *state = SHCOLSTATE_TYPE_INT | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
            return column < _columnCount ? _inner->GetDefaultColumnState(column, state) : E_INVALIDARG;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* column, VARIANT* value) override
        {
            if (!column || *column != kStaticSizeKey) return _inner->GetDetailsEx(item, column, value);
            InterlockedIncrement(&g_customDetailsEx);
            if (!item) return E_INVALIDARG;
            std::wstring name;
            if (!NameFromPidl(_inner, item, name)) return E_FAIL;
            bool found = false;
            const auto number = ValueForName(name, found);
            if (!found) return E_FAIL;
            VariantInit(value);
            value->vt = VT_UI8;
            value->ullVal = number;
            return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override
        {
            if (!details) return E_POINTER;
            if (column < _columnCount) return _inner->GetDetailsOf(item, column, details);
            if (column != _columnCount) return E_INVALIDARG;
            InterlockedIncrement(&g_customDetailsOf);
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
                if (!found) return E_FAIL;
                valueText = std::to_wstring(number);
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
            if (column != _columnCount) return E_INVALIDARG;
            *id = kStaticSizeKey;
            return S_OK;
        }

    private:
        ~StaticShellFolderAdapter() { if (_inner) _inner->Release(); }
        volatile LONG _refs = 1;
        IShellFolder2* _inner = nullptr;
        UINT _columnCount = 0;
    };
}

extern "C" __declspec(dllexport) LONG WINAPI GetStaticShellColumnComparisonCount()
{
    return InterlockedCompareExchange(&g_staticColumnComparisons, 0, 0);
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

extern "C" __declspec(dllexport) LONG WINAPI GetCustomShellDetailsOfCount()
{
    return InterlockedCompareExchange(&g_customDetailsOf, 0, 0);
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
