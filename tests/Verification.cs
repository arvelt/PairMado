using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Media.Imaging;

static class Verification
{
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static void Run(string root) {
        Directory.CreateDirectory(root);
        string folder = Path.Combine(root, "fixtures"); Directory.CreateDirectory(folder);
        string other = Path.Combine(root, "other"); Directory.CreateDirectory(other);
        string sub = Path.Combine(folder, "sub"); Directory.CreateDirectory(sub);
        string[] names = { "image2.png", "image10.png", "image1.png" };
        for (int i = 0; i < names.Length; i++) {
            string path = Path.Combine(folder, names[i]);
            using (var b = new Bitmap(180 + i * 60, 260 - i * 40)) {
                using (var g = Graphics.FromImage(b)) { g.Clear(new Color[] { Color.Teal, Color.Coral, Color.SteelBlue }[i]); g.DrawString(names[i], SystemFonts.DefaultFont, Brushes.White, 10, 10); }
                b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            File.SetCreationTimeUtc(path, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i));
            File.SetLastWriteTimeUtc(path, new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(2 - i));
        }
        File.WriteAllText(Path.Combine(folder, "note.txt"), "ignored");
        File.Copy(Path.Combine(folder, names[0]), Path.Combine(sub, "nested.png"), true);
        string otherPath = Path.Combine(other, "other.png"); File.Copy(Path.Combine(folder, names[0]), otherPath, true);
        string[][] expected = {
            new[] { "image1.png", "image2.png", "image10.png" }, new[] { "image10.png", "image2.png", "image1.png" },
            new[] { "image2.png", "image10.png", "image1.png" }, new[] { "image1.png", "image10.png", "image2.png" },
            new[] { "image1.png", "image10.png", "image2.png" }, new[] { "image2.png", "image10.png", "image1.png" }
        };
        for (int mode = 0; mode < 6; mode++) {
            var files = ImageFiles.Scan(folder); ImageFiles.Sort(files, mode);
            Assert(files.Select(f => f.Name).SequenceEqual(expected[mode]), "Sort mode " + mode);
        }
        using (var left = new ComparePane("左")) using (var right = new ComparePane("右")) {
            Assert(left.OpenPath(Path.Combine(folder, "image2.png")), "Open PNG");
            Assert(right.OpenPath(otherPath), "Open independent folder");
            Assert(left.Count == 3 && right.Count == 1, "Sibling filtering");
            left.Navigate(1); Assert(Path.GetFileName(left.CurrentPath) == "image10.png", "Next");
            Assert(!left.CanNext, "End disabled"); left.Navigate(1); Assert(Path.GetFileName(left.CurrentPath) == "image10.png", "No wrap");
            left.Navigate(-1); Assert(Path.GetFileName(left.CurrentPath) == "image2.png", "Previous");
            for (int mode = 0; mode < 6; mode++) { left.Mode = mode; Assert(Path.GetFileName(left.CurrentPath) == "image2.png", "Sort retains current"); }
            Assert(right.CurrentPath == otherPath, "Independent panes");
            Assert(!left.OpenPath(Path.Combine(folder, "note.txt")), "Reject non-image");
            Assert(Path.GetFileName(left.CurrentPath) == "image2.png", "Error retains current");
            string added = Path.Combine(folder, "image3.png"); File.Copy(otherPath, added, true);
            left.Mode = 0; Assert(left.Count == 4, "Refresh after folder changes");
            File.Delete(added);
            // 表示中の画像を開いていても元ファイルはロックされません。
            using (var access = new FileStream(left.CurrentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        File.WriteAllText(Path.Combine(root, "verification.txt"), "PASS: six sorts; natural filename order; sibling-only filtering; next/previous and boundaries; current-image preservation; independent folders; load-error preservation; folder refresh; source file unlocked.");
    }
}
