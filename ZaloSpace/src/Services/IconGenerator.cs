using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ZaloSpaceManager.Services
{
    public static class IconGenerator
    {
        public static string GenerateProfileIcon(string zExePath, string avatarPath, string profileId, string outputDir)
        {
            try
            {
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                string outIcoPath = Path.Combine(outputDir, string.Format("profile_{0}.ico", profileId));

                int iconSize = 256;
                using (Bitmap finalCanvas = new Bitmap(iconSize, iconSize, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(finalCanvas))
                    {
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        if (!string.IsNullOrEmpty(avatarPath) && File.Exists(avatarPath))
                        {
                            // "icon app clone thì lấy của user_zalo luôn"
                            // Draw clean user avatar squircle without any fake badges in corners
                            // so that Windows & Zalo real notification badges (unread message counts) can display cleanly!
                            byte[] imgRaw = File.ReadAllBytes(avatarPath);
                            using (MemoryStream imgMs = new MemoryStream(imgRaw))
                            using (Image userImg = Image.FromStream(imgMs))
                            {
                                int minDim = Math.Min(userImg.Width, userImg.Height);
                                int srcX = (userImg.Width - minDim) / 2;
                                int srcY = (userImg.Height - minDim) / 2;
                                Rectangle srcRect = new Rectangle(srcX, srcY, minDim, minDim);

                                // Modern rounded squircle path
                                int radius = 52;
                                Rectangle dstRect = new Rectangle(10, 10, iconSize - 20, iconSize - 20);
                                using (GraphicsPath path = GetRoundedRect(dstRect, radius))
                                {
                                    g.SetClip(path);
                                    g.DrawImage(userImg, dstRect, srcRect, GraphicsUnit.Pixel);
                                    g.ResetClip();

                                    // Crisp outer border
                                    using (Pen p = new Pen(Color.FromArgb(180, 255, 255, 255), 4))
                                    {
                                        g.DrawPath(p, path);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Fallback Zalo icon without fake corner badges
                            using (Bitmap baseIcon = CreateFallbackZaloIcon(iconSize, iconSize, profileId))
                            {
                                g.DrawImage(baseIcon, 0, 0, iconSize, iconSize);
                            }
                        }
                    }

                    // Save as multi-resolution .ico format for full Explorer and Taskbar support
                    SaveMultiResIcon(finalCanvas, outIcoPath);
                }

                return outIcoPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error generating icon: " + ex.Message);
                return null;
            }
        }

        private static GraphicsPath GetRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Bitmap CreateFallbackZaloIcon(int width, int height, string label)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (LinearGradientBrush bg = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(0, 130, 255), Color.FromArgb(0, 90, 230), 45f))
                {
                    g.FillEllipse(bg, 16, 16, width - 32, height - 32);
                }

                // If clone profile without avatar, show "Zalo #2" centered inside icon without corner badges
                if (label != "1" && !string.IsNullOrEmpty(label))
                {
                    using (Font f1 = new Font("Arial", width * 0.20f, FontStyle.Bold))
                    using (Font f2 = new Font("Arial", width * 0.16f, FontStyle.Bold))
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(220, 240, 255)))
                    {
                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("Zalo", f1, textBrush, new RectangleF(0, (float)(height * 0.22), width, (float)(height * 0.3)), sf);
                        g.DrawString("#" + label, f2, subBrush, new RectangleF(0, (float)(height * 0.52), width, (float)(height * 0.25)), sf);
                    }
                }
                else
                {
                    using (Font f = new Font("Arial", width * 0.28f, FontStyle.Bold))
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    {
                        StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("Zalo", f, textBrush, new RectangleF(0, 0, width, height), sf);
                    }
                }
            }
            return bmp;
        }

        public static void SaveMultiResIcon(Bitmap source256, string outPath)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
            List<byte[]> images = new List<byte[]>();

            foreach (int size in sizes)
            {
                using (Bitmap resized = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(source256, 0, 0, size, size);
                    }

                    if (size == 256)
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            resized.Save(ms, ImageFormat.Png);
                            images.Add(ms.ToArray());
                        }
                    }
                    else
                    {
                        images.Add(CreateDibIconEntry(resized));
                    }
                }
            }

            using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write((short)0); // reserved
                bw.Write((short)1); // type 1 = icon
                bw.Write((short)sizes.Length); // count

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    byte[] data = images[i];
                    bw.Write((byte)(s >= 256 ? 0 : s)); // width
                    bw.Write((byte)(s >= 256 ? 0 : s)); // height
                    bw.Write((byte)0); // colors
                    bw.Write((byte)0); // reserved
                    bw.Write((short)1); // color planes
                    bw.Write((short)32); // bpp
                    bw.Write((int)data.Length);
                    bw.Write((int)offset);
                    offset += data.Length;
                }

                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write(images[i]);
                }
            }
        }

        private static byte[] CreateDibIconEntry(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            int xorSize = width * height * 4;
            int andRowBytes = ((width + 31) / 32) * 4;
            int andSize = andRowBytes * height;
            int headerSize = 40;
            byte[] dib = new byte[headerSize + xorSize + andSize];

            using (MemoryStream ms = new MemoryStream(dib))
            using (BinaryWriter bw = new BinaryWriter(ms))
            {
                // BITMAPINFOHEADER
                bw.Write((int)40); // biSize
                bw.Write((int)width);
                bw.Write((int)(height * 2)); // icon height = height * 2 (XOR + AND)
                bw.Write((short)1); // planes
                bw.Write((short)32); // bitCount
                bw.Write((int)0); // BI_RGB
                bw.Write((int)(xorSize + andSize)); // biSizeImage
                bw.Write((int)0);
                bw.Write((int)0);
                bw.Write((int)0);
                bw.Write((int)0);

                // Bottom-up 32bpp BGRA pixels
                BitmapData bData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] row = new byte[width * 4];
                for (int y = height - 1; y >= 0; y--)
                {
                    IntPtr srcRow = new IntPtr(bData.Scan0.ToInt64() + y * bData.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(srcRow, row, 0, row.Length);
                    bw.Write(row);
                }
                bmp.UnlockBits(bData);

                // AND mask (all zeroes for 32bpp alpha)
                byte[] andMask = new byte[andSize];
                bw.Write(andMask);
            }
            return dib;
        }
    }
}
