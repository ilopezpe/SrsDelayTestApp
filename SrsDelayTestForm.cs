using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// this app reads units (degree), and requests an arduino to move a motor some # of steps.
// requires polarizerControl software to be installed on an arduino
//
namespace StepperApp
{
    public partial class SrsDelayTestApp : Form
    {
        #region Prologix Constants 
        private const string PrologixAddress = "++addr";
        private const string PrologixIFC = "++ifc";
        private const string PrologixMode = "++mode 1";
        private const string PrologixEOI = "++eoi 1";
        private const string PrologixEOS = "++eos 0"; // 0 = CRLF termination
        private const string PrologixRead = "++read";
        #endregion

        private int Timeout { get; set; } = 500;

        public SrsDelayTestApp()
        {
            InitializeComponent();
        }

        // initializes the form
        private void SrsDelayTestApp_Load(object sender, EventArgs e)
        {
            getPorts();

            groupBoxControl.Enabled = false;
            foreach (var address in Enumerable.Range(0, 31)) comboBoxGpibID.Items.Add(address);
            comboBoxGpibID.SelectedItem = 15;
            buttonPort.Focus();
        }

        #region Com Port Controls

        // populates com ports
        private void getPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                comboBoxPorts.Items.Add(port);
            }
        }
        // carefully close port 
        private void ClosePort()
        {

            // update panels
            textBoxStatus.AppendText("Disconnected");

            buttonPort.Text = "Open Port";
            buttonPort.BackColor = Color.Lime;
            buttonPort.Focus();

            comboBoxPorts.Enabled = true;
            buttonRefreshPorts.Enabled = true;
            groupBoxControl.Enabled = false;

            serialPort1.Close();
            serialPort1.Dispose();
        }

        // refreshes available ports
        private void buttonRefreshPorts_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen == false)
            {
                comboBoxPorts.Items.Clear();
                getPorts();
            }
        }
        #endregion

        // This opens the serial port if it is available
        private void buttonPort_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen == false && comboBoxPorts.SelectedIndex != -1)
            {
                #region Serial Port Config
                serialPort1.PortName = comboBoxPorts.Text;
                serialPort1.BaudRate = 9600; // baud rate does not matter
                serialPort1.Parity = Parity.None;
                serialPort1.DataBits = 8;
                serialPort1.StopBits = StopBits.One;
                serialPort1.Handshake = Handshake.RequestToSend;
                serialPort1.RtsEnable = true;
                serialPort1.DtrEnable = true;
                serialPort1.Encoding = Encoding.ASCII;
                // for error handling 
                serialPort1.DiscardNull = false;
                serialPort1.ParityReplace = 0;
                //Timeout settings
                serialPort1.ReadTimeout = Timeout;
                serialPort1.WriteTimeout = Timeout;
                #endregion

                try
                {
                    serialPort1.Open();
                    serialPort1.DiscardOutBuffer();
                    serialPort1.DiscardInBuffer();

                    buttonPort.Text = "Close Port";
                    buttonPort.BackColor = SystemColors.Control;

                    comboBoxPorts.Enabled = false;
                    buttonRefreshPorts.Enabled = false;
                    groupBoxControl.Enabled = true;
                    buttonSend.Focus();

                    // set up prologix controller
                    SendTX(PrologixIFC); // Make Prologix Controller-In-Charge
                    SendTX(PrologixMode); // Use CONTROLLER mode
                    SendTX(PrologixEOI); // Assert EOI after transmit GPIB data.
                    SendTX(PrologixEOS); // Use CRLF as GPIB terminator.
                    SendTX("++auto 1"); // Enable read after write.
                    SendTX("++read_tmo_ms " + Timeout); // Set readout for manual 
                    SendTX("++ver");
                }
                catch
                {
                    textBoxStatus.AppendText("Prologix: Device Not Available");
                }
            }
            else
            {
                try
                {
                    ClosePort();
                }
                catch (IOException ex)
                {
                    textBoxStatus.AppendText(ex.Message);
                }
            }
        }

        private delegate void InvokeDelegate(string s);
        private async void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            string RXbuffer = serialPort1.ReadExisting();
            {
                this.BeginInvoke(new InvokeDelegate(UpdateStatus), new object[] { RXbuffer });
            }
            await Task.Delay(100);
        }

        private void UpdateStatus(string RXbuffer)
        {
            textBoxStatus.AppendText(RXbuffer);
        }

        /// <summary>
        /// This function sends serial commands to the prologix controller.
        /// If commands begin with ++ they are sent to the controller.
        /// Otherwise, commands are relayed to the gpib instrument.
        /// </summary>
        /// <param name="command"></param>
        private void SendTX(string command)
        {
            string TX;
            if (command[0] == (char)43) // send to prologix 
            {
                TX = command + "\r\n";
            }
            else
            {
                TX = EscapeString(command) + "\r\n"; // send to instrument
            }

            textBoxStatus.AppendText("> " + command + "\r\n");
            try
            {
                if (!serialPort1.IsOpen) serialPort1.Open();
                serialPort1.Write(TX);
            }
            catch (IOException ex)
            {
                textBoxStatus.AppendText(ex.Message);
                ClosePort();
            }
        }

        /// <summary>
        /// This function escapes the GPIB command string with ascii null characters for use with Prologix controller.
        /// CR (13), LF (10), ESC (27), + (43) characters will also be escaped.
        /// </summary>
        /// <param name="s">GPIB command</param>
        /// <returns>Null-escaped string</returns>
        string EscapeString(string s)
        {
            StringBuilder builder = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == (char)10 || s[i] == (char)13 || s[i] == (char)27 || s[i] == (char)43)
                {
                    builder.Append((char)27); // escape
                }
                builder.Append(s[i]);
                builder.Append('\0'); // Hack/workaround for every-other-character problem.
            }
            return builder.ToString();
        }

        #region Background workers 
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            if (!serialPort1.IsOpen) serialPort1.Open();
            serialPort1.Write("++auto 0\r\n");

            for (int i = 0; i < 10; i++)
            {
                double Delay;
                if (i % 2 == 0)
                {
                    Delay = 1.5E-5;
                }
                else
                {
                    Delay = 2.5E-3;
                }

                serialPort1.DiscardInBuffer();
                serialPort1.DiscardOutBuffer();

                string WriteDelayCommand = "DT 2,1," + Delay.ToString();
                serialPort1.Write(EscapeString(WriteDelayCommand) + "\r\n"); //"\r\n" ???

                string ReadDelayCommand = "DT 2";
                serialPort1.Write(EscapeString(ReadDelayCommand) + "\r\n");
                serialPort1.Write("++read\r\n");
                Thread.Sleep(100);

                backgroundWorker1.ReportProgress(0, serialPort1.ReadExisting());
            }
            serialPort1.Write("++auto 1\r\n");
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            textBoxStatus.AppendText(e.UserState as string);
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            groupBoxControl.Enabled = true;
        }

        #endregion 

        private void buttonTestSequence_Click(object sender, EventArgs e)
        {
            groupBoxControl.Enabled = false;
            //BackgroundWorker backgroundWorker1 = new BackgroundWorker();
            backgroundWorker1.WorkerReportsProgress = true;
            backgroundWorker1.WorkerSupportsCancellation = true;
            backgroundWorker1.DoWork += backgroundWorker1_DoWork;
            backgroundWorker1.ProgressChanged += backgroundWorker1_ProgressChanged;
            backgroundWorker1.RunWorkerCompleted += backgroundWorker1_RunWorkerCompleted;
            backgroundWorker1.RunWorkerAsync();
        }

        private void buttonSend_Click(object sender, EventArgs e)
        {
            string sendText = textBox1.Text;
            if (string.IsNullOrEmpty(sendText) == false)
            {
                SendTX(sendText); // Raw Data send//"\r\n" ???
            }
        }

        // quit application
        private void SrsDelayTestApp_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (serialPort1.IsOpen)
            {
                ClosePort();
            }
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
