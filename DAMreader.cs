using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;

public class DAMReader
{
    public class FileDataReadyEventArgs : EventArgs
    {
        public byte[] Data { get; }

        public FileDataReadyEventArgs(byte[] data)
        {
            Data = data;
        }
    }

    public event EventHandler<FileDataReadyEventArgs> FileDataReady;

    protected virtual void OnFileDataReady(FileDataReadyEventArgs e)
    {
        FileDataReady?.Invoke(this, e);
    }

    private double AFRv, RPM, MAP, rawLevel, avgLevel, rawLevelLeft, rawLevelRight, DutyC, threshold, outlev, distance, version, knockCountL, knockCountR;
    private decimal smoothing;
    private int baud = 12000000;
    private Queue<double> rpmArr = new Queue<double>();
    private Queue<double> mapArr = new Queue<double>();
    private Queue<double> afrArr = new Queue<double>();
    private Queue<double> dutyArr = new Queue<double>();
    private Queue<double> auxArr = new Queue<double>();
    public readonly SerialPort port;
    public string outBuffer = "";
    public string sensorType = "";
    public string error = "";
    public static int damErrCounter = 0;
    public static int rpmErrCounter = 0;
    public string[] parameters = new string[6];
    public bool paramsRecieved = false;
    bool connected, isDualChannel = false;
    public bool XferIncoming = false;
    public bool XferHandled = true;
    public long xferFileSize = 0;
    public string xferFileName = "";
    public int xferRemain = 0;
    public int KnockLeft, KnockRight;
    public double Amp, Q, HighFreq, LowFreq;
    public int BoreSize;
    public bool hfMode = false;
    private int[] _cylinderValues = new int[8];
    SerialDataParser parser;
    public int[] CylinderValues
    {
        get { return _cylinderValues; }
        set { _cylinderValues = value; }
    }
    public double Version
    {
        get
        {
            return version;
        }
    }
    public bool Connected
    {
        get
        {
            connected = CanRead();
            return connected;
        }
        set { connected = value; }
    }
    public bool isDecel;
    public bool pause;
    public bool isV3 = false;
    public bool isV4 = false;
    public DateTime PacketLastRecievedAt;
    public int[] ThresholdLevels;
    public bool thresholdGrabbed;
    public Action kickoffRead;

    public void Close()
    {
        if (port != null && port.IsOpen)
            port.Close();
    }

