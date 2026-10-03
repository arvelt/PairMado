using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
#if VERIFY
        if (args.Length == 2 && args[0] == "--verify") {
            try { Verification.Run(args[1]); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "verification.txt"), ex.ToString()); return 1; }
        }
#endif
        Application.Run(new CompareWindow(args));
        return 0;
    }
}

static class ImageFiles
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);
    static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".avif", ".wdp", ".jxr"
    };
    public static List<FileInfo> Scan(string folder) {
        return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(f => Extensions.Contains(f.Extension)).ToList();
    }
    public static void Sort(List<FileInfo> files, int mode) {
        files.Sort(delegate(FileInfo a, FileInfo b) {
            int c;
            if (mode / 2 == 1) c = a.CreationTimeUtc.CompareTo(b.CreationTimeUtc);
            else if (mode / 2 == 2) c = a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc);
            else c = StrCmpLogicalW(a.Name, b.Name);
            if (c == 0) c = StrCmpLogicalW(a.Name, b.Name);
            if (c == 0) c = StringComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName);
            return mode % 2 == 0 ? c : -Math.Sign(c);
        });
    }
    public static Bitmap Read(string path) {
        // メモリに読み込み、表示中も元ファイルをロックしません。
        byte[] bytes = File.ReadAllBytes(path);
        try {
            using (var stream = new MemoryStream(bytes))
            using (var raw = Image.FromStream(stream, true, true)) {
                if (raw.PropertyIdList.Contains(0x112)) {
                    int orientation = raw.GetPropertyItem(0x112).Value[0];
                    switch (orientation) {
                        case 2: raw.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                        case 3: raw.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                        case 4: raw.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                        case 5: raw.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                        case 6: raw.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                        case 7: raw.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                        case 8: raw.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                    }
                }
                return new Bitmap(raw);
            }
        } catch (ArgumentException) {
            // Windowsに追加された画像コーデックも利用します。
            using (var stream = new MemoryStream(bytes)) {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
                using (var png = new MemoryStream()) {
                    encoder.Save(png); png.Position = 0;
                    using (var raw = Image.FromStream(png)) return new Bitmap(raw);
                }
            }
        }
    }
}

