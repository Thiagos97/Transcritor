// Transcritor: janela para escolher audios e transcrever com o Whisper.
// A transcricao roda no worker.py (embutido neste .exe) com o Python da maquina.
// Compilar com build.ps1 (usa o csc.exe do .NET Framework, C# 5).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Transcritor")]
[assembly: AssemblyProduct("Transcritor")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace Transcritor
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    class ModelOption
    {
        public string Name;
        public string File;
        public string Description;
        public string Size;

        public ModelOption(string name, string file, string description, string size)
        {
            Name = name; File = file; Description = description; Size = size;
        }

        public bool IsCached()
        {
            string cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrEmpty(cache))
                cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return System.IO.File.Exists(Path.Combine(Path.Combine(cache, "whisper"), File));
        }

        public override string ToString()
        {
            return Name + " — " + Description + (IsCached() ? " (já baixado)" : " (baixa " + Size + " na 1ª vez)");
        }
    }

    class AudioItem
    {
        public string Path;
        public AudioItem(string path) { Path = path; }
        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path) + "     (" + System.IO.Path.GetDirectoryName(Path) + ")";
        }
    }

    class LanguageOption
    {
        public string Code;
        public string Label;
        public LanguageOption(string code, string label) { Code = code; Label = label; }
        public override string ToString() { return Label; }
    }

    class MainForm : Form
    {
        static readonly string[] AudioExtensions = {
            ".ogg", ".opus", ".mp3", ".m4a", ".wav", ".aac", ".flac", ".wma", ".amr", ".mp4", ".webm", ".mkv", ".mov", ".3gp"
        };

        readonly ListBox fileList = new ListBox();
        readonly ComboBox modelBox = new ComboBox();
        readonly ComboBox languageBox = new ComboBox();
        readonly Button pickButton = new Button();
        readonly Button clearButton = new Button();
        readonly Button runButton = new Button();
        readonly TextBox urlBox = new TextBox();
        readonly Button urlButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Button copyButton = new Button();
        readonly Button folderButton = new Button();
        readonly ProgressBar progress = new ProgressBar();
        readonly Label statusLabel = new Label();
        readonly Label deviceLabel = new Label();
        readonly TextBox output = new TextBox();

        Process worker;
        bool cancelled;
        bool sawAllDone;
        string fatalMessage;
        readonly List<string> stderrTail = new List<string>();
        readonly List<string> runFiles = new List<string>();
        readonly List<string> doneTxts = new List<string>();
        readonly List<string> failed = new List<string>();
        int currentIndex;
        double currentDuration;
        string listPath;
        string workerPath;
        string downloadedPath;
        public MainForm(string[] args)
        {
            Text = "Transcritor de áudio";
            Font = new Font("Segoe UI", 10f);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            float scale;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            ClientSize = new Size((int)(860 * scale), (int)(680 * scale));
            MinimumSize = new Size((int)(640 * scale), (int)(520 * scale));

            BuildLayout(scale);
            LoadSettings();
            UpdateButtons();

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            fileList.AllowDrop = true;
            fileList.DragEnter += OnDragEnter;
            fileList.DragDrop += OnDragDrop;
            FormClosing += OnFormClosing;

            if (args != null && args.Length > 0)
            {
                AddFiles(args);
                if (fileList.Items.Count > 0)
                    Shown += delegate { StartTranscription(); };
            }
        }

        void BuildLayout(float scale)
        {
            int pad = (int)(12 * scale);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(pad);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 0 botoes de arquivo
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100 * scale)); // 1 lista
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 2 opcoes
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 3 progresso
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 4 status
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 5 titulo do texto
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));       // 6 texto
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // 7 copiar/abrir
            Controls.Add(root);

            FlowLayoutPanel top = NewFlow();
            SetupButton(pickButton, "Escolher áudios…", OnPick);
            SetupButton(clearButton, "Limpar lista", delegate { fileList.Items.Clear(); UpdateButtons(); });
            Label hint = new Label();
            hint.Text = "ou arraste os arquivos para esta janela";
            hint.AutoSize = true;
            hint.ForeColor = SystemColors.GrayText;
            hint.Margin = new Padding((int)(8 * scale), (int)(9 * scale), 0, 0);
            top.Controls.Add(pickButton);
            top.Controls.Add(clearButton);
            top.Controls.Add(hint);
            top.SetFlowBreak(hint, true);
            top.Controls.Add(NewLabel("Link do YouTube:", scale));
            urlBox.Width = (int)(420 * scale);
            urlBox.Margin = new Padding((int)(6 * scale), (int)(6 * scale), 0, 0);
            urlBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartDownload(); }
            };
            urlBox.TextChanged += delegate { UpdateButtons(); };
            top.Controls.Add(urlBox);
            SetupButton(urlButton, "Baixar e transcrever", delegate { StartDownload(); });
            urlButton.Margin = new Padding((int)(6 * scale), (int)(4 * scale), 0, 0);
            top.Controls.Add(urlButton);            root.Controls.Add(top, 0, 0);

            fileList.Dock = DockStyle.Fill;
            fileList.HorizontalScrollbar = true;
            fileList.SelectionMode = SelectionMode.MultiExtended;
            fileList.IntegralHeight = false;
            fileList.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete && worker == null)
                {
                    foreach (object item in fileList.SelectedItems.Cast<object>().ToList())
                        fileList.Items.Remove(item);
                    UpdateButtons();
                }
            };
            root.Controls.Add(fileList, 0, 1);

            FlowLayoutPanel options = NewFlow();
            options.Margin = new Padding(0, (int)(8 * scale), 0, 0);
            options.Controls.Add(NewLabel("Modelo:", scale));
            modelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            modelBox.Width = (int)(380 * scale);
            modelBox.Items.Add(new ModelOption("turbo", "large-v3-turbo.pt", "rápido e quase tão bom quanto o large", "1,6 GB"));
            options.Controls.Add(modelBox);
            options.Controls.Add(NewLabel("Idioma:", scale));
            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
            languageBox.Width = (int)(170 * scale);
            languageBox.Items.Add(new LanguageOption("pt", "Português"));
            languageBox.Items.Add(new LanguageOption("auto", "Detectar sozinho"));
            languageBox.Items.Add(new LanguageOption("en", "Inglês"));
            languageBox.Items.Add(new LanguageOption("es", "Espanhol"));
            options.Controls.Add(languageBox);
            root.Controls.Add(options, 0, 2);

            FlowLayoutPanel actions = NewFlow();
            actions.Margin = new Padding(0, (int)(8 * scale), 0, 0);
            SetupButton(runButton, "Transcrever", delegate { StartTranscription(); });
            runButton.Font = new Font(Font, FontStyle.Bold);
            SetupButton(cancelButton, "Cancelar", OnCancel);
            actions.Controls.Add(runButton);
            actions.Controls.Add(cancelButton);
            deviceLabel.AutoSize = true;
            deviceLabel.ForeColor = SystemColors.GrayText;
            deviceLabel.Margin = new Padding((int)(8 * scale), (int)(9 * scale), 0, 0);
            actions.Controls.Add(deviceLabel);
            root.Controls.Add(actions, 0, 3);

            TableLayoutPanel progressRow = new TableLayoutPanel();
            progressRow.Dock = DockStyle.Fill;
            progressRow.AutoSize = true;
            progressRow.ColumnCount = 1;
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            progressRow.Margin = new Padding(0, (int)(6 * scale), 0, 0);
            progress.Dock = DockStyle.Fill;
            progress.Height = (int)(18 * scale);
            progress.Maximum = 1000;
            progressRow.Controls.Add(progress, 0, 0);
            statusLabel.AutoSize = true;
            statusLabel.Text = "Escolha um ou mais áudios, ou cole um link do YouTube.";
            statusLabel.Margin = new Padding(0, (int)(4 * scale), 0, 0);
            progressRow.Controls.Add(statusLabel, 0, 1);
            root.Controls.Add(progressRow, 0, 4);

            Label textTitle = new Label();
            textTitle.Text = "Transcrição (o .txt é salvo ao lado de cada áudio):";
            textTitle.AutoSize = true;
            textTitle.Margin = new Padding(0, (int)(8 * scale), 0, (int)(2 * scale));
            root.Controls.Add(textTitle, 0, 5);

            output.Multiline = true;
            output.ReadOnly = true;
            output.BackColor = SystemColors.Window;
            output.ScrollBars = ScrollBars.Vertical;
            output.WordWrap = true;
            output.Dock = DockStyle.Fill;
            output.Font = new Font("Segoe UI", 11f);
            root.Controls.Add(output, 0, 6);

            FlowLayoutPanel bottom = NewFlow();
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = new Padding(0, (int)(8 * scale), 0, 0);
            SetupButton(folderButton, "Abrir pasta do .txt", OnOpenFolder);
            SetupButton(copyButton, "Copiar texto", OnCopy);
            bottom.Controls.Add(folderButton);
            bottom.Controls.Add(copyButton);
            root.Controls.Add(bottom, 0, 7);

            AcceptButton = runButton;
        }

        static FlowLayoutPanel NewFlow()
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.AutoSize = true;
            flow.Dock = DockStyle.Fill;
            flow.WrapContents = true;
            flow.Margin = new Padding(0);
            return flow;
        }

        static Label NewLabel(string text, float scale)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding((int)(6 * scale), (int)(7 * scale), 0, 0);
            return label;
        }

        static void SetupButton(Button button, string text, EventHandler onClick)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(8, 3, 8, 3);
            button.Click += onClick;
        }

        // ---------- arquivos ----------

        void OnPick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Qual áudio você quer transcrever?";
                dialog.Multiselect = true;
                dialog.Filter = "Áudio e vídeo|" + string.Join(";", AudioExtensions.Select(x => "*" + x).ToArray()) + "|Todos os arquivos|*.*";
                string last = ReadSetting("lastFolder");
                if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                    dialog.InitialDirectory = last;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    AddFiles(dialog.FileNames);
                    if (dialog.FileNames.Length > 0)
                        WriteSetting("lastFolder", Path.GetDirectoryName(dialog.FileNames[0]));
                }
            }
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (worker == null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            if (worker != null) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddFiles(paths);
        }

        void AddFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (string f in Directory.GetFiles(path).OrderBy(x => x))
                        if (AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            AddFile(f);
                }
                else if (File.Exists(path))
                {
                    AddFile(path);
                }
            }
            UpdateButtons();
        }

        void AddFile(string path)
        {
            string full = Path.GetFullPath(path);
            foreach (AudioItem item in fileList.Items)
                if (string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase)) return;
            fileList.Items.Add(new AudioItem(full));
        }

        // ---------- transcricao ----------

        void StartTranscription()
        {
            if (worker != null || fileList.Items.Count == 0) return;

            string python = FindPython();
            if (python == null)
            {
                MessageBox.Show(this, "Não achei o Python (python.exe) nesta máquina.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ModelOption model = (ModelOption)modelBox.SelectedItem;
            LanguageOption language = (LanguageOption)languageBox.SelectedItem;
            WriteSetting("model", model.Name);
            WriteSetting("language", language.Code);

            runFiles.Clear();
            runFiles.AddRange(fileList.Items.Cast<AudioItem>().Select(x => x.Path));
            doneTxts.Clear();
            failed.Clear();
            stderrTail.Clear();
            cancelled = false;
            sawAllDone = false;
            fatalMessage = null;
            currentIndex = 0;
            currentDuration = 0;
            output.Clear();
            progress.Value = 0;
            deviceLabel.Text = "";

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "Transcritor");
                Directory.CreateDirectory(tempDir);
                workerPath = Path.Combine(tempDir, "worker.py");
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("worker.py"))
                using (FileStream f = File.Create(workerPath))
                    s.CopyTo(f);
                listPath = Path.Combine(tempDir, "lista-" + Process.GetCurrentProcess().Id + ".txt");
                File.WriteAllLines(listPath, runFiles, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui preparar o worker: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = python;
            info.Arguments = "-u " + Quote(workerPath) + " --model " + model.Name + " --language " + language.Code + " --list " + Quote(listPath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            info.EnvironmentVariables["PYTHONUTF8"] = "1";
            string links = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links");
            info.EnvironmentVariables["PATH"] = links + ";" + (info.EnvironmentVariables["PATH"] ?? "");

            Process p = new Process();
            p.StartInfo = info;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) BeginInvoke(new Action<string>(HandleLine), e.Data);
            };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (stderrTail)
                {
                    stderrTail.Add(e.Data);
                    if (stderrTail.Count > 40) stderrTail.RemoveAt(0);
                }
            };

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui iniciar o Python: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            worker = p;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            SetStatus("Iniciando…");
            UpdateButtons();

            Thread waiter = new Thread(delegate()
            {
                p.WaitForExit(); // espera tambem o fim do stdout/stderr redirecionados
                int code = p.ExitCode;
                try { BeginInvoke(new Action<int>(OnWorkerExit), code); } catch { }
            });
            waiter.IsBackground = true;
            waiter.Start();
        }

        // ---------- download do YouTube ----------

        void StartDownload()
        {
            string url = urlBox.Text.Trim();
            if (worker != null || url.Length == 0) return;

            string python = FindPython();
            if (python == null)
            {
                MessageBox.Show(this, "Não achei o Python (python.exe) nesta máquina.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string root = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "videos");
            string template = Path.Combine(root, "%(title)s", "%(title)s.%(ext)s");

            cancelled = false;
            downloadedPath = null;
            stderrTail.Clear();
            output.Clear();
            progress.Value = 0;
            deviceLabel.Text = "";

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = python;
            info.Arguments = "-u -m yt_dlp --no-playlist -x --audio-format mp3 --newline --progress --print after_move:filepath -o "
                + Quote(template) + " " + Quote(url);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8;
            info.StandardErrorEncoding = Encoding.UTF8;
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            info.EnvironmentVariables["PYTHONUTF8"] = "1";
            string links = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WinGet\Links");
            info.EnvironmentVariables["PATH"] = links + ";" + (info.EnvironmentVariables["PATH"] ?? "");

            Process p = new Process();
            p.StartInfo = info;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) BeginInvoke(new Action<string>(HandleDownloadLine), e.Data);
            };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (stderrTail)
                {
                    stderrTail.Add(e.Data);
                    if (stderrTail.Count > 40) stderrTail.RemoveAt(0);
                }
            };

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui iniciar o Python: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            worker = p;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            SetStatus("Baixando o áudio…");
            UpdateButtons();

            Thread waiter = new Thread(delegate()
            {
                p.WaitForExit();
                int code = p.ExitCode;
                try { BeginInvoke(new Action<int>(OnDownloadExit), code); } catch { }
            });
            waiter.IsBackground = true;
            waiter.Start();
        }

        void HandleDownloadLine(string line)
        {
            if (line.StartsWith("[download]") && line.Contains("%"))
            {
                SetStatus("Baixando o áudio: " + line.Substring(10).Trim());
                return;
            }
            if (line.StartsWith("[") || line.Length == 0) return;
            if (File.Exists(line)) downloadedPath = line; // --print after_move:filepath
        }

        void OnDownloadExit(int exitCode)
        {
            worker = null;
            UpdateButtons();

            if (cancelled)
            {
                SetStatus("Cancelado.");
                return;
            }

            if (exitCode == 0 && downloadedPath != null)
            {
                fileList.Items.Clear();
                AddFile(downloadedPath);
                UpdateButtons();
                SetStatus("MP3 baixado. Transcrevendo…");
                StartTranscription();
                return;
            }

            string tail;
            lock (stderrTail) tail = string.Join(Environment.NewLine, stderrTail.ToArray());
            SetStatus("Não consegui baixar o vídeo.");
            MessageBox.Show(this, "O download falhou (código " + exitCode + ")." + (tail.Length > 0 ? Environment.NewLine + Environment.NewLine + "Detalhes:" + Environment.NewLine + tail : "")
                + Environment.NewLine + Environment.NewLine + "Se o vídeo é público, atualizar o yt-dlp costuma resolver: python -m pip install -U yt-dlp",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void HandleLine(string line)
        {
            if (!line.StartsWith("@@")) return;
            string[] f = line.Substring(2).Split('\t');
            switch (f[0])
            {
                case "STATUS":
                    if (f.Length > 1) SetStatus(f[1]);
                    break;
                case "DEVICE":
                    if (f.Length > 1) deviceLabel.Text = "Rodando em: " + f[1];
                    break;
                case "FILE":
                    currentIndex = int.Parse(f[1], CultureInfo.InvariantCulture);
                    currentDuration = ParseDouble(f[4]);
                    if (runFiles.Count > 1)
                    {
                        if (output.TextLength > 0) output.AppendText(Environment.NewLine);
                        output.AppendText("===== " + Path.GetFileName(f[3]) + " =====" + Environment.NewLine);
                    }
                    SetFileProgress(0);
                    break;
                case "SEG":
                    output.AppendText(f[3] + Environment.NewLine);
                    SetFileProgress(ParseDouble(f[2]));
                    break;
                case "FILEDONE":
                    doneTxts.Add(f[2]);
                    SetFileProgress(currentDuration);
                    break;
                case "FILEERR":
                    failed.Add(Path.GetFileName(runFiles[int.Parse(f[1], CultureInfo.InvariantCulture)]) + ": " + f[2]);
                    output.AppendText("[erro: " + f[2] + "]" + Environment.NewLine);
                    break;
                case "ALLDONE":
                    sawAllDone = true;
                    break;
                case "FATAL":
                    fatalMessage = f.Length > 1 ? f[1] : "erro desconhecido";
                    break;
            }
        }

        void SetFileProgress(double seconds)
        {
            double fraction = currentDuration > 0 ? Math.Min(1.0, seconds / currentDuration) : 0;
            double overall = (currentIndex + fraction) / Math.Max(1, runFiles.Count);
            progress.Value = Math.Max(0, Math.Min(1000, (int)(overall * 1000)));
            string name = Path.GetFileName(runFiles[currentIndex]);
            string prefix = runFiles.Count > 1 ? "Transcrevendo " + (currentIndex + 1) + " de " + runFiles.Count + ": " : "Transcrevendo: ";
            SetStatus(prefix + name + " — " + (int)(fraction * 100) + "%");
        }

        void OnWorkerExit(int exitCode)
        {
            worker = null;
            TryDelete(listPath);
            UpdateButtons();

            if (cancelled)
            {
                SetStatus("Cancelado.");
                return;
            }

            if (sawAllDone && failed.Count == 0)
            {
                progress.Value = 1000;
                SetStatus(doneTxts.Count == 1
                    ? "Pronto! Texto salvo em " + doneTxts[0]
                    : "Pronto! " + doneTxts.Count + " arquivos transcritos; cada .txt ficou ao lado do seu áudio.");
                SystemSounds.Asterisk.Play();
                return;
            }

            string detail;
            if (fatalMessage != null) detail = fatalMessage;
            else if (failed.Count > 0) detail = string.Join(Environment.NewLine, failed.ToArray());
            else detail = "O Python fechou com código " + exitCode + ".";

            string tail;
            lock (stderrTail) tail = string.Join(Environment.NewLine, stderrTail.ToArray());
            SetStatus(doneTxts.Count > 0 ? "Terminou com erros (" + doneTxts.Count + " de " + runFiles.Count + " transcritos)." : "Deu erro.");
            MessageBox.Show(this, detail + (tail.Length > 0 ? Environment.NewLine + Environment.NewLine + "Detalhes:" + Environment.NewLine + tail : ""),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        void OnCancel(object sender, EventArgs e)
        {
            if (worker == null) return;
            cancelled = true;
            try { worker.Kill(); } catch { }
            SetStatus("Cancelando…");
        }

        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (worker == null) return;
            if (MessageBox.Show(this, "A transcrição ainda está rodando. Cancelar e fechar?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            cancelled = true;
            try { worker.Kill(); } catch { }
        }

        void OnCopy(object sender, EventArgs e)
        {
            if (output.TextLength == 0) return;
            try
            {
                Clipboard.SetText(output.Text);
                SetStatus("Texto copiado.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Não consegui copiar: " + ex.Message, Text);
            }
        }

        void OnOpenFolder(object sender, EventArgs e)
        {
            string target = doneTxts.Count > 0 ? doneTxts[doneTxts.Count - 1] : null;
            if (target != null && File.Exists(target))
                Process.Start("explorer.exe", "/select," + Quote(target));
            else if (fileList.Items.Count > 0)
                Process.Start("explorer.exe", "/select," + Quote(((AudioItem)fileList.Items[0]).Path));
        }

        void UpdateButtons()
        {
            bool running = worker != null;
            pickButton.Enabled = !running;
            urlBox.Enabled = !running;
            urlButton.Enabled = !running && urlBox.Text.Trim().Length > 0;
            clearButton.Enabled = !running && fileList.Items.Count > 0;
            runButton.Enabled = !running && fileList.Items.Count > 0;
            cancelButton.Enabled = running;
            modelBox.Enabled = !running;
            languageBox.Enabled = !running;
            copyButton.Enabled = output.TextLength > 0 || running;
            folderButton.Enabled = fileList.Items.Count > 0;
            if (!running && fileList.Items.Count > 0 && statusLabel.Text.StartsWith("Escolha"))
                SetStatus(fileList.Items.Count == 1 ? "1 arquivo na lista." : fileList.Items.Count + " arquivos na lista.");
        }

        void SetStatus(string text)
        {
            statusLabel.Text = text;
        }

        // ---------- utilitarios ----------

        static string FindPython()
        {
            string forced = Environment.GetEnvironmentVariable("TRANSCRITOR_PYTHON");
            if (!string.IsNullOrEmpty(forced) && File.Exists(forced)) return forced;

            foreach (string raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string dir = raw.Trim().Trim('"');
                if (dir.Length == 0 || dir.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                try
                {
                    string candidate = Path.Combine(dir, "python.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            string[] fallbacks = {
                @"C:\Python314\python.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python314\python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Python\Python312\python.exe"),
            };
            foreach (string candidate in fallbacks)
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        static double ParseDouble(string s)
        {
            double value;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        static void TryDelete(string path)
        {
            try { if (path != null) File.Delete(path); } catch { }
        }

        // ---------- configuracoes (modelo, idioma, ultima pasta) ----------

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Transcritor\config.txt"); }
        }

        static Dictionary<string, string> ReadSettings()
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (string line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
            }
            catch { }
            return values;
        }

        static string ReadSetting(string key)
        {
            string value;
            return ReadSettings().TryGetValue(key, out value) ? value : null;
        }

        static void WriteSetting(string key, string value)
        {
            try
            {
                Dictionary<string, string> values = ReadSettings();
                values[key] = value;
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllLines(SettingsPath, values.Select(kv => kv.Key + "=" + kv.Value).ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }

        void LoadSettings()
        {
            string model = ReadSetting("model") ?? "turbo";
            string language = ReadSetting("language") ?? "pt";
            modelBox.SelectedIndex = 0;
            for (int i = 0; i < modelBox.Items.Count; i++)
                if (((ModelOption)modelBox.Items[i]).Name == model) modelBox.SelectedIndex = i;
            languageBox.SelectedIndex = 0;
            for (int i = 0; i < languageBox.Items.Count; i++)
                if (((LanguageOption)languageBox.Items[i]).Code == language) languageBox.SelectedIndex = i;
        }
    }
}