    public DAMReader(string COM, decimal Smoothing)
    {
        parser = new SerialDataParser();
        PacketLastRecievedAt = DateTime.MaxValue;
        smoothing = Smoothing;
        if (string.IsNullOrEmpty(COM))
            COM = "COM1";
        port = new SerialPort(COM, baud, Parity.None, 8, StopBits.One);
        port.Disposed += Port_Disposed;
        port.ReadTimeout = 200;
        port.WriteTimeout = 2000;

        try
        {
            var portnames = SerialPort.GetPortNames();
            bool present = portnames.Contains(port.PortName);
            if (present)
            {
                port.Open();
                damErrCounter = 0;
            }
        }
        catch (IOException ioe) { }
        catch (Exception ed)
        {
            damErrCounter += 1;
#if DEBUG
            error = ed.ToString();
#endif
        }

        if (port != null && port.IsOpen)
        {
            byte[] buffer = new byte[1024];
            kickoffRead = delegate
            {
                try
                {
                    port.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                    {
                        if (port.IsOpen)
                        {
                            if (!(port != null && port.IsOpen))
                                kickoffRead();

                            try
                            {
                                int actualLength = port?.BaseStream.EndRead(ar) ?? 0;
                                byte[] received = new byte[actualLength];
                                Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                                raiseAppSerialDataEvent(received);
                            }
                            catch { }
                        }

                        kickoffRead();
                    }, null);
                }
                catch (IOException io) { }
                catch (Exception e) { }
            };
            kickoffRead();
        }
    }

    private void handleAppSerialError(IOException exc)
    {
        Connected = false;
    }

    private void raiseAppSerialDataEvent(byte[] received)
    {
        PacketLastRecievedAt = DateTime.Now;
        if (!threadCultureSet)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            threadCultureSet = true;
        }

        if (damErrCounter > 80)
        {
            this.Connected = false;
            return;
        }

        if (port.IsOpen)
        {
            try
            {
                string s = Encoding.ASCII.GetString(received);

                if (s.Contains("XFER"))
                {
                    XferIncoming = true;
                    try
                    {
                        var startingIndex = s.IndexOf("TKMXFER") + 7;
                        XferIncoming = true;
                        XferHandled = false;
                        xferFileSize = long.Parse(s.Substring(startingIndex, 9));
                        xferRemain = (int)xferFileSize;
                        int endingIndex = s.IndexOf("TK", startingIndex + 9);
                        xferFileName = s.Substring(startingIndex + 9, s.IndexOf(" ", startingIndex + 9) - (startingIndex + 9));
                        Console.Beep();
                    }
                    catch (Exception dxs) { }
                }

                if (s == null || !s.Contains("TKM"))
                {
                    return;
                }

                s = s.Replace("\n", "");
                s = s.Replace("\r", "");
            }
            catch (Exception dsa)
            {
                error = dsa.ToString();
                damErrCounter += 1;
                return;
            }
        }

        if (port.IsOpen)
            port.DiscardInBuffer();

        if (s == null)
            return;

        parser.ParseSerialData(s);
        UpdateValuesFromParser();
    }

    private void UpdateValuesFromParser()
    {
        MAP = parser.TKMBST;
        mapArr.Enqueue(MAP);
        if (mapArr.Count > smoothing)
            mapArr.Dequeue();
        MAP = mapArr.Average();

        RPM = parser.TKMRPM;
        rawLevelLeft = parser.TKMPKL;
        rawLevelRight = parser.TKMPKR;
        KnockRight = (int)parser.TKMKNKR;
        KnockLeft = (int)parser.TKMKNKL;
        DutyC = parser.TKMDUTY;
        dutyArr.Enqueue(DutyC);
        if (dutyArr.Count > 5)
            dutyArr.Dequeue();
        DutyC = dutyArr.Average();
        isDualChannel = parser.TKMISDUAL == 1;
        rawLevel = parser.TKMSND;
        hfMode = parser.TKMHFMODE == 1;
        Amp = parser.AMP_LEVEL;
        Q = parser.TKMQ;
        HighFreq = parser.TKMUpperHz;
        LowFreq = parser.TKMLowerHz;
        BoreSize = (int)parser.TKMBORE;
        sensorType = (int)parser.TKM5VTYPE == 0 ? "KPA" : parser.TKM5VTYPE == 1 ? "AFR" : "EGT";
        _cylinderValues[0] = (int)parser.TKMKCCYL1;
        _cylinderValues[1] = (int)parser.TKMKCCYL2;
        _cylinderValues[2] = (int)parser.TKMKCCYL3;
        _cylinderValues[3] = (int)parser.TKMKCCYL4;
        _cylinderValues[4] = (int)parser.TKMKCCYL5;
        _cylinderValues[5] = (int)parser.TKMKCCYL6;
        _cylinderValues[6] = (int)parser.TKMKCCYL7;
        _cylinderValues[7] = (int)parser.TKMKCCYL8;
        knockCountL = parser.TKMKNKCNTL;
        knockCountR = parser.TKMKNKCNTR;
        version = parser.TKMVER;
    }

    private void Port_Disposed(object sender, EventArgs e) { }

    private void Port_ErrorReceived(object sender, SerialErrorReceivedEventArgs e) { }

    public bool CanRead()
    {
        try
        {
            if (port != null)
            {
                var open = port.IsOpen;
                if (!open)
                    this.Connected = false;
                return port?.IsOpen ?? false;
            }
            else
                return false;
        }
        catch
        {
            return false;
        }
    }

    bool threadCultureSet = false;
    int readLimitCounter = 0;
    const int readLimit = 3;
    byte[] buff = new byte[1024];
    private void port_DataReceived(object sender, SerialDataReceivedEventArgs e) { }

    public void Flush()
    {
        if (lastFlush != DateTime.MaxValue && DateTime.Now < lastFlush.AddMilliseconds(100))
            return;
        WriteThread();
        lastFlush = DateTime.Now;
    }

    private async void WriteThread()
    {
        return;
        try
        {
            var buffer = Encoding.ASCII.GetBytes(outBuffer);
            Thread.Sleep(50);
            Stream str = port.BaseStream;
            await port.BaseStream.WriteAsync(buffer, 0, buffer.Length);
            port.BaseStream.Flush();
            outBuffer = "";
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch { }
    }

    decimal RollingAvg(decimal newval, decimal avg)
    {
        if (isV3)
            return (newval + avg * 3) / 4;

        avg = (newval + smoothing * avg) / (smoothing + 1);
        return avg;
    }

    double WeightedAvg(Queue<double> _queue)
    {
        double aggregate = 0;
        double weight;

        int count = _queue.Count();

        for (double i = 1; i <= count; i++)
        {
            weight = i / (double)count;
            aggregate += _queue.ElementAt((int)i - 1) * weight;
        }
        return aggregate / count;
    }

    public static class DamHelper
    {
        public static bool RPMJumped = false;
    }

    public double GetRPM(ref double _lastRPM)
    {
        if (Connected)
            return Math.Round(((int)RPM) / 5.0) * 5;
        else
        {
            DutyC = MAP = AFRv = 0.0;
            return 0;
        }
    }

    public double GetDuty()
    {
        return DutyC;
    }

    public double GetSND()
    {
        double ret = (double)rawLevel;
        return (double)avgLevel;
    }

    public bool IsDual()
    {
        return isDualChannel;
    }

    public double GetSNDL()
    {
        double ret = (double)rawLevelLeft;
        return ret;
    }

    public double GetSNDR()
    {
        double ret = (double)rawLevelRight;
        return ret;
    }

    public double GetDistance()
    {
        return distance;
    }

    public double GetOutLevel()
    {
        return outlev;
    }

    public double GetKnockCountL()
    {
        return (double)knockCountL;
    }

    public double GetKnockCountR()
    {
        return (double)knockCountR;
    }

    public double GetThresh()
    {
        return (double)threshold;
    }

    public int[] GetThresholds()
    {
        return ThresholdLevels;
    }

    public double GetMAP()
    {
        if (port == null || !port.IsOpen)
            return -1;
        return MAP;
    }

    public double GetAFRv()
    {
        if (port == null || !port.IsOpen)
            return -1;
        return AFRv;
    }

    public class SerialDataParser
    {
        public double TKMSND { get; private set; }
        public double TKMPKL { get; private set; }
        public double TKMPKR { get; private set; }
        public double TKMKNKR { get; private set; }
        public double TKMKNKL { get; private set; }
        public double TKMKNKCNTL { get; private set; }
        public double TKMKNKCNTR { get; private set; }
        public double TKMVER { get; private set; }
        public double TKMISDUAL { get; private set; }
        public double TKMDUTY { get; private set; }
        public double TKMDUTYL { get; private set; }
        public double TKM5VTYPE { get; private set; }
        public double TKMThreshL { get; private set; }
        public double TKMThreshR { get; private set; }
        public double TKMThreshM { get; private set; }
        public double TKMKCCYL1 { get; private set; }
        public double TKMKCCYL2 { get; private set; }
        public double TKMKCCYL3 { get; private set; }
        public double TKMKCCYL4 { get; private set; }
        public double TKMKCCYL5 { get; private set; }
        public double TKMKCCYL6 { get; private set; }
        public double TKMKCCYL7 { get; private set; }
        public double TKMKCCYL8 { get; private set; }
        public double TKMKDEG { get; private set; }
        public double TKMBST { get; private set; }
        public double TKMRPM { get; private set; }
        public double AMP_LEVEL { get; private set; }
        public double TKMHFMODE { get; private set; }
        public double TKMQ { get; private set; }
        public double TKMUpperHz { get; private set; }
        public double TKMLowerHz { get; private set; }
        public double TKMBORE { get; private set; }

        public void ParseSerialData(string serialData)
        {
            var properties = GetType().GetProperties();
            foreach (var property in properties)
            {
                var regexPattern = $@"{property.Name}\s?([-+]?\d*\.?\d+)";
                var match = Regex.Match(serialData, regexPattern);
                if (match.Success)
                {
                    double value = double.Parse(match.Groups[1].Value);
                    property.SetValue(this, value);
                }
            }
        }
    }
}
