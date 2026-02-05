using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NotepadppIoC
{
    public partial class Form1 : Form
    {
        private static IocCollection IocCollection;

        private BindingList<IoC> _ioCs = new BindingList<IoC>();
        private readonly BindingSource _bindingSource  = new BindingSource();
        private static int _scanned = 0;
        private static int _bad = 0;
        private Random Random = new Random();
        private Stopwatch _watch = new Stopwatch();
        public Form1()
        {
            InitializeComponent();
            this.dataGridView1.CellFormatting += new DataGridViewCellFormattingEventHandler(DataGridView1_CellFormatting);
            this.dataGridView1.RowPostPaint += DataGridView1_RowPostPaint;
            this._bindingSource.DataSource = this._ioCs;
            this.dataGridView1.DataSource = this._bindingSource;
            this.dataGridView1.AutoGenerateColumns = true;

            this.checkedListBox1.Items.AddRange(
                new string[] { "Files", "Registry", "Mutex", "Services" }
                );

            for (int i = 0; i < this.checkedListBox1.Items.Count; i++)
            {
                this.checkedListBox1.SetItemChecked(i, true);
            }

            this.LoadIocFile();
            this.InsertAllUserDirectories();
        }

        private void LoadIocFile()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string filePath = Path.Combine(Path.GetDirectoryName(exePath),"IoC.json");

            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    throw new FileNotFoundException("IoC.json is required", filePath);
                }
                string jsonContent = File.ReadAllText(filePath);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                IocCollection = serializer.Deserialize<IocCollection>(jsonContent);
            }catch(Exception ex)
            {
                System.IO.File.WriteAllText(Path.Combine(Path.GetDirectoryName(exePath), "err.txt"), ex.Message);
                throw ex;
            }
        }

        private void InsertAllUserDirectories()
        {
            try
            {
                string currentUserProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                DirectoryInfo usersFolder = Directory.GetParent(currentUserProfilePath);
                List<string> iocDirectories = new List<string>();

                if (usersFolder != null)
                {
                    string[] userDirectories = Directory.GetDirectories(usersFolder.FullName);
                    List<string> users = new List<string>();
                    string currentUser = new DirectoryInfo(currentUserProfilePath).Name;

                    foreach (string dir in userDirectories)
                    {
                        DirectoryInfo dirInfo = new DirectoryInfo(dir);
                        if (string.Compare(dirInfo.Name, currentUser, true) != 0)
                            users.Add(dirInfo.Name);
                    }

                    foreach (string ioc in IocCollection.Paths)
                    {
                        var cleanPath = this.CleanPath(ioc);

                        if (cleanPath.Contains(currentUser))
                        {
                            foreach(var user in users)
                            {
                                iocDirectories.Add(cleanPath.Replace(currentUser, user));
                            }
                        }
                    }

                    IocCollection.Paths.AddRange(iocDirectories);
                }
            }
            catch (UnauthorizedAccessException uae)
            {
                this.Write($"Access denied. Try running the application with elevated privileges: {uae.Message}");
            }
            catch (Exception e)
            {
                this.Write($"An error occurred: {e.Message}");
            }
        }
    
        private string CleanPath(string path)
        {
            var cleanPath = path;

            if (path.Contains("%"))
            {
                var parts = path.Split('\\');
                string expandedValue = "";

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (part.Contains("%"))
                    {
                        parts[i] = part.Replace("%", "");
                        var rawValue = Environment.GetEnvironmentVariable(parts[i]);
                        expandedValue = Environment.ExpandEnvironmentVariables(rawValue);
                        parts[i] = expandedValue;
                    }
                }

                cleanPath = parts.Length > 1 ? string.Join(Path.DirectorySeparatorChar.ToString(), parts) : expandedValue;
            }

            return cleanPath;
        }

        private async void Button1_Click(object sender, EventArgs e)
        {
            _scanned = 0;
            _bad = 0;
            this._ioCs.Clear();
            this.Write(" ");
            bool shoScanned = this.checkBox1.Checked;

            if (!string.IsNullOrWhiteSpace(this.textBox1.Text) && !Directory.Exists(this.textBox1.Text))
            {
                MessageBox.Show("Selected directory does not exist.\nLeave blank for default or use valid path.\nEnvironment paths like %appdata% are not allowed.\nUse full path or browse.");
                return;
            }

            this.EnableUi(false);

            this._watch.Reset();
            this._watch.Start();

            string[] selectedPaths = !string.IsNullOrWhiteSpace(this.textBox1.Text) ? new string[] { this.textBox1.Text } : IocCollection.Paths.ToArray();
            List<Task> tasks = new List<Task>();


            if (this.checkedListBox1.CheckedItems.Contains("Files"))
            {
                tasks.Add(Task.Run(() =>
                {
                    this.Write("checking files");
                    this.CheckFiles(selectedPaths, shoScanned);
                }));
            }

            if (this.checkedListBox1.CheckedItems.Contains("Mutex"))
            {
                tasks.Add(Task.Run(() =>
                {
                    this.Write("checking mutex");
                    bool? mutextFound = OpeNMutex();
                    string message = mutextFound.HasValue ? (mutextFound.Value ? "MUTEX found" : "Not found") : "Unknown: Insufficient permissions";

                    if ((mutextFound.HasValue && mutextFound.Value) || shoScanned)
                    {
                        var ioc = new IoC() { Path = "Global\\Jdhfv_1.0.1", Issue = mutextFound, Message = message };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                    }

                    Interlocked.Increment(ref _scanned);
                    if ((mutextFound.HasValue && mutextFound.Value) || !mutextFound.HasValue)
                    {
                        Interlocked.Increment(ref _bad);
                    }
                }));
            }

            if (this.checkedListBox1.CheckedItems.Contains("Registry"))
            {
               tasks.Add(Task.Run(() =>
                {
                    this.Write("checking registry");

                    using (RegistryKey baseKey = Registry.CurrentUser.OpenSubKey(@"Software"))
                    {
                        if (baseKey != null)
                        {
                            EnumerateSubKeysRecursive(baseKey, shoScanned);
                        }
                    }

                    using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(@"Software"))
                    {
                        if (baseKey != null)
                        {
                            EnumerateSubKeysRecursive(baseKey, shoScanned);
                        }
                    }
                }));
            }

            if (this.checkedListBox1.CheckedItems.Contains("Services"))
            {
                tasks.Add(Task.Run(() =>
                {
                    this.Write("checking services");

                    this.CheckServices(shoScanned);
                }));
            }

            if (tasks.Count < 1)
            {
                MessageBox.Show("Must select at least one area to scan");
                return;
            }

            await Task.WhenAll(tasks);

            this._watch.Stop();

            this.Write($"Done. {_scanned:N0} scanned. {_bad:N0} issues. Completion took {_watch.Elapsed:G}");

            this.EnableUi(true);

            if (_bad > 0)
            {
                var backingList = new List<IoC>(this._ioCs);

                backingList.Sort(new IocIssueComparer());
                //backingList.Sort((x, y) => (!x.Issue).CompareTo(!y.Issue));

                this._ioCs.Clear();

                foreach (var item in backingList)
                    this._ioCs.Add(item);
            }
        }

        private void EnableUi(bool enabled)
        {
            this.button1.Enabled = enabled;
            this.button2.Enabled = enabled;
            this.checkBox1.Enabled = enabled;
            this.textBox1.Enabled = enabled;
            this.checkedListBox1.Enabled = enabled;
        }

        private void EnumerateSubKeysRecursive(RegistryKey key, bool showScanned)
        {
            try
            {
                this.Write($"Key: {key.Name}"); 
                
                foreach (string valueName in key.GetValueNames())
                {
                    object valueData = key.GetValue(valueName);
                    string displayName = string.IsNullOrEmpty(valueName) ? "(Default)" : valueName;
                    string value = Convert.ToString(valueData);

                    Interlocked.Increment(ref _scanned);

                    if (!string.IsNullOrWhiteSpace(value) 
                        && (
                            (value.ToLowerInvariant().Contains("bluetooth") 
                                && value.ToLowerInvariant().Contains("-i"))
                            || 
                            (value.ToLowerInvariant().Contains("bluetooth")
                                && value.ToLowerInvariant().Contains("-k"))
                            )
                    )
                    {
                        var ioc = new IoC() { Path = $"{key}/{valueName}", Issue = true, Message = "Bluetooth service malicious arguments" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });

                        Interlocked.Increment(ref _bad);
                    }
                    else if (showScanned)
                    {
                        var ioc = new IoC() { Path = $"{key}/{valueName}", Issue = false, Message = "Not found" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                    }
                }

                string[] subKeyNames = key.GetSubKeyNames();

                foreach (string subKeyName in subKeyNames)
                {
                    using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                    {
                        if (subKey != null)
                        {
                            EnumerateSubKeysRecursive(subKey, showScanned);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Write($"Error accessing key {key.Name}: {ex.Message}");
                var ioc = new IoC() { Path = $"{key}", Issue = null, Message = "Error accessing key" };
                this.dataGridView1.InvokeIfRequired(d =>
                {
                    this._ioCs.Add(ioc);
                });
            }
        }

        private void CheckFiles(string[] paths, bool showScanned)
        {
            Parallel.ForEach(paths, path =>
            {
                this.Write($"Checking path {path}");
                var cleanPath = this.CleanPath(path);

                if (!Directory.Exists(cleanPath))
                {
                    if (showScanned)
                    {
                        var ioc = new IoC() { Path = cleanPath, Issue = false, Message = "Not found" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                    }
                    Interlocked.Increment(ref _scanned);
                    return;
                }

                var files = SafeFilenameWalker.EnumerateFiles(cleanPath, true);// Directory.EnumerateFiles(cleanPath, "*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var sha1 = "";
                    var sha256 = "";
                    var isFound = false;

                    this.Write($"Checking path {file}");

                    try
                    {
                        sha1 = GetSha1Hash(file);
                    }
                    catch (Exception)
                    {
                        var ioc = new IoC() { Path = file, Issue = null, Message = "Cannot access file", Attack = "unknown", Sha = "unknown" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                        Interlocked.Increment(ref _bad);
                    }
                    try
                    {
                        sha256 = GetSha256Hash(file);
                    }
                    catch (Exception)
                    {
                        var ioc = new IoC() { Path = file, Issue = null, Message = "Cannot access file", Attack = "unknown", Sha = "unknown" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                        Interlocked.Increment(ref _bad);
                    }
                    string sha1fail = "";
                    if (!string.IsNullOrWhiteSpace(sha1) && IocCollection.Shas.TryGetValue(sha1, out sha1fail))//) || Random.Next(1, 100) == 5
                    {
                        var ioc = new IoC() { Path = file, Issue = true, Message = "SHA1 Found!", Attack = sha1fail, Sha = sha1 };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                        Interlocked.Increment(ref _bad);
                    }
                    if (!string.IsNullOrWhiteSpace(sha256) && IocCollection.Shas.TryGetValue(sha256, out var sha256fail))
                    {
                        var ioc = new IoC() { Path = file, Issue = true, Message = "SHA256 Found!", Attack = sha256fail, Sha = sha256 };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                        Interlocked.Increment(ref _bad);
                    }

                    if (!isFound)
                    {
                        if (showScanned)
                        {
                            var ioc = new IoC() { Sha = sha256, Path = file, Issue = false, Message = "SHA not found" };
                            this.dataGridView1.InvokeIfRequired(d =>
                            {
                                this._ioCs.Add(ioc);
                            });
                        }
                        Interlocked.Increment(ref _scanned);
                    }
                }
            });
        }

        private void CheckServices(bool showScanned)
        {
            ServiceController[] services = ServiceController.GetServices();

            try
            {

                foreach (ServiceController service in services)
                {
                    string displayName = service.DisplayName;
                    string serviceName = service.ServiceName;
                    ServiceControllerStatus status = service.Status;
                    bool outputDone = false;
                    string query = String.Format("SELECT PathName FROM Win32_Service WHERE Name = '{0}'", serviceName);
                    string pathName = string.Empty;

                    this.Write($"Checking {displayName}");

                    try
                    {
                        using (ManagementObjectSearcher mos = new ManagementObjectSearcher(query))
                        {
                            foreach (ManagementObject mo in mos.Get())
                            {
                                pathName = mo["PathName"]?.ToString() ?? "null";
                                break;
                            }
                        }

                        if (pathName.ToLowerInvariant().Contains("bluetooth"))
                        {
                            if (pathName.ToLowerInvariant().Contains("-i") || pathName.ToLowerInvariant().Contains("-k"))
                            {
                                var ioc = new IoC() { Path = $"Service: {displayName}", Issue = true, Message = $"Possible malicious service calling: {pathName}" };
                                this.dataGridView1.InvokeIfRequired(d =>
                                {
                                    this._ioCs.Add(ioc);
                                });
                                outputDone = true;
                                Interlocked.Increment(ref _bad);
                            }
                        }

                        if (showScanned && !outputDone)
                        {
                            var ioc = new IoC() { Path = $"Service: {displayName}", Issue = false, Message = "Not suspicious" };
                            this.dataGridView1.InvokeIfRequired(d =>
                            {
                                this._ioCs.Add(ioc);
                            });
                        }
                    }
                    catch (Exception)
                    {
                        var ioc = new IoC() { Path = $"Service: {displayName}", Issue = null, Message = "Error scanning" };
                        this.dataGridView1.InvokeIfRequired(d =>
                        {
                            this._ioCs.Add(ioc);
                        });
                    }

                    Interlocked.Increment(ref _scanned);
                }

            }
            catch(Exception)
            {
                var ioc = new IoC() { Path = $"Services", Issue = null, Message = "Error iterating services" };
                this.dataGridView1.InvokeIfRequired(d =>
                {
                    this._ioCs.Add(ioc);
                });
                Interlocked.Increment(ref _scanned);
            }
        }

        private void CheckEvents()
        {
            string logName = "Application";
            EventLog eventLog = new EventLog(logName);

            Console.WriteLine($"Reading entries from the '{logName}' log:");

            Parallel.ForEach(eventLog.Entries as IEnumerable<EventLogEntry>, entry =>
            {

            });

            // Iterate through all entries in the log
            foreach (EventLogEntry entry in eventLog.Entries)
            {
                Console.WriteLine($"Time: {entry.TimeWritten}");
                Console.WriteLine($"Source: {entry.Source}");
                Console.WriteLine($"Entry Type: {entry.EntryType}");
                Console.WriteLine($"Event ID: {entry.EventID}");
                Console.WriteLine($"Message: {entry.Message}");
                Console.WriteLine(new string('-', 20));
            }
        }

        private void Write(string message)
        {
            Console.WriteLine(message);
            this.label1.InvokeIfRequired(l => l.Text = message);
        }

        private void DataGridView1_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.Value != null)
            {
                IoC ioC = this.dataGridView1.Rows[e.RowIndex].DataBoundItem as IoC;

                if (ioC is null)
                    throw new NullReferenceException($"row {e.RowIndex} was not IoC");

                if (ioC.Issue.HasValue && ioC.Issue.Value)
                {
                    this.dataGridView1.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LightCoral;
                }
                else if (ioC.Issue.HasValue && !ioC.Issue.Value)
                {
                    this.dataGridView1.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.LightGreen;
                }
            }
        }

        public static string GetSha1Hash(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(bs);
                StringBuilder formattedHash = new StringBuilder();

                // Convert the byte array to a hexadecimal string
                foreach (byte b in hash)
                {
                    formattedHash.Append(b.ToString("X2"));
                }

                return formattedHash.ToString();
            }
        }
        
        public static string GetSha256Hash(string filePath)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (BufferedStream bs = new BufferedStream(fs))
            using (SHA256 sha1 = SHA256.Create())
            {
                byte[] hash = sha1.ComputeHash(bs);
                StringBuilder formattedHash = new StringBuilder();

                // Convert the byte array to a hexadecimal string
                foreach (byte b in hash)
                {
                    formattedHash.Append(b.ToString("X2"));
                }

                return formattedHash.ToString();
            }
        }


        private void DataGridView1_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            string rowNumber = (e.RowIndex + 1).ToString();
            StringFormat format = new StringFormat()
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Center
            };

            Rectangle bounds = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, this.dataGridView1.RowHeadersWidth, e.RowBounds.Height);

            TextRenderer.DrawText(e.Graphics, rowNumber, this.Font, bounds, this.dataGridView1.RowHeadersDefaultCellStyle.ForeColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        private FolderBrowserDialog browserDialog = new FolderBrowserDialog();
        private void Button2_Click(object sender, EventArgs e)
        {
            if (this.browserDialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(browserDialog.SelectedPath))
            {
                this.textBox1.Text = this.browserDialog.SelectedPath;
            }
        }

        private bool? OpeNMutex()
        {
            string mutexName = "Global\\Jdhfv_1.0.1";
            bool? found = false;

            try
            {
                using (Mutex mutex = Mutex.OpenExisting(mutexName))
                {
                    found = true;
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                found = false;
            }
            catch (UnauthorizedAccessException)
            {
                found = null;
            }

            return found;
        }
    }
}
