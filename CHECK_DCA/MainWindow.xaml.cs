using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Media;
using System.IO.Ports;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;

using Newtonsoft.Json.Linq;
using Tulpep.NotificationWindow;

namespace CHECK_DCA
{
    public partial class MainWindow : Window
    {
        static Thread main;
        static bool window_closed = false;

        public MainWindow()
        {
            InitializeComponent();

            main = new Thread(MainThread);
            main.Start();
        }

        static void MainThread()
        {
            do
            {
                if (!first)
                {
                    Thread.Sleep(launch_frequency * 60 * 1000);
                    WriteLine($"\n--------------------\n");
                }

                completed = false;
                queue.Clear();
                ports.Clear();
                events.Clear();

                NewFile();

                InitDCAs();
                InitParseEvents();
                InitAlerts();

                StartChecking();

            } while (launch_frequency > 0 && !window_closed);
        }

        static void StartChecking()
        {
            PrepareConsole();
            WriteLine($"НАЧАЛО ПРОВЕРКИ - {DateTime.Now.ToString("HH:mm:ss")}\n");

            Thread parseThread = new Thread(Parse);
            parseThread.Start();

            last_rec = DateTime.Now;

            while (!completed)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        string indata = _serialPort.ReadExisting();

                        if (indata != "")
                        {
                            last_rec = DateTime.Now;

                            WriteLine(indata, false, false);
                            File.AppendAllText(current_path, indata);
                        }
                    }
                }
                catch (System.IO.IOException) { }
                catch (System.NullReferenceException) { }
                catch (System.InvalidOperationException) { }
            }

            first = false;
        }

        static string current_path = AppDomain.CurrentDomain.BaseDirectory + $"serial_port.data";

        static int last_line = 0;

        static DateTime last_send;
        static DateTime last_rec;

        static SerialPort _serialPort = new SerialPort();

        static List<Port> ports = new List<Port> { };

        static List<DCA> queue = new List<DCA> { };

        static Dictionary<string, string> events =
                       new Dictionary<string, string>();

        static bool first = true;
        static bool completed = false;
        static bool quit = false;

        static bool develope_mode = false;
        static bool was_problems = false;

        static bool alert_cd = true;
        static bool alert_fail = true;
        static bool alert_problem = false;
        static bool alert_result = false;

        static int additional_attempt = 0;
        static int launch_frequency = 0;

        static SoundPlayer player = new SoundPlayer(AppDomain.CurrentDomain.BaseDirectory + "alert.wav");
        static SoundPlayer warning = new SoundPlayer(AppDomain.CurrentDomain.BaseDirectory + "warning.wav");

        static double get_random_number(double minimum, double maximum)
        {
            Random random = new Random();
            return random.NextDouble() * (maximum - minimum) + minimum;
        }

        static bool ready()
        {
            if (DateTime.Now.Subtract(last_send).Seconds > 1.0 &&
                DateTime.Now.Subtract(last_rec).Seconds > 2.5)
            {
                return true;
            }

            return false;
        }

        static bool ready_to_upd()
        {
            //get_random_number(2.3, 2.5)

            if (DateTime.Now.Subtract(last_rec).Seconds > 2.5)
            {
                return true;
            }

            return false;
        }

        static bool passed_time_now(DateTime dt, double time)
        {
            return DateTime.Now.Subtract(dt).Seconds >= time;
        }

        public class Port
        {
            public string name { get; set; }
            public int baudrate { get; set; }
            public List<DCA> numbers { get; set; }
        }
        public class DCA
        {
            public string number { get; set; }

            public string serial_name { get; set; }
            public int serial_baudrate { get; set; }
        }

        public static void NewFile()
        {
            if (File.Exists(current_path))
                File.Delete(current_path);

            File.Create(current_path);
            last_line = 0;
        }

        public static void InitDCAs()
        {
            JObject jObject;

            string path = AppDomain.CurrentDomain.BaseDirectory + "settings.json";

            string settings = File.ReadAllText(path);
            jObject = JObject.Parse(settings);

            for (int i = 0; i < jObject["ports"].ToArray().Length; i++)
            {
                Port port = new Port();

                foreach (var k in JObject.FromObject(jObject["ports"][i]))
                {
                    switch (k.Key)
                    {
                        case "name":
                            port.name = k.Value.ToString();
                            break;

                        case "baudrate":
                            port.baudrate = Int32.Parse(k.Value.ToString());
                            break;
                    }
                }

                port.numbers = new List<DCA> { };

                for (int j = 0; j < jObject["ports"][i]["numbers"].ToArray().Length; ++j)
                {
                    DCA number = new DCA();

                    //number.user = jObject["ports"][i]["accounts"][j]["user"].ToString();
                    //number.password = jObject["ports"][i]["accounts"][j]["password"].ToString();
                    number.number = jObject["ports"][i]["numbers"][j].ToString();
                    number.serial_name = port.name;
                    number.serial_baudrate = port.baudrate;

                    port.numbers.Add(number);
                }

                ports.Add(port);
            }

            additional_attempt = Int32.Parse(jObject["additional_attempt"].ToString());
            launch_frequency = Int32.Parse(jObject["launch_frequency"].ToString());
        }

        public static void InitParseEvents()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "parsing_events.json";

            JObject jObject = JObject.Parse(File.ReadAllText(path));

            foreach (var text in jObject)
            {
                events.Add(text.Key.ToString(), text.Value.ToString());
            }
        }

        public static void InitAlerts()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "settings.json";

            JObject jObject = JObject.Parse(File.ReadAllText(path));

            foreach (var k in JObject.FromObject(jObject["alerts"]))
            {
                switch (k.Key)
                {
                    case "cd":
                        alert_cd = bool.Parse(k.Value.ToString());
                        break;

                    case "fail":
                        alert_fail = bool.Parse(k.Value.ToString());
                        break;

                    case "problem":
                        alert_problem = bool.Parse(k.Value.ToString());
                        break;

                    case "result":
                        alert_result = bool.Parse(k.Value.ToString());
                        break;
                }
            }
        }

        public static IEnumerable<string> GetUpdates()
        {
            while (true)
            {
                if (ready_to_upd())
                {
                    int current_line = 0;
                    string data = String.Empty;
                    string[] read_text;

                    while (true)
                    {
                        try
                        {
                            read_text = File.ReadAllLines(current_path);

                            break;
                        }
                        catch (System.IO.IOException) { }
                    }

                    foreach (string line in read_text)
                    {
                        if (current_line >= last_line || last_line == 0)
                        {
                            data += line + " \n";
                        }

                        current_line++;
                    }

                    if (current_line >= last_line)
                    {
                        last_line = current_line;

                        foreach (KeyValuePair<string, string> kvp in events)
                        {
                            if (data.Contains(kvp.Key))
                            {
                                yield return kvp.Value;

                                if (kvp.Value == "quit")
                                    quit = true;
                            }
                        }
                    }
                }

                if (quit)
                {
                    quit = false;
                    break;
                }

                yield return "";
            }
        }

        public static void Parse()
        {
            int success = 0;

            List<DCA> fail = new List<DCA> { };
            List<DCA> problems = new List<DCA> { };

            foreach (Port port in ports)
            {
                ChangeSerialPort(port.name, port.baudrate);

                foreach(DCA dca in port.numbers)
                {
                    CheckCDHolding();

                    if (!ListenDCA(dca) && !window_closed)
                    {
                        if (additional_attempt > 0)
                            queue.Add(dca);
                        else
                        {
                            if (alert_fail)
                            {
                                ShowMessageWindow($"Номер '{dca.number}' не доступен", "not_ok.png");
                                player.Play();
                            }

                            ChangeBackgroundColor(Color.FromRgb(70, 0, 0));

                            fail.Add(dca);
                        }
                    } 
                    else
                    {
                        if (was_problems)
                        {
                            if (additional_attempt > 0)
                                queue.Add(dca);
                            else
                            {
                                problems.Add(dca);

                                if (alert_problem)
                                {
                                    warning.Play();
                                    ShowMessageWindow($"Проблемы с терминалом '{dca.number}'", "warning.png");
                                }
                            }
                        } 
                        else
                        {
                            success++;
                        }
                    }

                    if (window_closed) return;
                }
            }

            if (window_closed) return;

            for (int i = 0; i < additional_attempt && queue.ToArray().Length != 0; ++i)
            {
                bool last_attempt = (i + 1 == additional_attempt);

                WriteLine("\n", false, false);
                WriteLine($"ПОПЫТКА #{(i+2).ToString()}");

                for (int j = 0, d = 0; j < queue.ToArray().Length - d; ++j)
                {
                    DCA dca = queue[j];

                    ChangeSerialPort(dca.serial_name, dca.serial_baudrate);

                    CheckCDHolding();

                    if (!ListenDCA(dca) && !window_closed)
                    {
                        if (last_attempt)
                        {
                            if (alert_fail)
                            {
                                ShowMessageWindow($"Номер '{dca.number}' не доступен", "not_ok.png");
                                player.Play();
                            }

                            ChangeBackgroundColor(Color.FromRgb(70, 0, 0));

                            fail.Add(dca);
                        }
                    }
                    else
                    {
                        if (was_problems && last_attempt)
                        {
                            problems.Add(dca);

                            if (alert_problem)
                            {
                                warning.Play();
                                ShowMessageWindow($"Проблемы с терминалом '{dca.number}'", "warning.png");
                            }
                        }
                        else
                        {
                            if (!was_problems)
                            {
                                queue.Remove(dca);
                                success++;
                                d++; j--;
                            }
                        }
                    }

                    if (window_closed) return;
                }

                if (window_closed) return;
            }

            CloseSerialPort();

            if (window_closed)
                return;

            if (fail.ToArray().Length != 0)
            {
                ChangeBackgroundColor(Color.FromRgb(70, 0, 0));

                WriteLine("\n", false, false);
                WriteLine("НЕ БЫЛО СОЕДИНЕНИЯ СО СЛЕДУЮЩИМИ НОМЕРАМИ:");

                for (int i = 0; i < fail.ToArray().Length; ++i)
                {
                    WriteLine($"  · {fail[i].number}");
                }
            }

            if (problems.ToArray().Length != 0)
            {
                if (fail.ToArray().Length == 0)
                {
                    //if (success > 0)
                        ChangeBackgroundColor(Color.FromRgb(54, 54, 0));
                    //else
                    //    ChangeBackgroundColor(Color.FromRgb(70, 0, 0));
                }

                WriteLine("\n", false, false);
                WriteLine("ВОЗНИКЛИ ПРОБЛЕМЫ СО СЛЕДУЮЩИМИ НОМЕРАМИ:");

                for (int i = 0; i < problems.ToArray().Length; ++i)
                {
                    WriteLine($"  · {problems[i].number}");
                }
            }

            string result;

            if (problems.ToArray().Length == 0 && fail.ToArray().Length == 0)
            {
                if (alert_result)
                    ShowMessageWindow("Проверка успешно завершена", "ok.png");

                ChangeBackgroundColor(Color.FromRgb(18, 81, 0));
                result = $"ПРОВЕРКА ЗАВЕРШЕНА УСПЕШНО - {DateTime.Now.ToString("HH:mm:ss")}";
            } 
            else
            {
                if (alert_result)
                {
                    if (fail.ToArray().Length == 0)
                    {
                        if (success > 0)
                            ShowMessageWindow("Проверка завершена, но были проблемы с терминалом", "enough_ok.png");
                        else
                            ShowMessageWindow("Проверка завершена неуспешно  из-за проблем с терминалом", "not_ok.png");
                    }
                    else
                    {
                        ShowMessageWindow("Проверка завершена неуспешно", "not_ok.png");
                    }
                }

                result = $"ПРОВЕРКА ЗАВЕРШЕНА - {DateTime.Now.ToString("HH:mm:ss")}";
            }

            WriteLine("\n", false, false);
            WriteLine(result);

            SaveLogging(problems, fail);

            completed = true;
        }

        public static bool ListenDCA(DCA dca)
        {
            string number = dca.number;

            string process = "start";
            string prev_process = "";

            int iteration = 0;

            WriteLine("\n", false, false);
            WriteLine($"НОМЕР --- {number} ---");

            quit = false;
            was_problems = false;

            DateTime last_upd_iteration = DateTime.Now;

            foreach (string update in GetUpdates())
            {
                switch (update)
                {
                    case "input":
                        process = "input";

                        byte[] a = Encoding.ASCII.GetBytes($"set time {DateTime.Now.ToString("HH:mm:ss")}");
                        byte[] b = GetBytes("0D");
                        byte[] c = a.Concat(b).ToArray();

                        Send(c);
                        Send(GetBytes("65 78 69 74 0D"));
                        process = "end";
                        break;

                    case "input tel":
                        process = "input tel";
                        Send(Encoding.ASCII.GetBytes(number));
                        break;

                    case "busy":
                        process = "busy";
                        break;

                    case "connected":
                        process = "connected";
                        break;

                    case "fail call":
                        process = "fail call";
                        break;

                    case "input alm":
                        process = "input alm";
                        Send(GetBytes("65 78 69 74 0D"));
                        break;


                    case "input cdr":
                        process = "input cdr";
                        Send(GetBytes("65 78 69 74 0D"));
                        break;

                    case "input edt":
                        process = "input edt";
                        Send(GetBytes("65 78 69 74 0D"));
                        break;

                    case "input sts":
                        process = "input sts";
                        Send(GetBytes("65 78 69 74 0D"));
                        break;


                    default:
                        break;
                }

                if (process == "start")
                {
                    if (iteration <= 2)
                    {
                        Send(GetBytes("0D"));
                    }
                    else
                    {
                        process = "error";
                    }
                }

                if (process == "error")
                {
                    was_problems = true;
                    WriteLine("МОДЕМ НЕ ОТВЕЧАЕТ");

                    quit = true;
                }

                if (process == "busy")
                {
                    was_problems = true;
                    WriteLine("МОДЕМ ЗАНЯТ");

                    quit = true;
                }

                if (process != "start" && process != "input tel" && process != "fail call")
                {
                    process = "end";
                    quit = true;
                } 
                else
                {
                    if (process == "fail call")
                        quit = true;
                }

                if (iteration >= 15 && passed_time_now(last_rec, 2.6))
                {
                    was_problems = true;
                    process = "not_answered";
                    WriteLine("МОДЕМ НЕ ОТВЕЧАЕТ");

                    quit = true;
                }

                if (process == prev_process)
                {
                    if (passed_time_now(last_upd_iteration, 1.0))
                    {
                        iteration++;
                        last_upd_iteration = DateTime.Now;
                    }
                }
                else
                {
                    iteration = 0;
                    last_upd_iteration = DateTime.Now;
                }

                if (window_closed)
                    return false;

                prev_process = process;
            }

            SendBreak();

            return (process == "end" || process == "error" || was_problems);
        }

        public static void Send(byte[] message)
        {
            if (_serialPort.IsOpen)
            {
                while (true)
                {
                    try
                    {
                        if (ready())
                        {
                            _serialPort.Write(message, 0, message.Length);

                            last_send = DateTime.Now;
                            break;
                        }
                    }
                    catch (Exception) { }
                }
            }
        }

        public static void SendBreak()
        {
            for (int i = 0; i < 2; ++i)
            {
                _serialPort.BreakState = true;
                Thread.Sleep(500);
                _serialPort.BreakState = false;
            }

            Thread.Sleep(1000);
        }

        public static byte[] GetBytes(string hex_string)
        {
            byte[] bytes;

            string[] hexValuesSplit = hex_string.Split(' ');

            string result = String.Empty;

            foreach (string hex in hexValuesSplit)
            {
                int value = Convert.ToInt32(hex, 16);
                string stringValue = Char.ConvertFromUtf32(value);
                char charValue = (char)value;
                result += stringValue;
            }

            bytes = Encoding.ASCII.GetBytes(result);

            return bytes;
        }

        public static string ConvertToHEX(string text)
        {
            bool first = true;

            string output = String.Empty;
            char[] values = text.ToCharArray();

            foreach (char letter in values)
            {
                if (!first) output += " ";

                int value = Convert.ToInt32(letter);
                output += $"{value:X}";

                first = false;
            }

            return output;
        }

        public static void ChangeSerialPort(string name, int baud_rate) 
        {
            CloseSerialPort();

            _serialPort = new SerialPort();
            _serialPort.PortName = name;
            _serialPort.BaudRate = baud_rate;

            int iteration = 0;

            while (true)
            {
                try
                {
                    if (window_closed)
                        break;

                    _serialPort.Open();

                    break;
                }
                catch (Exception)
                {
                    if (iteration > 5 && iteration < 7)
                    {
                        if (alert_cd)
                            ShowMessageWindow($"Нет доступа к порту '{_serialPort.PortName}'", "warning.png");

                        WriteLine($"НЕ МОГУ ОТКРЫТЬ ПОРТ '{_serialPort.PortName}'");
                        ChangeBackgroundColor(Color.FromRgb(54, 54, 0));
                        iteration++;
                    }
                    else
                    {
                        if (iteration < 6)
                            iteration++;
                    }

                    Thread.Sleep(500);
                }
            }

            if (iteration > 5)
            {
                WriteLine("ПОРТ ОТКРЫТ");
                ChangeBackgroundColor(Color.FromRgb(32, 32, 32));
            }
        }

        public static void CloseSerialPort()
        {
            int iteration = 0;

            while (true)
            {
                try
                {
                    if (window_closed)
                        break;

                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    break;
                }
                catch (Exception)
                {
                    Thread.Sleep(500);

                    if (iteration > 5 && iteration < 7)
                    {
                        if (alert_cd)
                            ShowMessageWindow($"Нет доступа к порту '{_serialPort.PortName}'", "warning.png");

                        WriteLine($"НЕ МОГУ ЗАКРЫТЬ ПОРТ '{_serialPort.PortName}'");
                        ChangeBackgroundColor(Color.FromRgb(54, 54, 0));
                        iteration++;
                    }
                    else
                    {
                        if (iteration < 6)
                            iteration++;
                    }
                }
            }

            if (iteration > 5)
            {
                WriteLine("ПОРТ ЗАКРЫТ");
                ChangeBackgroundColor(Color.FromRgb(32, 32, 32));
            }
        }

        public static void AwaitCDHolding()
        {
            while (true)
            {
                try
                {
                    if (_serialPort.CDHolding || window_closed || develope_mode)
                    {
                        break;
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(500);
                }
            }
        }

        public static void CheckCDHolding()
        {
            if (!_serialPort.CDHolding)
            {
                if (last_line != 0)
                    WriteLine("\n", false);

                WriteLine("НЕТ СОЕДИНЕНИЯ С МОДЕМОМ. ОЖИДАНИЕ СОЕДИНЕНИЯ..");
                ChangeBackgroundColor(Color.FromRgb(54, 54, 0));

                if (alert_cd)
                {
                    ShowMessageWindow("Нет соединения с модемом", "warning.png");
                    warning.Play();
                }

                AwaitCDHolding();

                if (window_closed)
                    return;

                WriteLine("СОЕДИНЕНИЕ С МОДЕМОМ ВОССТАНОВЛЕНО");
                ChangeBackgroundColor(Color.FromRgb(32, 32, 32));
            }
        }

        public static void WriteLine(string text, bool line_break=true, bool first_space=true)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                MainWindow my = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                my.consoleBox.Text += (first_space ? " " : "") + text + (line_break ? "\n" : "");
                my.scrollViewer.ScrollToBottom();
            }));
        }
    
        public static void ShowMessageWindow(string message, string image="")
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                MainWindow my = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                PopupNotifier form = new PopupNotifier();
                
                form.BodyColor = System.Drawing.Color.FromName("White");

                form.TitleFont = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);
                form.TitleColor = System.Drawing.Color.FromName("Black");
                form.TitleText = $"Проверка связи - {DateTime.Now.ToString("HH:mm:ss")}";
                form.TitlePadding = new System.Windows.Forms.Padding(10);

                form.Delay = 86400000;

                form.ContentFont = new System.Drawing.Font("Arial", 12);
                form.ContentColor = System.Drawing.Color.FromName("Black");
                form.ContentPadding = new System.Windows.Forms.Padding(10, 5, 0, 0);
                form.ContentText = message;

                if (image != "")
                {
                    form.Image = System.Drawing.Image.FromFile(AppDomain.CurrentDomain.BaseDirectory + image);
                    form.ImageSize = new System.Drawing.Size(70, 70);
                    form.ImagePadding = new System.Windows.Forms.Padding(10);
                }

                form.Popup();
            }));
        }

        public static void ChangeBackgroundColor(Color color)
        {
            Application.Current.Dispatcher.Invoke(new Action(() =>
            {
                MainWindow my = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

                LinearGradientBrush gradient = new LinearGradientBrush();
                gradient.StartPoint = new Point(0, 0);
                gradient.EndPoint = new Point(0, 1);
                gradient.GradientStops.Add(new GradientStop(Color.FromRgb(0, 0, 0), 0.0));
                gradient.GradientStops.Add(new GradientStop(color, 1.0));

                my.backgroundConsole.Background = gradient;
            }));
        }

        public static void PrepareConsole()
        {
            ChangeBackgroundColor(Color.FromRgb(32, 32, 32));
        }

        public static void SaveLogging(List<DCA> problems, List<DCA> fail)
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + $"log.txt";

            void write(string text)
            {
                File.AppendAllText(path, text);
            }

            write(DateTime.Now.ToString("\ndd.MM.yyyy|HH:mm:ss| "));

            if (problems.ToArray().Length == 0 && fail.ToArray().Length == 0)
                write("ok");
            else
            {
                if (fail.ToArray().Length > 0)
                {
                    write("fail: ");

                    for (int i = 0, f = 1; i < fail.ToArray().Length; ++i, f = 0)
                    {
                        if (f == 0)
                            write(", ");

                        write($"{fail[i].number}");
                    }

                    if (problems.ToArray().Length > 0)
                        write("; ");
                }

                if (problems.ToArray().Length > 0)
                {
                    write("problems: ");

                    for (int i = 0, f = 1; i < problems.ToArray().Length; ++i, f = 0)
                    {
                        if (f == 0)
                            write(", ");

                        write($"{problems[i].number}");
                    }
                }
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void Window_Close(object sender, RoutedEventArgs e)
        {
            CloseProgram();
        }
        
        private void Window_Minimize(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void min_button_MouseEnter(object sender, MouseEventArgs e)
        {

        }

        private void Window_Closed_1(object sender, EventArgs e)
        {
            CloseProgram();
        }

        private void CloseProgram()
        {
            window_closed = true;

            CloseSerialPort();
            
            this.Close();
            main.Abort();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Enter && completed)
            {
                CloseProgram();
            }
        }
    }
}
