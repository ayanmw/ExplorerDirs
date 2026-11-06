#pragma once

#if defined(EXPLORERDIRS_PROJECTED_NOTIFY_BRIDGE_EXPERIMENT)
#include <deque>
#include <set>

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
        ULONGLONG owner = 0; // Zero retains the original unscoped transport contract.
    };

    class Queue : public std::enable_shared_from_this<Queue>
    {
    public:
        using Deliver = std::function<void(const Payload&, bool)>;
        struct Snapshot { size_t queued; bool inFlight; bool running; bool retired; HRESULT failure; };

        static std::shared_ptr<Queue> Create(size_t capacity, Deliver deliver, size_t flushBatchSize = 1)
        { return std::shared_ptr<Queue>(new Queue(capacity, std::move(deliver), flushBatchSize)); }

        HRESULT Enqueue(const Payload& payload)
        { return EnqueueItems(&payload, 1); }

        HRESULT EnqueueBatch(const std::vector<Payload>& payloads)
        { return EnqueueItems(payloads.data(), payloads.size()); }

        HRESULT RegisterOwner(ULONGLONG* owner)
        {
            if (!owner) return E_POINTER;
            *owner = 0;
            std::lock_guard<std::mutex> lock(_mutex);
            if (_retired) return CO_E_OBJNOTCONNECTED;
            if (_nextOwner == ~ULONGLONG{ 0 }) return HRESULT_FROM_WIN32(ERROR_ARITHMETIC_OVERFLOW);
            const auto next = _nextOwner + 1;
            try { _owners.insert(next); }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
            *owner = _nextOwner = next;
            return S_OK;
        }

        void RetireOwner(ULONGLONG owner)
        {
            if (!owner) return;
            std::lock_guard<std::mutex> lock(_mutex);
            _owners.erase(owner);
            _pending.erase(std::remove_if(_pending.begin(), _pending.end(),
                [owner](const Payload& payload) { return payload.owner == owner; }), _pending.end());
            // The accepted call is deliberately left intact. Other owners'
            // queued items retain FIFO order and the worker keeps its module pin.
        }

    private:
        HRESULT EnqueueItems(const Payload* payloads, size_t count)
        {
            std::lock_guard<std::mutex> lock(_mutex);
            if (_retired) return CO_E_OBJNOTCONNECTED;
            if (count == 0) return S_OK;
            for (size_t index = 0; index < count; ++index)
                if (payloads[index].owner && !_owners.count(payloads[index].owner)) return CO_E_OBJNOTCONNECTED;
            const auto occupied = _pending.size() + (_inFlight ? 1 : 0);
            if (occupied > _capacity || count > _capacity - occupied)
            {
                RetireLocked(HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW));
                return _failure;
            }
            // Hold the submission lock through copying the entire batch and
            // scheduling: the worker must never observe a partial submission.
            // Allocation failure retires/clears it before any new item escapes.
            try { for (size_t index = 0; index < count; ++index) _pending.push_back(payloads[index]); }
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

    public:
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
        Queue(size_t capacity, Deliver deliver, size_t flushBatchSize)
            : _capacity(capacity), _flushBatchSize(flushBatchSize), _deliver(std::move(deliver)) { }

        void RetireLocked(HRESULT failure)
        {
            _retired = true;
            _pending.clear();
            _owners.clear();
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
            size_t sinceFlush = 0;
            for (;;)
            {
                Payload payload;
                bool flush = false;
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
                    flush = ++sinceFlush >= queue->_flushBatchSize || queue->_pending.empty();
                    if (flush) sinceFlush = 0;
                }
                HRESULT failure = S_OK;
                try { queue->_deliver(payload, flush); }
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
        const size_t _flushBatchSize;
        const Deliver _deliver;
        std::mutex _mutex;
        std::condition_variable _idle;
        std::deque<Payload> _pending;
        std::set<ULONGLONG> _owners;
        ULONGLONG _nextOwner = 0;
        bool _inFlight = false;
        bool _running = false;
        bool _retired = false;
        HRESULT _failure = S_OK;
    };
}
#endif
