using System;
using System.Runtime.InteropServices;

namespace MyCanBusTool
{
    public class MultimediaTimer : IDisposable
    {
        // Importiere die Funktionen aus der winmm.dll
        [DllImport("winmm.dll", EntryPoint = "timeSetEvent")]
        private static extern uint TimeSetEvent(uint delay, uint resolution, TimerCallback handler, UIntPtr user, uint eventType);

        [DllImport("winmm.dll", EntryPoint = "timeKillEvent")]
        private static extern uint TimeKillEvent(uint timerId);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint period);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint period);

        private delegate void TimerCallback(uint id, uint msg, UIntPtr user, UIntPtr dw1, UIntPtr dw2);

        private const uint TIME_PERIODIC = 1;
        private uint _timerId;
        private TimerCallback _callback; // Referenz halten, damit GC sie nicht löscht
        private bool _disposed;
        private readonly Action _action;

        public MultimediaTimer(Action action)
        {
            _action = action;
        }

        public void Start(uint intervalMs)
        {
            // Auflösung auf 1ms setzen für maximale Präzision
            TimeBeginPeriod(1);
            _callback = OnTimerTick;
            // Timer starten (Interval, Auflösung 1ms, Callback, ...)
            _timerId = TimeSetEvent(intervalMs, 1, _callback, UIntPtr.Zero, TIME_PERIODIC);
        }

        public void Stop()
        {
            if (_timerId != 0)
            {
                TimeKillEvent(_timerId);
                TimeEndPeriod(1);
                _timerId = 0;
            }
        }

        private void OnTimerTick(uint id, uint msg, UIntPtr user, UIntPtr dw1, UIntPtr dw2)
        {
            _action?.Invoke();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                Stop();
                _disposed = true;
            }
        }

        ~MultimediaTimer()
        {
            Dispose(false);
        }
    }
}
