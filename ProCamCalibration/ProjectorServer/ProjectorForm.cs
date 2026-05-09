using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Vortice.DXGI;
using Vortice.DCommon;
using RoomAliveToolkit;

namespace RoomAliveToolkit
{
    public partial class ProjectorForm : Form
    {
        ID2D1Factory factory = D2D1.D2D1CreateFactory<ID2D1Factory>();
        IDWriteFactory directWriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>();
        ID2D1HwndRenderTarget renderTarget;
        GrayCode grayCode;
        ARGBImage[] grayCodeImages;
        ID2D1Bitmap bitmap;
        int screenIndex;
        System.Drawing.Rectangle bounds;
        IDWriteTextFormat textFormat;
        ID2D1SolidColorBrush solidColorBrush;

        public ProjectorForm(int screenIndex)
        {
            InitializeComponent();
            this.screenIndex = screenIndex;
            ShowInTaskbar = false;

            FormBorderStyle = FormBorderStyle.None;


            // assumes that taskbar is not displayed on every display

            bounds = Screen.AllScreens[screenIndex].Bounds;
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(bounds.X, bounds.Y);
            Size = new System.Drawing.Size(bounds.Width, bounds.Height);

            // Gray code
            grayCode = new GrayCode(bounds.Width, bounds.Height);
            grayCodeImages = grayCode.Generate();


        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Direct2D
            var renderTargetProperties = new RenderTargetProperties()
            {
                PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore)
            };
            var hwndRenderTargetProperties = new HwndRenderTargetProperties()
            {
                Hwnd = this.Handle,
                PixelSize = new SizeI(bounds.Width, bounds.Height),
                PresentOptions = PresentOptions.Immediately,
            };
            renderTarget = factory.CreateHwndRenderTarget(renderTargetProperties, hwndRenderTargetProperties);

            var bitmapProperties = new BitmapProperties()
            {
                PixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Ignore)
            };
            bitmap = renderTarget.CreateBitmap(new SizeI(bounds.Width, bounds.Height), bitmapProperties);

            textFormat = directWriteFactory.CreateTextFormat("Arial", FontWeight.Normal, Vortice.DirectWrite.FontStyle.Normal, 96.0f);
            textFormat.ParagraphAlignment = ParagraphAlignment.Center;
            textFormat.TextAlignment = TextAlignment.Center;

            solidColorBrush = renderTarget.CreateSolidColorBrush(new Color4(1.0f, 1.0f, 1.0f, 1.0f));
        }

        public int NumberOfGrayCodeImages
        {
            get { return grayCodeImages.Length; }
        }

        public void DisplayGrayCode(int i)
        {
            var image = grayCodeImages[i];
            bitmap.CopyFromMemory(image.DataIntPtr, (uint)(image.Width * 4));
            renderTarget.BeginDraw();
            renderTarget.DrawBitmap(bitmap, 1.0f, BitmapInterpolationMode.Linear);
            renderTarget.EndDraw();
            //Console.WriteLine("displaying Gray code " + i);
        }

        public void DisplayName(string name)
        {
            var brush = renderTarget.CreateSolidColorBrush(new Color4(1.0f, 1.0f, 1.0f, 1.0f));
            var layoutRect = new Vortice.RawRectF(0, 0, bounds.Width, bounds.Height);

            renderTarget.BeginDraw();
            renderTarget.Clear(new Color4(0.0f, 0.0f, 0.0f, 1.0f));
            renderTarget.DrawRectangle(new Vortice.RawRectF(0, 0, bounds.Width, bounds.Height), brush, 10f);
            renderTarget.DrawText(name, textFormat, layoutRect, solidColorBrush);

            //int nx = 4;
            //int ny = 4;
            //for (int i = 0; i < nx - 1; i++)
            //    for (int j = 0; j < ny - 1; j++)
            //    {
            //        float x = (float)bounds.Width / (float)nx * (i + 1);
            //        float y = (float)bounds.Height / (float)ny * (j + 1);

            //        const int w = 10;
            //        renderTarget.DrawLine(new Vector2(x - w, y), new Vector2(x + w, y), brush, 1);
            //        renderTarget.DrawLine(new Vector2(x, y - w), new Vector2(x, y + w), brush, 1);
            //    }

            int nx = 4;
            int ny = 4;
            for (int i = 0; i < nx - 1; i++)
            {
                float x = (float)bounds.Width / (float)nx * (i + 1);
                renderTarget.DrawLine(new Vector2(x, 0), new Vector2(x, bounds.Height), brush, 1);
            }
            for (int i = 0; i < ny - 1; i++)
            {
                float y = (float)bounds.Height / (float)ny * (i + 1);
                renderTarget.DrawLine(new Vector2(0, y), new Vector2(bounds.Width, y), brush, 1);
            }
            renderTarget.EndDraw();

            brush.Dispose();
        }

        public void SetColor(float r, float g, float b)
        {
            renderTarget.BeginDraw();
            renderTarget.Clear(new Color4(r, g, b, 1.0f));
            renderTarget.EndDraw();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            bitmap.Dispose();
            renderTarget.Dispose();
            for (int i = 0; i < grayCodeImages.Length; i++)
                grayCodeImages[i].Dispose();
        }

    }
}
