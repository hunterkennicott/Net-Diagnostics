using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Web;
using System.Configuration;
using System.Threading;
using System.Management;
using System.Net.Mail;

namespace Net_Diagnostics
{
    public partial class Form1 : Form
    {
        BackgroundWorker bkWorker;

        public Form1()
        {
            InitializeComponent();

            bkWorker = new BackgroundWorker();
            bkWorker.DoWork += new DoWorkEventHandler(bkWorker_DoWork);
            bkWorker.ProgressChanged += new ProgressChangedEventHandler(bkWorker_ProgressChanged);
            bkWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bkWorker_RunWorkerCompleted);
            bkWorker.WorkerReportsProgress = true;
            bkWorker.WorkerSupportsCancellation = true;

            lblStatus.Hide();
            progressBar1.Hide();
        }

        void bkWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                lblStatus.Text = "Task Cancelled";
            }
            else if (e.Error != null)
            {
                lblStatus.Text = "Error while performing background operation.";
            }
            else
            {
                lblStatus.Text = "Completed . . . . . ." + progressBar1.Value.ToString() + "%";
                MessageBox.Show("Diagnostic complete. The file is saved on the Desktop as 'Diagnostic Results'");
            }

            startButton.Enabled = true;
            btnCancel.Enabled = false;
        }

        void bkWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            progressBar1.Value = e.ProgressPercentage;
            lblStatus.Text = "Processing . . . . . ." + progressBar1.Value.ToString() + "%";
        }

        void emailDBC()
        {
            MailMessage mail = new MailMessage();
            SmtpClient SmtpServer = new SmtpClient("smtp.gmail.com");

            mail.From = new MailAddress("dbcnetdiag@gmail.com");
            mail.To.Add("hunter@dbcontrols.com");
            mail.Subject = "Connection Issues";
            mail.Body = "Attached is the diagnostic results of a network";
            //Attach txt file here


            SmtpServer.Port = 587;
            SmtpServer.Credentials = new System.Net.NetworkCredential("dbcnetdiag@gmail.com", "Dbc623Slc");
            SmtpServer.EnableSsl = true;

            SmtpServer.Send(mail);
            
        }

        void bkWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            Ping pinger = new Ping();
            //for speeds
            PingReply replyPing = pinger.Send("65.100.224.20");
            //for traceroute
            string ipAddressOrHostName = "65.100.224.20";

            //Create file on desktop and a way to write to it
            string targetFolder = Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            string targetPath = Path.Combine(targetFolder, "Diagnostic Results.txt");
            StreamWriter sw = new StreamWriter(targetPath);
            StringBuilder sb = new StringBuilder();

            sb.Append("Pinging DBC Server\r\n\r\n");
            
            //Basic info
            sb.Append("Address: " + replyPing.Address + "\r\n");
            sb.Append("Roundtrip Time: " + replyPing.RoundtripTime + "\r\n");
            sb.Append("TTL (Time to Live): " + replyPing.Options.Ttl + "\r\n");
            sb.Append("Buffer Size: " + replyPing.Buffer.Length.ToString() + "\r\n");
            sb.Append("Time: " + DateTime.Now + "\r\n\r\n");

            //Check Gateway
            ManagementClass objMC = new ManagementClass("Win32_NetworkAdapterConfiguration");
            ManagementObjectCollection objMOC = objMC.GetInstances();

            foreach (ManagementObject objMO in objMOC)
            {
                if (!(bool)objMO["ipEnabled"])
                    continue;

                string[] gateways = (string[])objMO["DefaultIPGateway"];

                //sb.AppendLine("Printing Default Gateway Info:");
                //sb.AppendLine(objMO["DefaultIPGateway"].ToString());

                sb.Append("Printing IPGateway info: ");
                foreach (string sGate in gateways)
                    sb.AppendLine(sGate);
            }

            //Check if IP is local
            sb.Append("Is the IP local: ");
            if (IsLocalIpAddress(System.Environment.MachineName) == true)
            {
                sb.Append("Yes\r\n\r\n");
            }
            else
            {
                sb.Append("No\r\n\r\n");
            }

            PingReply reply = pinger.Send(ipAddressOrHostName);

            IPAddress ipAddress = Dns.GetHostEntry(ipAddressOrHostName).AddressList[0];
            StringBuilder traceResults = new StringBuilder();

            //Start tracerouting
            using (Ping pingSender = new Ping())
            {
                PingOptions pingOptions = new PingOptions();
                Stopwatch stopWatch = new Stopwatch();
                byte[] bytes = new byte[32];

                pingOptions.DontFragment = true;
                pingOptions.Ttl = 1;
                int maxHops = 30;

                sb.AppendLine(string.Format("Tracing route to {0} over a maximum of {1} hops:", ipAddress, maxHops));
                sb.AppendLine();

                //Loop tracert and time it
                for (int i = 1; i < maxHops + 1; i++)
                {
                    stopWatch.Reset();
                    stopWatch.Start();
                    PingReply pingReply = pingSender.Send(ipAddress, 5000, new byte[32], pingOptions);

                    stopWatch.Stop();

                    sb.AppendLine(string.Format("{0}\t{1} ms\t{2}", i, stopWatch.ElapsedMilliseconds, pingReply.Address));

                    //break loop here
                    if (pingReply.Status == IPStatus.Success)
                    {
                        traceResults.AppendLine();
                        traceResults.AppendLine("Trace complete.");

                        break;
                    }

                    pingOptions.Ttl++;

                    //Show progress after each hop
                    bkWorker.ReportProgress(i);

                    if (bkWorker.CancellationPending)
                    {
                        e.Cancel = true;
                        bkWorker.ReportProgress(0);
                        return;
                    }
                }
            }

            string results = sb.ToString();
            sw.Write(sb);
            sw.Close();

            bkWorker.ReportProgress(100);
        }

        public static IPAddress GetDefaultGateway()
        {
            var card = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault();
            if (card == null) return null;
            var address = card.GetIPProperties().GatewayAddresses.FirstOrDefault();
            return address.Address;
        }

        public static bool IsLocalIpAddress(string host)
        {
            try
            {
                IPAddress[] hostIPs = Dns.GetHostAddresses(host);
                IPAddress[] localIPs = Dns.GetHostAddresses(Dns.GetHostName());

                foreach (IPAddress hostIP in hostIPs)
                {
                    if (IPAddress.IsLoopback(hostIP)) return true;
                    foreach (IPAddress localIP in localIPs)
                    {
                        if (hostIP.Equals(localIP)) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void startButton_Click(object sender, EventArgs e)
        {
            startButton.Enabled = false;
            btnCancel.Enabled = true;
            bkWorker.RunWorkerAsync();

            lblStatus.Show();
            progressBar1.Show();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            if (bkWorker.IsBusy)
            {
                bkWorker.CancelAsync();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
