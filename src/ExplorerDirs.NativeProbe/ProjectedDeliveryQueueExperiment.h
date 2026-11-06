#pragma once

#if defined(EXPLORERDIRS_PROJECTED_NOTIFY_BRIDGE_EXPERIMENT)
#include <deque>

// Experimental transport only. No Shell interface pointers, HWNDs or owner callbacks
// cross threads. The caller prepares payloads; the worker receives independent bytes.
namespace ProjectedDeliveryExperiment
{
    struct Payload
    {
        LONG event = 0;
        std::vector<BYTE> first;
        std::vector<BYTE> second;
        ULONGLONG sequence = 0;
        DWORD context = 0;
        std::wstring name;
    };

    class Queue : public std::enable_shared_from_this<Queue>
    {
    public:
        using Deliver = std::function<void(const Payload&)>;
        struct Snapshot { size_t queued; bool inFlight; bool running; bool retired; HRESULT failure; };

        static std::shared_ptr<Queue> Create(size_t capacity, Deliver deliver)
        { return std::shared_ptr<Queue>(new Queue(capacity, std::move(deliver))); }

        HRESULT Enqueue(const Payload& payload)
        {
            std::lock_guard<std::mutex> lock(_mutex);
            if (_retired) return CO_E_OBJNOTCONNECTED;
            if (_pending.size() + (_inFlight ? 1 : 0) >= _capacity)
            {
                RetireLocked(HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW));
                return _failure;
            }
            try { _pending.push_back(payload); }
            catch (const std::bad_alloc&) { RetireLocked(E_OUTOFMEMORY); return _failure; }
            if (_running) return S_OK;
            HMODULE module = nullptr;
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                reinterpret_cast<LPCWSTR>(&Run), &module))
            {
                RetireLocked(HRESULT_FROM_WIN32(GetLastError()));
                return _failure;
            }
            auto* context = new (std::nothrow) Context{ shared_from_this(), module };
            if (!context)
            {
                FreeLibrary(module); RetireLocked(E_OUTOFMEMORY); return _failure;
            }
            auto* work = CreateThreadpoolWork(Run, context, nullptr);
            if (!work)
            {
                const auto error = HRESULT_FROM_WIN32(GetLastError());
                delete context; FreeLibrary(module); RetireLocked(error); return _failure;
            }
            _running = true;
            SubmitThreadpoolWork(work);
            return S_OK;
        }

        void Retire()
        {
            std::lock_guard<std::mutex> lock(_mutex);
            RetireLocked(S_OK);
        }

        Snapshot Read()
        {
            std::lock_guard<std::mutex> lock(_mutex);
            return { _pending.size(), _inFlight, _running, _retired, _failure };
        }

        // Probe-only synchronization. Product UI must never wait for a worker.
        bool WaitIdleForProbe(DWORD milliseconds)
        {
            std::unique_lock<std::mutex> lock(_mutex);
            return _idle.wait_for(lock, std::chrono::milliseconds(milliseconds), [&] { return !_running; });
        }

    private:
        struct Context { std::shared_ptr<Queue> queue; HMODULE module; };
        Queue(size_t capacity, Deliver deliver) : _capacity(capacity), _deliver(std::move(deliver)) { }

        void RetireLocked(HRESULT failure)
        {
            _retired = true;
            _pending.clear();
            if (FAILED(failure)) _failure = failure;
            // An accepted in-flight call may finish after retirement. Do not hold
            // this mutex during delivery or join from the UI; do not claim retraction.
        }

        static void CALLBACK Run(PTP_CALLBACK_INSTANCE instance, void* raw, PTP_WORK work)
        {
            std::unique_ptr<Context> context(static_cast<Context*>(raw));
            // OS releases the code-module reference only after this callback returns.
            FreeLibraryWhenCallbackReturns(instance, context->module);
            auto queue = context->queue;
            for (;;)
            {
                Payload payload;
                {
                    std::lock_guard<std::mutex> lock(queue->_mutex);
                    if (queue->_retired || queue->_pending.empty())
                    {
                        queue->_running = false;
                        queue->_idle.notify_all();
                        break;
                    }
                    payload = std::move(queue->_pending.front());
                    queue->_pending.pop_front();
                    queue->_inFlight = true;
                }
                HRESULT failure = S_OK;
                try { queue->_deliver(payload); }
                catch (...) { failure = E_FAIL; }
                {
                    std::lock_guard<std::mutex> lock(queue->_mutex);
                    queue->_inFlight = false;
                    if (FAILED(failure)) queue->RetireLocked(failure);
                }
            }
            CloseThreadpoolWork(work);
        }

        const size_t _capacity;
        const Deliver _deliver;
        std::mutex _mutex;
        std::condition_variable _idle;
        std::deque<Payload> _pending;
        bool _inFlight = false;
        bool _running = false;
        bool _retired = false;
        HRESULT _failure = S_OK;
    };
}
#endif
