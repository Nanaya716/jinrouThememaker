using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

[assembly: System.Reflection.AssemblyTitle("主题图像工具")]
[assembly: System.Reflection.AssemblyDescription("本机裁剪、Postimages 上传与直链复制")]
[assembly: System.Reflection.AssemblyVersion("1.0.2.0")]

namespace ThemeImageUploader
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { SetProcessDPIAware(); } catch { }
            if (args.Length >= 2 && (args[0] == "--self-test" || args[0] == "--upload-test"))
                return Diagnostics.Run(args[1], args[0] == "--upload-test");
            try { Application.Run(new MainForm()); return 0; }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "工具启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    internal static class CropImage
    {
        public static Bitmap Load(string file)
        {
            var info = new FileInfo(file);
            if (info.Length > 50L * 1024 * 1024) throw new Exception("图片文件不能超过 50 MB。");
            using (var stream = File.OpenRead(file))
            using (var image = Image.FromStream(stream, true, true))
            {
                if ((long)image.Width * image.Height > 50000000) throw new Exception("图片不能超过 5000 万像素。");
                if (image.PropertyIdList.Contains(274))
                {
                    int orientation = image.GetPropertyItem(274).Value[0];
                    var transforms = new[] { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone,
                        RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX,
                        RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX,
                        RotateFlipType.Rotate270FlipNone };
                    if (orientation > 0 && orientation < transforms.Length) image.RotateFlip(transforms[orientation]);
                }
                return new Bitmap(image);
            }
        }

        public static Bitmap Render(Bitmap source, RectangleF region, int size)
        {
            if (source == null || size < 1 || size > 2048 || region.Width < 1 || region.Height < 1 ||
                Math.Abs(region.Width - region.Height) > 0.1f || region.X < -0.1 || region.Y < -0.1 ||
                region.Right > source.Width + 0.1 || region.Bottom > source.Height + 0.1)
                throw new ArgumentException("裁剪区域或输出尺寸无效。");
            var target = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(target))
            using (var attributes = new ImageAttributes())
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(0, 0, size, size), region.X, region.Y,
                    region.Width, region.Height, GraphicsUnit.Pixel, attributes);
            }
            return target;
        }

        public static byte[] Png(Bitmap image)
        {
            using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); return stream.ToArray(); }
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }

    internal sealed class UploadResult
    {
        public string DirectUrl { get; set; }
        public string PageUrl { get; set; }
        public string DeleteUrl { get; set; }
    }

    internal static class Postimages
    {
        private const string DefaultApiKey = "__POSTIMAGES_API_KEY__";

        public static async Task<UploadResult> Upload(byte[] png, string name, string apiKey)
        {
            var values = new Dictionary<string, string> {
                { "key", String.IsNullOrWhiteSpace(apiKey) ? DefaultApiKey : apiKey.Trim() },
                { "gallery", "default" }, { "o", "2b819584285c102318568238c7d4a4c7" },
                { "m", "59c2ad4b46b0c1e12d5703302bff0120" }, { "version", "1.0.1" },
                { "portable", "1" }, { "name", name }, { "type", "png" }, { "image", Convert.ToBase64String(png) }
            };
            using (var client = new HttpClient())
            using (var content = CreateFormContent(values))
            {
                client.Timeout = TimeSpan.FromSeconds(60);
                using (var response = await client.PostAsync("https://api.postimage.org/1/upload", content).ConfigureAwait(false))
                {
                    string xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new Exception("图床返回 HTTP " + (int)response.StatusCode + "。请检查网络、Key 或图床限制。");
                    return Parse(xml);
                }
            }
        }

        internal static HttpContent CreateFormContent(IDictionary<string, string> values)
        {
            // Framework FormUrlEncodedContent uses Uri.EscapeDataString, which rejects long
            // values. Image Base64 belongs in a POST body and must not use the URI encoder.
            string body = String.Join("&", values.Select(pair =>
                WebUtility.UrlEncode(pair.Key) + "=" + WebUtility.UrlEncode(pair.Value)));
            return new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        }

        public static UploadResult Parse(string text)
        {
            var document = new XmlDocument { XmlResolver = null };
            try
            {
                using (var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 }))
                    document.Load(reader);
            }
            catch (XmlException) { throw new Exception("图床没有返回有效上传结果，可能受到网络验证或接口限制。"); }
            var error = document.SelectSingleNode("//error");
            if (error != null && !String.IsNullOrWhiteSpace(error.InnerText))
                throw new Exception("图床拒绝上传：" + error.InnerText.Substring(0, Math.Min(400, error.InnerText.Length)));
            string direct = ReadUrl(document, "hotlink", "i.postimg.cc");
            if (direct == "") throw new Exception("图床没有返回有效图片直链，本次结果无法确认，请勿连续重传。");
            return new UploadResult { DirectUrl = direct, PageUrl = ReadUrl(document, "page", "postimg.cc"),
                DeleteUrl = ReadUrl(document, "delete", "postimg.cc") };
        }

        private static string ReadUrl(XmlDocument document, string tag, string host)
        {
            var node = document.SelectSingleNode("//links/" + tag);
            Uri url;
            string text = node == null ? "" : node.InnerText.Trim();
            return Uri.TryCreate(text, UriKind.Absolute, out url) && url.Scheme == "https" &&
                url.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && String.IsNullOrEmpty(url.UserInfo) ? url.AbsoluteUri : "";
        }
    }

    internal sealed class UploadRecord
    {
        public string Time { get; set; }
        public string File { get; set; }
        public int Size { get; set; }
        public string Sha256 { get; set; }
        public string DirectUrl { get; set; }
        public string PageUrl { get; set; }
        public string DeleteUrl { get; set; }
    }

    internal static class History
    {
        public static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThemeImageUploader", "history.jsonl");

        public static void Append(string file, UploadRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.WriteLine(new JavaScriptSerializer().Serialize(record));
        }

        public static List<UploadRecord> Read(string file)
        {
            var rows = new List<UploadRecord>();
            if (!System.IO.File.Exists(file)) return rows;
            using (var reader = new StreamReader(file, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    try
                    {
                        var row = new JavaScriptSerializer().Deserialize<UploadRecord>(line);
                        if (row != null && !String.IsNullOrEmpty(row.DirectUrl)) rows.Add(row);
                        if (rows.Count > 500) rows.RemoveAt(0);
                    }
                    catch (ArgumentException) { }
                }
            }
            rows.Reverse();
            return rows;
        }
    }

    internal sealed class CropCanvas : Control
    {
        public Bitmap Picture { get; private set; }
        public RectangleF CropRegion { get; private set; }
        public event EventHandler CropChanged;
        private RectangleF display;
        private Point dragStart;
        private RectangleF initial;
        private int dragMode;

        public CropCanvas()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(237, 239, 244);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetPicture(Bitmap picture)
        {
            var old = Picture;
            Picture = picture;
            if (old != null) old.Dispose();
            ResetRegion();
        }

        public void ResetRegion()
        {
            if (Picture != null)
            {
                float edge = Math.Min(Picture.Width, Picture.Height);
                CropRegion = new RectangleF((Picture.Width - edge) / 2, (Picture.Height - edge) / 2, edge, edge);
            }
            Changed();
        }

        public void SetCoverage(float ratio)
        {
            if (Picture == null) return;
            float edge = Math.Max(1, Math.Min(Picture.Width, Picture.Height) * ratio);
            SetRegion(CropRegion.X + CropRegion.Width / 2 - edge / 2, CropRegion.Y + CropRegion.Height / 2 - edge / 2, edge);
        }

        private void SetRegion(float x, float y, float edge)
        {
            edge = Math.Max(1, Math.Min(edge, Math.Min(Picture.Width, Picture.Height)));
            CropRegion = new RectangleF(Math.Max(0, Math.Min(Picture.Width - edge, x)),
                Math.Max(0, Math.Min(Picture.Height - edge, y)), edge, edge);
            Changed();
        }

        private void Changed() { Invalidate(); if (CropChanged != null) CropChanged(this, EventArgs.Empty); }
        private RectangleF Selection()
        {
            float scale = display.Width / Picture.Width;
            return new RectangleF(display.X + CropRegion.X * scale, display.Y + CropRegion.Y * scale, CropRegion.Width * scale, CropRegion.Height * scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            if (Picture == null)
            {
                TextRenderer.DrawText(g, "把图片拖到这里\n\n或打开图片 / Ctrl+V 粘贴", Font, ClientRectangle,
                    Color.FromArgb(108, 116, 138), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            float scale = Math.Min(Math.Max(1, Width - 40) / (float)Picture.Width, Math.Max(1, Height - 40) / (float)Picture.Height);
            display = new RectangleF((Width - Picture.Width * scale) / 2, (Height - Picture.Height * scale) / 2,
                Picture.Width * scale, Picture.Height * scale);
            using (var checker = new Bitmap(16, 16))
            {
                using (var cg = Graphics.FromImage(checker))
                {
                    cg.Clear(Color.White);
                    cg.FillRectangle(Brushes.LightGray, 0, 0, 8, 8);
                    cg.FillRectangle(Brushes.LightGray, 8, 8, 8, 8);
                }
                using (var brush = new TextureBrush(checker)) g.FillRectangle(brush, display);
            }
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(Picture, display);
            var box = Selection();
            using (var shade = new SolidBrush(Color.FromArgb(145, 23, 28, 44)))
            {
                g.FillRectangle(shade, display.X, display.Y, display.Width, Math.Max(0, box.Top - display.Top));
                g.FillRectangle(shade, display.X, box.Bottom, display.Width, Math.Max(0, display.Bottom - box.Bottom));
                g.FillRectangle(shade, display.X, box.Top, Math.Max(0, box.Left - display.Left), box.Height);
                g.FillRectangle(shade, box.Right, box.Top, Math.Max(0, display.Right - box.Right), box.Height);
            }
            using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            using (var pen = new Pen(Color.FromArgb(115, Color.White), 1))
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(pen, box.X + box.Width * i / 3, box.Y, box.X + box.Width * i / 3, box.Bottom);
                    g.DrawLine(pen, box.X, box.Y + box.Height * i / 3, box.Right, box.Y + box.Height * i / 3);
                }
            g.FillRectangle(Brushes.White, box.Right - 7, box.Bottom - 7, 14, 14);
            using (var brush = new SolidBrush(Color.FromArgb(191, 50, 104))) g.FillRectangle(brush, box.Right - 4, box.Bottom - 4, 8, 8);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Picture == null || e.Button != MouseButtons.Left || display.Width <= 0) return;
            Focus();
            var box = Selection();
            if (Math.Abs(e.X - box.Right) < 16 && Math.Abs(e.Y - box.Bottom) < 16) dragMode = 2;
            else if (box.Contains(e.Location)) dragMode = 1;
            else return;
            dragStart = e.Location;
            initial = CropRegion;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Picture == null || display.Width <= 0) return;
            if (dragMode == 0)
            {
                var box = Selection();
                Cursor = Math.Abs(e.X - box.Right) < 16 && Math.Abs(e.Y - box.Bottom) < 16 ? Cursors.SizeNWSE :
                    box.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            float scale = display.Width / Picture.Width;
            float dx = (e.X - dragStart.X) / scale, dy = (e.Y - dragStart.Y) / scale;
            if (dragMode == 1) SetRegion(initial.X + dx, initial.Y + dy, initial.Width);
            else
            {
                float maxEdge = Math.Min(Picture.Width - initial.X, Picture.Height - initial.Y);
                SetRegion(initial.X, initial.Y, Math.Min(maxEdge, initial.Width + Math.Max(dx, dy)));
            }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragMode = 0; Capture = false; }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) dragMode = 0; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Picture != null) SetCoverage(CropRegion.Width / Math.Min(Picture.Width, Picture.Height) * (e.Delta > 0 ? 0.9f : 1.1f));
        }
        protected override void Dispose(bool disposing) { if (disposing && Picture != null) Picture.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class MainForm : Form
    {
        private readonly CropCanvas canvas = new CropCanvas();
        private readonly PictureBox preview = new PictureBox();
        private readonly Label details = new Label();
        private readonly Label status = new Label();
        private readonly TrackBar coverage = new TrackBar();
        private readonly ComboBox outputSize = new ComboBox();
        private readonly TextBox resultBox = new TextBox();
        private readonly TextBox keyBox = new TextBox();
        private readonly Button upload;
        private readonly Button save;
        private readonly Button copy;
        private bool busy, syncing;
        private string fileName = "clipboard";
        private string imageLabel = "尚未选择图片";
        private static readonly Color Accent = Color.FromArgb(186, 43, 98);
        private static readonly Color Ink = Color.FromArgb(38, 45, 64);

        public MainForm()
        {
            Text = "主题图像工具 v1.0.2 · 裁剪与上传";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(247, 248, 251);
            ForeColor = Ink;
            ClientSize = new Size(1000, 710);
            MinimumSize = new Size(940, 740);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            AllowDrop = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(22) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Controls.Add(root);
            var heading = new Label { Text = "主题图像工具", Font = new Font(Font.FontFamily, 21, FontStyle.Bold), Dock = DockStyle.Fill };
            root.Controls.Add(heading, 0, 0);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var open = MakeButton("打开图片", 112, false); open.Click += delegate { OpenFile(); };
            var paste = MakeButton("粘贴图片", 112, false); paste.Click += delegate { PasteImage(); };
            var reset = MakeButton("重置取景", 112, false); reset.Click += delegate { if (!busy) canvas.ResetRegion(); };
            var history = MakeButton("上传记录", 112, false); history.Click += delegate { ShowHistory(); };
            toolbar.Controls.AddRange(new Control[] { open, paste, reset, history });
            root.Controls.Add(toolbar, 0, 1);
            var work = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 8, 0, 0) };
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            work.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 268));
            root.Controls.Add(work, 0, 2);
            canvas.Dock = DockStyle.Fill;
            canvas.Margin = new Padding(0, 0, 18, 0);
            canvas.CropChanged += delegate { UpdatePreview(); };
            work.Controls.Add(canvas, 0, 0);
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 13, Margin = new Padding(0) };
            foreach (int h in new[] { 26, 108, 36, 24, 36, 26, 38, 44, 38, 38, 24, 30 }) right.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            work.Controls.Add(right, 1, 0);
            right.Controls.Add(new Label { Text = "裁剪预览", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.Dock = DockStyle.Fill;
            preview.BackColor = Color.White;
            right.Controls.Add(preview, 0, 1);
            details.Dock = DockStyle.Fill; details.TextAlign = ContentAlignment.MiddleLeft; right.Controls.Add(details, 0, 2);
            right.Controls.Add(new Label { Text = "取景范围 · 拖动选框 / 滚轮缩放", Dock = DockStyle.Fill }, 0, 3);
            coverage.Minimum = 1; coverage.Maximum = 100; coverage.Value = 100; coverage.TickStyle = TickStyle.None;
            coverage.Dock = DockStyle.Fill;
            coverage.ValueChanged += delegate { if (!syncing && !busy) canvas.SetCoverage(coverage.Value / 100F); };
            right.Controls.Add(coverage, 0, 4);
            right.Controls.Add(new Label { Text = "输出尺寸", Dock = DockStyle.Fill }, 0, 5);
            outputSize.DropDownStyle = ComboBoxStyle.DropDownList; outputSize.Dock = DockStyle.Fill;
            outputSize.Items.AddRange(new object[] { "256 × 256", "512 × 512", "1024 × 1024" });
            outputSize.SelectedIndex = 1; outputSize.SelectedIndexChanged += delegate { UpdatePreview(); };
            right.Controls.Add(outputSize, 0, 6);
            upload = MakeButton("裁剪并上传", 240, true); upload.Dock = DockStyle.Fill;
            upload.Click += async delegate { await UploadImage(); }; right.Controls.Add(upload, 0, 7);
            save = MakeButton("保存裁剪图", 240, false); save.Dock = DockStyle.Fill;
            save.Click += delegate { SaveImage(); }; right.Controls.Add(save, 0, 8);
            var resultRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            resultRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resultRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            resultBox.ReadOnly = true; resultBox.Dock = DockStyle.Fill; resultBox.Margin = new Padding(0, 5, 5, 0);
            copy = MakeButton("复制", 60, false); copy.Dock = DockStyle.Fill;
            copy.Click += delegate { CopyLink(resultBox.Text); };
            resultRow.Controls.Add(resultBox); resultRow.Controls.Add(copy); right.Controls.Add(resultRow, 0, 9);
            right.Controls.Add(new Label { Text = "自定义 API Key（可选）", Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 10);
            keyBox.UseSystemPasswordChar = true; keyBox.Dock = DockStyle.Fill; right.Controls.Add(keyBox, 0, 11);
            var apiLink = new LinkLabel { Text = "自取账号API链接", AutoSize = true, LinkColor = Accent, Margin = new Padding(0, 4, 0, 0) };
            apiLink.LinkClicked += delegate {
                try { System.Diagnostics.Process.Start("https://postimages.org/login/api"); }
                catch (Exception error) { SetStatus("无法打开浏览器：" + error.Message, true); }
            };
            right.Controls.Add(apiLink, 0, 12);
            status.Dock = DockStyle.Fill; status.Padding = new Padding(0, 12, 0, 0);
            status.Text = "图片只从本机上传至 Postimages。支持拖入文件，或 Ctrl+V 粘贴图片。";
            root.Controls.Add(status, 0, 3);
            DragEventHandler dragEnter = delegate(object sender, DragEventArgs e) { if (!busy && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragEventHandler dragDrop = delegate(object sender, DragEventArgs e) {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (!busy && files != null && files.Length > 0) LoadImageFile(files[0]);
            };
            DragEnter += dragEnter; DragDrop += dragDrop;
            canvas.AllowDrop = true; canvas.DragEnter += dragEnter; canvas.DragDrop += dragDrop;
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; SetStatus("正在等待图床响应，请等上传结束后再关闭。", true); }
            };
            UpdatePreview();
        }

        private static Button MakeButton(string text, int width, bool primary)
        {
            return new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Margin = new Padding(0, 0, 8, 4), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
        }
        private int SizeValue { get { return new[] { 256, 512, 1024 }[Math.Max(0, outputSize.SelectedIndex)]; } }
        private void SetStatus(string text, bool error) { status.Text = text; status.ForeColor = error ? Color.Firebrick : Ink; }

        private void UpdatePreview()
        {
            if (upload == null) return;
            resultBox.Text = ""; copy.Enabled = false;
            upload.Enabled = save.Enabled = canvas.Picture != null && !busy;
            coverage.Enabled = canvas.Picture != null && !busy;
            var previous = preview.Image; preview.Image = null;
            if (previous != null) previous.Dispose();
            if (canvas.Picture == null) { details.Text = "支持 PNG / JPG / BMP / GIF / TIFF"; return; }
            using (var thumb = CropImage.Render(canvas.Picture, canvas.CropRegion, 128)) preview.Image = new Bitmap(thumb);
            details.Text = String.Format("原图 {0} × {1}  ·  取景 {2} × {2}", canvas.Picture.Width, canvas.Picture.Height, (int)canvas.CropRegion.Width);
            syncing = true;
            coverage.Value = Math.Max(1, Math.Min(100, (int)Math.Round(100 * canvas.CropRegion.Width / Math.Min(canvas.Picture.Width, canvas.Picture.Height))));
            syncing = false;
        }

        private void OpenFile()
        {
            if (busy) return;
            using (var dialog = new OpenFileDialog { Title = "选择要裁剪的图片", Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) LoadImageFile(dialog.FileName);
        }
        internal void LoadImageFile(string file)
        {
            try
            {
                var image = CropImage.Load(file);
                fileName = Path.GetFileNameWithoutExtension(file);
                imageLabel = Path.GetFileName(file);
                canvas.SetPicture(image);
                SetStatus("已打开 " + imageLabel + "。拖动选框移动取景，拖动右下角调整大小。", false);
            }
            catch (Exception error) { SetStatus("图片打开失败：" + error.Message, true); }
        }
        private void PasteImage()
        {
            if (busy) return;
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (var image = Clipboard.GetImage())
                    {
                        if (image == null) return;
                        if ((long)image.Width * image.Height > 50000000) throw new Exception("图片不能超过 5000 万像素。");
                        fileName = "clipboard-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); imageLabel = "剪贴板图片";
                        canvas.SetPicture(new Bitmap(image));
                        SetStatus("已粘贴图片，可以调整取景。", false);
                    }
                }
                else if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList().Count > 0) LoadImageFile(Clipboard.GetFileDropList()[0]);
                else SetStatus("剪贴板中没有图片或图片文件。", true);
            }
            catch (Exception error) { SetStatus("粘贴失败：" + error.Message, true); }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) && !keyBox.Focused) { PasteImage(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenFile(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveImage()
        {
            if (canvas.Picture == null || busy) return;
            using (var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = fileName + "-cropped.png", AddExtension = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var image = CropImage.Render(canvas.Picture, canvas.CropRegion, SizeValue)) image.Save(dialog.FileName, ImageFormat.Png);
                    SetStatus("裁剪图已保存到 " + dialog.FileName, false);
                }
                catch (Exception error) { SetStatus("保存失败：" + error.Message, true); }
            }
        }

        private async Task UploadImage()
        {
            if (canvas.Picture == null || busy) return;
            byte[] png;
            try { using (var image = CropImage.Render(canvas.Picture, canvas.CropRegion, SizeValue)) png = CropImage.Png(image); }
            catch (Exception error) { SetStatus("裁剪失败：" + error.Message, true); return; }
            int size = SizeValue;
            busy = true; canvas.Enabled = false; upload.Enabled = save.Enabled = coverage.Enabled = false;
            outputSize.Enabled = keyBox.Enabled = false; upload.Text = "正在上传…";
            resultBox.Text = ""; copy.Enabled = false;
            SetStatus("正在从本机上传裁剪后的 PNG，请稍候…", false);
            try
            {
                var result = await Postimages.Upload(png, fileName + "-cropped", keyBox.Text);
                resultBox.Text = result.DirectUrl; copy.Enabled = true;
                string note = "上传成功，图片直链已复制。";
                bool warning = false;
                try { Clipboard.SetText(result.DirectUrl); }
                catch { note = "上传成功。剪贴板暂不可用，请点击“复制”或手动复制直链。"; }
                try
                {
                    History.Append(History.FilePath, new UploadRecord { Time = DateTimeOffset.Now.ToString("o"), File = imageLabel,
                        Size = size, Sha256 = CropImage.Hash(png), DirectUrl = result.DirectUrl, PageUrl = result.PageUrl, DeleteUrl = result.DeleteUrl });
                }
                catch { note += " 本地记录写入失败，请保留直链。"; warning = true; }
                SetStatus(note, warning);
            }
            catch (TaskCanceledException) { SetStatus("上传超时，结果暂不确定。网络恢复后再试，避免连续重传。", true); }
            catch (HttpRequestException error) { SetStatus("无法连接图床，请检查本机网络或代理：" + error.Message, true); }
            catch (Exception error) { SetStatus("上传失败：" + error.Message, true); }
            finally
            {
                busy = false; canvas.Enabled = true; upload.Enabled = save.Enabled = coverage.Enabled = true;
                outputSize.Enabled = keyBox.Enabled = true; upload.Text = "裁剪并上传";
            }
        }

        private void CopyLink(string link)
        {
            if (String.IsNullOrEmpty(link)) return;
            try { Clipboard.SetText(link); SetStatus("图片直链已复制，可粘贴到 thememaker。", false); }
            catch { SetStatus("无法写入剪贴板，请选中链接后手动复制。", true); }
        }

        private void ShowHistory()
        {
            List<UploadRecord> records;
            try { records = History.Read(History.FilePath); }
            catch (Exception error) { SetStatus("读取记录失败：" + error.Message, true); return; }
            using (var dialog = new Form { Text = "本机上传记录", Font = Font, Size = new Size(860, 510), StartPosition = FormStartPosition.CenterParent, MinimizeBox = false })
            {
                var table = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
                table.Columns.Add("时间", 185); table.Columns.Add("文件", 185); table.Columns.Add("尺寸", 65); table.Columns.Add("图片直链", 360);
                foreach (var row in records)
                {
                    DateTimeOffset date;
                    string time = DateTimeOffset.TryParse(row.Time, out date) ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : row.Time;
                    var item = new ListViewItem(new[] { time, row.File, row.Size.ToString(), row.DirectUrl }); item.Tag = row; table.Items.Add(item);
                }
                var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 80, Padding = new Padding(10) };
                var copyHistory = MakeButton("复制所选直链", 130, true);
                copyHistory.Click += delegate { if (table.SelectedItems.Count > 0) CopyLink(((UploadRecord)table.SelectedItems[0].Tag).DirectUrl); };
                var openPage = MakeButton("打开图片页", 120, false);
                openPage.Click += delegate {
                    if (table.SelectedItems.Count == 0) return;
                    string url = ((UploadRecord)table.SelectedItems[0].Tag).PageUrl;
                    Uri parsed;
                    if (Uri.TryCreate(url, UriKind.Absolute, out parsed) && parsed.Scheme == "https" && parsed.Host == "postimg.cc")
                        try { System.Diagnostics.Process.Start(url); } catch (Exception error) { MessageBox.Show(dialog, error.Message); }
                };
                table.DoubleClick += delegate { copyHistory.PerformClick(); };
                footer.Controls.Add(copyHistory); footer.Controls.Add(openPage);
                footer.Controls.Add(new Label { Text = "记录位置：" + History.FilePath, AutoSize = true, Margin = new Padding(0, 6, 0, 0) });
                dialog.Controls.Add(table); dialog.Controls.Add(footer);
                dialog.ShowDialog(this);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }
            base.Dispose(disposing);
        }
    }

    internal static class Diagnostics
    {
        private static byte[] CheckLargeImages(string directory)
        {
            byte[] uploadPng = null;
            using (var source = new Bitmap(1024, 1024, PixelFormat.Format32bppArgb))
            {
                var bits = source.LockBits(new Rectangle(0, 0, 1024, 1024), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] pixels = new byte[bits.Stride * 1024];
                    new Random(712).NextBytes(pixels);
                    for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                    Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
                }
                finally { source.UnlockBits(bits); }
                foreach (int size in new[] { 256, 512, 1024 })
                {
                    using (var cropped = CropImage.Render(source, new RectangleF(128, 128, 768, 768), size))
                    {
                        var png = CropImage.Png(cropped);
                        string base64 = Convert.ToBase64String(png);
                        if (base64.Length < 65536) throw new Exception("Regression image is too small to reproduce the old URI limit");
                        const string unicodeName = "裁剪图 + / = & % # 测试";
                        var fields = new Dictionary<string, string> { { "name", unicodeName }, { "image", base64 } };
                        using (var content = Postimages.CreateFormContent(fields))
                        {
                            if (content.Headers.ContentType.MediaType != "application/x-www-form-urlencoded")
                                throw new Exception("Wrong upload content type");
                            string encoded = content.ReadAsStringAsync().GetAwaiter().GetResult();
                            var decoded = encoded.Split('&').Select(field => field.Split(new[] { '=' }, 2))
                                .ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
                            if (decoded["name"] != unicodeName || decoded["image"] != base64)
                                throw new Exception("Large image / Unicode form data was corrupted: " + size);
                        }
                        if (size == 512) uploadPng = png;
                    }
                }
            }
            File.WriteAllBytes(Path.Combine(directory, "large-cropped.png"), uploadPng);
            return uploadPng;
        }

        public static int Run(string directory, bool upload)
        {
            Directory.CreateDirectory(directory);
            string report = Path.Combine(directory, upload ? "upload-test.json" : "self-test.json");
            try
            {
                byte[] largePng = CheckLargeImages(directory);
                using (var source = new Bitmap(800, 600, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(source))
                    {
                        g.Clear(Color.Transparent);
                        g.FillRectangle(Brushes.Coral, 200, 100, 400, 400);
                        g.FillEllipse(Brushes.Gold, 300, 200, 200, 200);
                    }
                    string imageFile = Path.Combine(directory, "synthetic-source.png"); source.Save(imageFile, ImageFormat.Png);
                    using (var output = CropImage.Render(source, new RectangleF(200, 100, 400, 400), 512))
                    {
                        if (output.Width != 512 || output.Height != 512 || output.GetPixel(256, 256).ToArgb() != Color.Gold.ToArgb())
                            throw new Exception("Crop geometry / resampling test failed");
                        var png = CropImage.Png(output); File.WriteAllBytes(Path.Combine(directory, "synthetic-cropped.png"), png);
                        using (var transparent = CropImage.Render(source, new RectangleF(0, 0, 100, 100), 256))
                            if (transparent.GetPixel(128, 128).A != 0) throw new Exception("Alpha preservation failed");
                        var parsed = Postimages.Parse("<data><links><hotlink>https://i.postimg.cc/test/test.png</hotlink><page>https://postimg.cc/test</page></links></data>");
                        bool errorRejected = false, hostRejected = false, dtdRejected = false;
                        try { Postimages.Parse("<data><error>bad key</error></data>"); } catch { errorRejected = true; }
                        try { Postimages.Parse("<data><links><hotlink>https://evil.invalid/image.png</hotlink></links></data>"); } catch { hostRejected = true; }
                        try { Postimages.Parse("<!DOCTYPE data [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><data>&x;</data>"); } catch { dtdRejected = true; }
                        if (!errorRejected || !hostRejected || !dtdRejected) throw new Exception("Response validation failed");
                        byte[] payload = upload ? largePng : png;
                        if (upload) parsed = Postimages.Upload(payload, "thememaker-desktop-large-diagnostic", "").GetAwaiter().GetResult();
                        var record = new UploadRecord { Time = DateTimeOffset.Now.ToString("o"), File = upload ? "large-cropped.png" : "synthetic-cropped.png", Size = 512,
                            Sha256 = CropImage.Hash(payload), DirectUrl = parsed.DirectUrl, PageUrl = parsed.PageUrl, DeleteUrl = parsed.DeleteUrl };
                        string history = Path.Combine(directory, "test-history.jsonl"); History.Append(history, record);
                        if (History.Read(history)[0].Sha256 != record.Sha256) throw new Exception("History round trip failed");
                        using (var form = new MainForm())
                        {
                            form.LoadImageFile(imageFile);
                            form.StartPosition = FormStartPosition.Manual;
                            form.Location = new Point(-16000, -16000);
                            form.ShowInTaskbar = false;
                            form.Show();
                            Application.DoEvents();
                            using (var preview = new Bitmap(form.Width, form.Height))
                            { form.DrawToBitmap(preview, new Rectangle(0, 0, form.Width, form.Height)); preview.Save(Path.Combine(directory, "preview.png"), ImageFormat.Png); }
                            form.Hide();
                        }
                        File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { ok = true, version = "1.0.2", upload = upload, cropSize = 512,
                            largeFormSizes = new[] { 256, 512, 1024 }, largeImageBytes = largePng.Length, largeFormRoundTrip = true,
                            sha256 = record.Sha256, directUrl = parsed.DirectUrl, pageUrl = parsed.PageUrl, alphaPreserved = true,
                            responseValidation = true, historyRoundTrip = true }), new UTF8Encoding(false));
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(report, new JavaScriptSerializer().Serialize(new { ok = false, error = error.ToString() }), new UTF8Encoding(false));
                return 1;
            }
        }
    }
}
