using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Peak.Can.Basic;

namespace MyCanBusTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Hardware Konfiguration
        private const ushort PcanHandle = PCANBasic.PCAN_USBBUS1;
        private const TPCANBaudrate Baudrate = TPCANBaudrate.PCAN_BAUD_500K;

        // Threading & Timing
        private MultimediaTimer _txTimer;
        private Thread _rxThread;
        private volatile bool _isRunning = false;

        // Datenbindung
        public ObservableCollection<CanLogEntry> ReceivedMessages { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            ReceivedMessages = new ObservableCollection<CanLogEntry>();
            DgMessages.ItemsSource = ReceivedMessages;

            // Timer initialisieren, aber noch nicht starten
            _txTimer = new MultimediaTimer(SendCyclicMessage);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            // 1. Verbindung herstellen
            TPCANStatus status = PCANBasic.Initialize(PcanHandle, Baudrate);

            if (status != TPCANStatus.PCAN_ERROR_OK)
            {
                MessageBox.Show($"Fehler beim Initialisieren: {status}");
                return;
            }

            _isRunning = true;
            TxtStatus.Text = "Connected & Running";

            // 2. RX Thread starten
            _rxThread = new Thread(ReceiveLoop);
            _rxThread.IsBackground = true; // Beendet sich automatisch wenn App schließt
            _rxThread.Priority = ThreadPriority.AboveNormal;
            _rxThread.Start();

            // 3. TX Timer starten (10ms)
            _txTimer.Start(10);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopCan();
        }

        private void StopCan()
        {
            _isRunning = false;
            _txTimer.Stop();

            // Kurz warten, bis Thread fertig ist (optional sauber joinen)
            Thread.Sleep(50);

            PCANBasic.Uninitialize(PcanHandle);
            TxtStatus.Text = "Stopped";
        }

        // --- TX: Senden alle 10ms (Aufgerufen vom Multimedia Timer Thread) ---
        private void SendCyclicMessage()
        {
            if (!_isRunning) return;

            TPCANMsg msg = new TPCANMsg();
            msg.ID = 0x100;
            msg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;
            msg.LEN = 8;

            // Dummy Daten: Zähler hochzählen
            long ticks = DateTime.Now.Ticks;
            msg.DATA = new byte[8];
            msg.DATA[0] = (byte)(ticks & 0xFF);
            msg.DATA[1] = (byte)((ticks >> 8) & 0xFF);
            // ... Rest 0

            TPCANStatus status = PCANBasic.Write(PcanHandle, ref msg);

            // Optional: Fehlerbehandlung, aber Vorsicht: Console/UI Zugriff hier teuer!
        }

        // --- RX: Empfangen in separatem Thread ---
        private void ReceiveLoop()
        {
            TPCANMsg msg = new TPCANMsg();
            TPCANTimestamp timestamp;

            while (_isRunning)
            {
                // Versuche Nachricht zu lesen
                TPCANStatus status = PCANBasic.Read(PcanHandle, out msg, out timestamp);

                if (status == TPCANStatus.PCAN_ERROR_OK)
                {
                    ProcessMessage(msg);
                }
                else
                {
                    // Wenn keine Nachrichten da sind, kurz schlafen um CPU zu schonen
                    // Bei hoher Buslast kann man das Sleep verringern oder weglassen
                    Thread.Sleep(1);
                }
            }
        }

        private void ProcessMessage(TPCANMsg msg)
        {
            // Datenformatierung vorbereiten
            string dataString = BitConverter.ToString(msg.DATA, 0, msg.LEN).Replace("-", " ");

            var entry = new CanLogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Id = msg.ID.ToString("X"),
                Dlc = msg.LEN,
                Data = dataString
            };

            // WICHTIG: UI-Update muss im UI-Thread passieren!
            // Wir nutzen Application.Current.Dispatcher
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Performance-Tipp: Bei sehr vielen Nachrichten (>1000/s) 
                // sollte man die Grid-Updates puffern/drosseln.
                ReceivedMessages.Insert(0, entry);

                // Speicher begrenzen (optional)
                if (ReceivedMessages.Count > 100) ReceivedMessages.RemoveAt(ReceivedMessages.Count - 1);
            }));
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopCan();
            _txTimer.Dispose();
        }
    }
}