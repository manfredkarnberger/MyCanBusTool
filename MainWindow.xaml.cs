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
    using TPCANHandle = System.UInt16;

    public partial class MainWindow : Window
    {
        // Hardware Konfiguration
        private const TPCANHandle PcanHandle = PCANBasic.PCAN_USBBUS1;
        private const TPCANBaudrate Baudrate = TPCANBaudrate.PCAN_BAUD_500K;

        // Threading & Timing
        private MultimediaTimer _txTimer;
        private Thread _rxThread;
        private volatile bool _isRunning = false;

        // Daten für die GUI
        public ObservableCollection<CanLogEntry> ReceivedMessages { get; set; }

        // Simulations-Variablen für die Datenänderung
        private double _sineWaveAngle = 0;
        private byte _counter = 0;

        public MainWindow()
        {
            InitializeComponent();
            ReceivedMessages = new ObservableCollection<CanLogEntry>();
            DgMessages.ItemsSource = ReceivedMessages;

            // Timer initialisieren (ruft SendCyclicMessages auf)
            _txTimer = new MultimediaTimer(SendCyclicMessages);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;

            TPCANStatus status = PCANBasic.Initialize(PcanHandle, Baudrate);

            if (status != TPCANStatus.PCAN_ERROR_OK)
            {
                MessageBox.Show($"Fehler beim Initialisieren: {status}");
                return;
            }

            _isRunning = true;
            TxtStatus.Text = "Running (3 Msgs @ 10ms)";

            // RX Thread starten
            _rxThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _rxThread.Start();

            // TX Timer starten (10ms Zykluszeit)
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
            Thread.Sleep(50); // Kurz warten bis Threads auslaufen
            PCANBasic.Uninitialize(PcanHandle);
            TxtStatus.Text = "Stopped";
        }

        // --- TX: Senden der 3 Nachrichten alle 10ms ---
        // Diese Methode läuft auf dem Multimedia-Timer Thread (High Priority)
        private void SendCyclicMessages()
        {
            if (!_isRunning) return;

            // Daten generieren (damit wir Bewegung sehen)
            _counter++;
            _sineWaveAngle += 0.1;
            byte sineVal = (byte)(Math.Sin(_sineWaveAngle) * 127 + 128);

            // --- Nachricht 1: Status (ID 0x100) ---
            byte[] data1 = new byte[8];
            data1[0] = 0xAA;       // Marker
            data1[1] = _counter;   // Laufender Zähler
            SendSingleCanMessage(0x100, data1);

            // --- Nachricht 2: Analogwert Simulation (ID 0x200) ---
            byte[] data2 = new byte[8];
            data2[0] = sineVal;    // Simulierter Sensorwert
            data2[1] = (byte)(sineVal / 2);
            SendSingleCanMessage(0x200, data2);

            // --- Nachricht 3: Control Flags (ID 0x300) ---
            byte[] data3 = new byte[4]; // Nur 4 Byte Länge (DLC 4)
            data3[0] = 0x01;
            data3[1] = 0x02;
            data3[2] = 0x04;
            data3[3] = 0x08;
            SendSingleCanMessage(0x300, data3);
        }

        // Hilfsmethode zum Senden einer einzelnen Nachricht
        private void SendSingleCanMessage(uint id, byte[] data)
        {
            TPCANMsg msg = new TPCANMsg();
            msg.ID = id;
            msg.MSGTYPE = TPCANMessageType.PCAN_MESSAGE_STANDARD;

            // Die DLC (Data Length Code) bestimmt, wie viele Bytes tatsächlich gesendet werden
            msg.LEN = (byte)data.Length;

            // WICHTIG: Die Struktur benötigt für die DLL intern IMMER ein Array der Länge 8.
            // Auch wenn wir nur 4 Bytes senden (LEN=4), muss das Array im Speicher 8 Bytes haben.
            msg.DATA = new byte[8];

            // Kopiere die Nutzdaten in den 8-Byte-Puffer
            for (int i = 0; i < data.Length && i < 8; i++)
            {
                msg.DATA[i] = data[i];
            }

            // Jetzt passt das Array-Layout zur C-Struktur Definition
            TPCANStatus status = PCANBasic.Write(PcanHandle, ref msg);

            // Optional: Fehlerprüfung für Debugging
            if (status != TPCANStatus.PCAN_ERROR_OK)
            {
                // System.Diagnostics.Debug.WriteLine($"Error sending ID {id:X}: {status}");
            }
        }

        // --- RX: Empfangen ---
        private void ReceiveLoop()
        {
            TPCANMsg msg = new TPCANMsg();
            TPCANTimestamp timestamp;

            while (_isRunning)
            {
                TPCANStatus status = PCANBasic.Read(PcanHandle, out msg, out timestamp);

                if (status == TPCANStatus.PCAN_ERROR_OK)
                {
                    // Um zu verhindern, dass wir unsere EIGENEN gesendeten Nachrichten
                    // im Grid sehen (Echo), filtern wir sie hier optional aus.
                    // PCANBasic liefert standardmäßig keine TX-Echos, außer man konfiguriert es.

                    ProcessMessage(msg);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }

        private void ProcessMessage(TPCANMsg msg)
        {
            string dataString = BitConverter.ToString(msg.DATA, 0, msg.LEN).Replace("-", " ");

            var entry = new CanLogEntry
            {
                Timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),
                Id = msg.ID.ToString("X"),
                Dlc = msg.LEN,
                Data = dataString
            };

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ReceivedMessages.Insert(0, entry);
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