class ImageSurface : Control
{
    Image image;
    public bool Highlight;
    public ImageSurface() {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(19, 25, 33);
        ForeColor = Color.FromArgb(170, 185, 204);
        Dock = DockStyle.Fill;
    }
    public void Replace(Image next) {
        Image previous = image; image = next;
        if (previous != null) previous.Dispose();
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if (image != null) {
            double scale = Math.Min((double)Math.Max(1, Width - 16) / image.Width, (double)Math.Max(1, Height - 16) / image.Height);
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(image, new Rectangle((Width - w) / 2, (Height - h) / 2, w, h));
        } else {
            TextRenderer.DrawText(e.Graphics, "画像をここにドロップ\nまたは「画像を選ぶ」", Font, ClientRectangle,
                ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
        if (Highlight) using (var pen = new Pen(Color.FromArgb(115, 183, 255), 4))
            e.Graphics.DrawRectangle(pen, 2, 2, Math.Max(0, Width - 5), Math.Max(0, Height - 5));
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    protected override void Dispose(bool disposing) {
        if (disposing && image != null) { image.Dispose(); image = null; }
        base.Dispose(disposing);
    }
}

class ComparePane : UserControl
{
    readonly ImageSurface surface = new ImageSurface();
    readonly Label filename = new Label();
    readonly Label details = new Label();
    readonly ToolTip tips = new ToolTip();
    readonly Button previous, next;
    readonly ComboBox order = new ComboBox();
    List<FileInfo> files = new List<FileInfo>();
    public string CurrentPath { get; private set; }
    public int Mode { get { return order.SelectedIndex; } set { order.SelectedIndex = value; } }
    public int Count { get { return files.Count; } }
    public bool CanPrevious { get { return previous.Enabled; } }
    public bool CanNext { get { return next.Enabled; } }
    public string Message { get { return details.Text; } }

    public ComparePane(string title) {
        Dock = DockStyle.Fill;
        Margin = new Padding(6);
        BackColor = Color.FromArgb(30, 38, 49);
        ForeColor = Color.FromArgb(233, 237, 242);
        Font = new Font("Yu Gothic UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
        var open = MakeButton("画像を選ぶ", 110);
        open.Click += delegate {
            using (var dialog = new OpenFileDialog()) {
                dialog.Title = title + "を選ぶ";
                dialog.Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.webp;*.avif;*.wdp;*.jxr|すべてのファイル|*.*";
                if (CurrentPath != null) dialog.InitialDirectory = Path.GetDirectoryName(CurrentPath);
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.FileName);
            }
        };
        header.Controls.Add(open, 1, 0);
        layout.Controls.Add(header, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        previous = MakeButton("←", 42); next = MakeButton("→", 42);
        previous.AccessibleName = title + "：前の画像"; next.AccessibleName = title + "：次の画像";
        tips.SetToolTip(previous, "前の画像"); tips.SetToolTip(next, "次の画像");
        previous.Click += delegate { Navigate(-1); }; next.Click += delegate { Navigate(1); };
        previous.Enabled = next.Enabled = false;
        order.DropDownStyle = ComboBoxStyle.DropDownList;
        order.Width = 224; order.Margin = new Padding(9, 4, 0, 0);
        order.AccessibleName = title + "：並べ替え";
        order.Items.AddRange(new object[] { "名前：昇順", "名前：降順", "作成日時：古い順", "作成日時：新しい順", "更新日時：古い順", "更新日時：新しい順" });
        order.SelectedIndex = 0;
        order.SelectedIndexChanged += delegate {
            try { RefreshFiles(); UpdateInformation(); }
            catch (Exception ex) { ShowError(ex); }
        };
        toolbar.Controls.Add(previous); toolbar.Controls.Add(next); toolbar.Controls.Add(order);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(surface, 0, 2);
        filename.Dock = details.Dock = DockStyle.Fill;
        filename.TextAlign = details.TextAlign = ContentAlignment.MiddleLeft;
        filename.Font = details.Font = new Font("Yu Gothic UI", 9);
        filename.AutoEllipsis = details.AutoEllipsis = true;
        filename.Text = "画像未選択";
        details.ForeColor = Color.FromArgb(162, 180, 201);
        layout.Controls.Add(filename, 0, 3); layout.Controls.Add(details, 0, 4);
        Controls.Add(layout);
        AttachDrop(this);
    }
    Button MakeButton(string text, int width) {
        return new Button { Text = text, Width = width, Height = 30, Margin = new Padding(0, 2, 6, 0),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 61, 80), ForeColor = ForeColor, UseVisualStyleBackColor = false };
    }
    void AttachDrop(Control control) {
        control.AllowDrop = true;
        control.DragEnter += delegate(object sender, DragEventArgs e) {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            surface.Highlight = e.Effect != DragDropEffects.None; surface.Invalidate();
        };
        control.DragLeave += delegate { surface.Highlight = false; surface.Invalidate(); };
        control.DragDrop += delegate(object sender, DragEventArgs e) {
            surface.Highlight = false; surface.Invalidate();
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null && paths.Length == 1 && File.Exists(paths[0])) OpenPath(paths[0]);
            else SetMessage("各枠に画像ファイルを1枚ずつドロップしてください。", true);
        };
        foreach (Control child in control.Controls) AttachDrop(child);
    }
    void RefreshFiles() {
        if (CurrentPath == null) return;
        files = ImageFiles.Scan(Path.GetDirectoryName(CurrentPath));
        ImageFiles.Sort(files, Mode);
    }
    public bool OpenPath(string path) {
        Bitmap bitmap = null;
        try {
            path = Path.GetFullPath(path);
            bitmap = ImageFiles.Read(path);
            var scanned = ImageFiles.Scan(Path.GetDirectoryName(path));
            if (!scanned.Any(f => StringComparer.OrdinalIgnoreCase.Equals(f.FullName, path))) scanned.Add(new FileInfo(path));
            ImageFiles.Sort(scanned, Mode);
            files = scanned; CurrentPath = path;
            surface.Replace(bitmap); bitmap = null;
            UpdateInformation();
            return true;
        } catch (Exception ex) { ShowError(ex); return false; }
        finally { if (bitmap != null) bitmap.Dispose(); }
    }
    public void Navigate(int delta) {
        if (CurrentPath == null) return;
        try {
            RefreshFiles();
            int index = files.FindIndex(f => StringComparer.OrdinalIgnoreCase.Equals(f.FullName, CurrentPath));
            if (index < 0) { UpdateInformation(); SetMessage("表示中のファイルが移動または削除されています。画像を選び直してください。", true); return; }
            int target = index + delta;
            if (target >= 0 && target < files.Count) OpenPath(files[target].FullName);
            else UpdateInformation();
        } catch (Exception ex) { ShowError(ex); }
    }
    void UpdateInformation() {
        int index = files.FindIndex(f => StringComparer.OrdinalIgnoreCase.Equals(f.FullName, CurrentPath));
        previous.Enabled = index > 0;
        next.Enabled = index >= 0 && index < files.Count - 1;
        if (CurrentPath == null) return;
        filename.Text = (index < 0 ? "—" : (index + 1).ToString()) + " / " + files.Count + "    " + Path.GetFileName(CurrentPath);
        tips.SetToolTip(filename, CurrentPath);
        var info = new FileInfo(CurrentPath);
        SetMessage(info.Exists ? "作成 " + info.CreationTime.ToString("yyyy/MM/dd HH:mm:ss") + "   更新 " + info.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss") : "表示中のファイルが見つかりません。", false);
        tips.SetToolTip(details, details.Text + "\n" + Path.GetDirectoryName(CurrentPath));
    }
    void SetMessage(string text, bool error) { details.Text = text; details.ForeColor = error ? Color.FromArgb(255, 184, 135) : Color.FromArgb(162, 180, 201); tips.SetToolTip(details, text); }
    void ShowError(Exception ex) {
        string text = ex is UnauthorizedAccessException ? "このフォルダまたはファイルを読み取れません。" :
            ex is IOException ? "ファイルを読み取れません。移動・削除や使用状況をご確認ください。" : "この画像形式は表示できないか、画像が破損しています。";
        SetMessage(text, true);
        tips.SetToolTip(details, text + "\n" + ex.Message);
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}

class CompareWindow : Form
{
    public CompareWindow(string[] paths) {
        Text = "PairMado";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1200, 780); MinimumSize = new Size(840, 440);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(16, 20, 25); ForeColor = Color.FromArgb(233, 237, 242);
        Font = new Font("Yu Gothic UI", 10);
        string icon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "image-compare.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Text = "PairMado　　各枠に画像をドロップ  ·  ← → で同じフォルダ内を移動", AutoEllipsis = true };
        layout.Controls.Add(heading, 0, 0);
        var panes = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panes.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var left = new ComparePane("左の画像"); var right = new ComparePane("右の画像");
        panes.Controls.Add(left, 0, 0); panes.Controls.Add(right, 1, 0);
        layout.Controls.Add(panes, 0, 1); Controls.Add(layout);
        Shown += delegate {
            if (paths.Length > 0) left.OpenPath(paths[0]);
            if (paths.Length > 1) right.OpenPath(paths[1]);
        };
    }
}
