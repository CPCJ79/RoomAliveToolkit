using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

namespace RoomAliveToolkit
{
    unsafe public class ShortImage : UnmanagedImage
    {
        protected ushort* data;

        public ShortImage(int width, int height)
            : base(width, height, sizeof(short))
        {
            data = (ushort*)dataIntPtr.ToPointer();
            //Zero();
        }

        public ShortImage(int width, int height, IntPtr dataIntPtr)
            : base(width, height, dataIntPtr, sizeof(short))
        {
            data = (ushort*)dataIntPtr.ToPointer();
        }

        public ushort* Data(int x, int y)
        {
            return &data[width * y + x];
        }

        public ushort this[int x, int y]
        {
            get { return data[y * width + x]; }
            set { data[y * width + x] = value; }
        }

        public ushort this[int i]
        {
            get { return data[i]; }
            set { data[i] = value; }
        }

        public void SetTo(ushort value)
        {
            ushort* p = data;
            for (int i = 0; i < width * height; i++)
                *p++ = value;
        }

        public void Add(ShortImage other)
        {
            ushort* p = data;
            ushort* po = other.data;
            for (int i = 0; i < width * height; i++)
                *p++ += *po++;
        }

        public void Add(ByteImage other)
        {
            ushort* p = data;
            byte* po = other.Data(0, 0);
            for (int i = 0; i < width * height; i++)
                *p++ += *po++;
        }

        public void Copy(FloatImage source)
        {
            ushort* p = data;
            float* ps = source.Data(0, 0);
            for (int i = 0; i < width * height; i++)
                *p++ = (ushort)*ps++;
        }

        public void Blur5x5NonZero(ShortImage a)
        {
            ushort* output = data;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sum = 0;
                    int count = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height) continue;
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= width) continue;
                            ushort val = a.data[ny * width + nx];
                            if (val != 0)
                            {
                                sum += val;
                                count++;
                            }
                        }
                    }
                    *output++ = (count > 0) ? (ushort)(sum / count) : (ushort)0;
                }
            }
        }

        public void XMirror(ShortImage source)
        {
            for (int y = 0; y < height; y++)
            {
                ushort* pSrc = source.data + y * width;
                ushort* pDst = data + y * width + width - 1;
                for (int x = 0; x < width; x++)
                    *pDst-- = *pSrc++;
            }
        }

        public void XMirror_YUYSpecial(ShortImage source)
        {
            // YUY2 format: pairs of pixels share chroma (YUYV YUYV ...)
            // When mirroring, we swap pixel pairs and also swap Y values within each pair
            for (int y = 0; y < height; y++)
            {
                ushort* pSrc = source.data + y * width;
                ushort* pDst = data + y * width + width - 2;
                for (int x = 0; x < width; x += 2)
                {
                    pDst[0] = pSrc[1];
                    pDst[1] = pSrc[0];
                    pSrc += 2;
                    pDst -= 2;
                }
            }
        }
    }
}